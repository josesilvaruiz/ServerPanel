using System.Net.Http.Json;
using System.Text.RegularExpressions;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

// Vigila el log de producción en busca de la firma "UNEXPECTED LONG FRAME DETECTED" que el
// propio motor imprime cuando el hilo principal se congela más de lo que su propio sistema de
// amnistía tolera (visto en vivo: congelamientos de 1.3-2.5s atribuidos casi al 100% a "Server
// Simulation" — lógica de juego/plugins, no red/física). Si vuelve a pasar, avisa por email.
public class TickStallAlertBackgroundService : BackgroundService
{
    // Ventana de log revisada cada poll; algo mayor que el intervalo para no perder líneas
    // si un poll se retrasa por cualquier motivo.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LogWindow = TimeSpan.FromSeconds(30);

    // Evita mandar un email por cada línea si el problema se repite en ráfaga — agrupa
    // todo lo visto en esta ventana y solo re-avisa pasado este tiempo.
    private static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(10);

    private static readonly Regex LongFramePattern = new(
        @"UNEXPECTED LONG FRAME DETECTED:\s*([\d.]+)ms elapsed", RegexOptions.Compiled);

    private readonly IActiveServerService _activeServer;
    private readonly ISshService _ssh;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TickStallAlertBackgroundService> _logger;

    private DateTime _lastAlertUtc = DateTime.MinValue;

    public TickStallAlertBackgroundService(
        IActiveServerService activeServer,
        ISshService ssh,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<TickStallAlertBackgroundService> logger)
    {
        _activeServer = activeServer;
        _ssh = ssh;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TickStallAlert started. Interval: {Interval}s", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task CheckAsync(CancellationToken ct)
    {
        var serverName = _config["Notifications:ProductionServerName"] ?? "Producción";
        var production = _activeServer.Servers.FirstOrDefault(s => s.Name == serverName);
        if (production is null) return;

        try
        {
            var podName = await ResolvePodNameAsync(production);
            if (podName is null) return;

            var sinceSeconds = (int)LogWindow.TotalSeconds;
            var logs = await _ssh.ExecuteAsync(
                $"kubectl logs {podName} -n {production.KubeNamespace} --since={sinceSeconds}s 2>/dev/null " +
                "| grep 'UNEXPECTED LONG FRAME DETECTED'");

            var matches = LongFramePattern.Matches(logs);
            if (matches.Count == 0) return;

            var elapsedMs = matches
                .Select(m => double.TryParse(m.Groups[1].Value, out var v) ? v : 0)
                .ToList();
            var worst = elapsedMs.Max();

            _logger.LogWarning(
                "TickStallAlert: {Count} congelamiento(s) detectado(s) en producción, peor caso {Worst}ms",
                elapsedMs.Count, worst);

            if (DateTime.UtcNow - _lastAlertUtc < AlertCooldown)
                return; // ya se avisó recientemente de esta misma racha

            _lastAlertUtc = DateTime.UtcNow;
            await SendAlertEmailAsync(production, elapsedMs.Count, worst, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TickStallAlert: error comprobando el log de producción");
        }
    }

    // Mismo mecanismo que ProductionDownAlertBackgroundService: el label del pod no es
    // necesariamente "app=<KubeDeployment>", hay que leerlo del propio deployment.
    private async Task<string?> ResolvePodNameAsync(ServerConfig production)
    {
        var selector = (await _ssh.ExecuteAsync(
            $"kubectl get deployment {production.KubeDeployment} -n {production.KubeNamespace} " +
            "-o jsonpath='{.spec.selector.matchLabels.app}'")).Trim();

        if (string.IsNullOrWhiteSpace(selector))
            return null;

        var podNameRaw = (await _ssh.ExecuteAsync(
            $"kubectl get pods -n {production.KubeNamespace} -l app={selector} " +
            "--sort-by=.metadata.creationTimestamp -o jsonpath='{.items[-1:].metadata.name}'")).Trim();

        var podName = podNameRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
        var looksValid = podName.Length > 0 && !podName.Contains(' ') && podNameRaw.Count(c => c == '\n') == 0;

        return looksValid ? podName : null;
    }

    private async Task SendAlertEmailAsync(ServerConfig production, int count, double worstMs, CancellationToken ct)
    {
        var baseUrl = _config["Notifications:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("TickStallAlert: Notifications:BaseUrl no configurado, no se envía el email");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                userId = $"CS2-{production.Name}",
                message =
                    $"El servidor CS2 '{production.Name}' ha tenido congelamientos de tick (lag).\n" +
                    $"Hora (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}\n" +
                    $"Congelamientos detectados en esta racha: {count}\n" +
                    $"Peor caso: {worstMs:0}ms (el motor lo marca como 'UNEXPECTED LONG FRAME' — no es un pico normal)\n\n" +
                    "Revisa el log del pod para más detalle (\"Framerate spike report\" justo después de cada línea).",
                channel = 0 // Email
            };

            var response = await client.PostAsJsonAsync($"{baseUrl}/api/Notifications", payload, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("TickStallAlert: email de aviso de lag encolado para {Server}", production.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TickStallAlert: fallo llamando a la API de Notifications");
        }
    }
}
