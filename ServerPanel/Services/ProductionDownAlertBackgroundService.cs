using System.Net.Http.Json;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

// Vigila SIEMPRE el servidor "Producción", sin importar cuál esté seleccionado
// en la UI (IActiveServerService.Active es un puntero global que cualquiera puede
// cambiar). Independiente de Cs2MetricsCollectorBackgroundService a propósito: no
// altera las estadísticas que se guardan para lo que esté activo en el panel.
public class ProductionDownAlertBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    // Cubre el tiempo entre disparar un stop/restart y que el próximo poll (cada 1 min)
    // vea el servidor caído; si el manual action tracker lo marcó dentro de esta ventana,
    // se asume que la caída es intencional y no se avisa.
    private static readonly TimeSpan ManualActionGracePeriod = TimeSpan.FromMinutes(3);

    private readonly IServerQueryService _serverQuery;
    private readonly IActiveServerService _activeServer;
    private readonly IManualActionTracker _manualActionTracker;
    private readonly ISshService _ssh;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ProductionDownAlertBackgroundService> _logger;

    // null = todavía no sabemos el estado (arranque del panel): no alertar en el primer poll.
    private bool? _lastKnownOnline;

    public ProductionDownAlertBackgroundService(
        IServerQueryService serverQuery,
        IActiveServerService activeServer,
        IManualActionTracker manualActionTracker,
        ISshService ssh,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ProductionDownAlertBackgroundService> logger)
    {
        _serverQuery = serverQuery;
        _activeServer = activeServer;
        _manualActionTracker = manualActionTracker;
        _ssh = ssh;
        _httpClientFactory = httpClientFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductionDownAlert started. Interval: {Interval}s", Interval.TotalSeconds);

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
        if (production is null)
        {
            _logger.LogWarning("ProductionDownAlert: no se encontró el servidor '{Name}' en Servers", serverName);
            return;
        }

        bool isOnline;
        try
        {
            var info = await _serverQuery.GetServerInfoAsync(production);
            isOnline = info.IsOnline;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProductionDownAlert: error consultando {Host}:{Port}", production.Host, production.Port);
            isOnline = false;
        }

        // Solo alerta en la transición Online -> Offline: evita un email por cada
        // poll mientras el servidor sigue caído.
        if (_lastKnownOnline == true && !isOnline)
        {
            if (_manualActionTracker.WasRecentlyActedOn(production.Name, ManualActionGracePeriod))
            {
                _logger.LogInformation("ProductionDownAlert: {Name} caído tras una acción manual reciente, no se avisa", production.Name);
            }
            else
            {
                _logger.LogWarning("ProductionDownAlert: {Name} ({Host}:{Port}) ha dejado de responder", production.Name, production.Host, production.Port);
                await SendAlertAsync(production, ct);
            }
        }

        _lastKnownOnline = isOnline;
    }

    private async Task SendAlertAsync(ServerConfig production, CancellationToken ct)
    {
        var baseUrl = _config["Notifications:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("ProductionDownAlert: Notifications:BaseUrl no configurado, no se envía el email");
            return;
        }

        var crashedAtUtc = DateTime.UtcNow;
        var diagnostics = await GetCrashDiagnosticsAsync(production);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                userId = $"CS2-{production.Name}",
                message =
                    $"El servidor CS2 '{production.Name}' ha dejado de responder.\n" +
                    $"Hora de la caída (UTC): {crashedAtUtc:yyyy-MM-dd HH:mm:ss}\n\n" +
                    diagnostics,
                channel = 0 // Email
            };

            var response = await client.PostAsJsonAsync($"{baseUrl}/api/Notifications", payload, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("ProductionDownAlert: email de caída encolado para {Server}", production.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductionDownAlert: fallo llamando a la API de Notifications");
        }
    }

    // Diagnóstico best-effort: último estado conocido del pod (para pillar OOMKilled,
    // Error, exit code) + las últimas líneas de log del contenedor que se cayó
    // (--previous, porque si K8s ya reinició el pod, "kubectl logs" a secas
    // devolvería los logs de la instancia NUEVA, no la que crasheó).
    private async Task<string> GetCrashDiagnosticsAsync(ServerConfig production)
    {
        try
        {
            var podName = (await _ssh.ExecuteAsync(
                $"kubectl get pods -n {production.KubeNamespace} -l app={production.KubeDeployment} " +
                "-o jsonpath='{.items[0].metadata.name}'")).Trim();

            if (string.IsNullOrWhiteSpace(podName))
                return "No se pudo localizar el pod para diagnosticar la caída.";

            var stateInfo = (await _ssh.ExecuteAsync(
                $"kubectl describe pod {podName} -n {production.KubeNamespace} " +
                "| grep -E 'State:|Reason:|Exit Code:|Message:' | head -10")).Trim();

            var logs = (await _ssh.ExecuteAsync(
                $"kubectl logs {podName} -n {production.KubeNamespace} --previous --tail=40 2>/dev/null")).Trim();

            if (string.IsNullOrWhiteSpace(logs))
            {
                logs = (await _ssh.ExecuteAsync(
                    $"kubectl logs deployment/{production.KubeDeployment} -n {production.KubeNamespace} --tail=40 2>/dev/null")).Trim();
            }

            return
                $"Estado del pod:\n{(string.IsNullOrWhiteSpace(stateInfo) ? "(sin datos)" : stateInfo)}\n\n" +
                $"Últimas líneas de log:\n{(string.IsNullOrWhiteSpace(logs) ? "(sin logs disponibles)" : logs)}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProductionDownAlert: error obteniendo diagnóstico de la caída");
            return "No se pudo obtener el diagnóstico de la caída (error consultando kubectl).";
        }
    }
}
