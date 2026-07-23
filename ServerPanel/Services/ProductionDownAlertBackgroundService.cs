using System.Net.Http.Json;
using System.Text.Json;
using ServerPanel.Contracts;
using ServerPanel.Data;
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
    private readonly IServiceScopeFactory _scopeFactory;
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
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<ProductionDownAlertBackgroundService> logger)
    {
        _serverQuery = serverQuery;
        _activeServer = activeServer;
        _manualActionTracker = manualActionTracker;
        _ssh = ssh;
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
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
        var (diagnostics, logs) = await GetCrashDiagnosticsAsync(production);

        if (!string.IsNullOrWhiteSpace(logs))
            await TryRecordCrashedMapAsync(logs, production, crashedAtUtc);

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

    // Si el log coincide con la firma conocida (mapas con física/colisión rota,
    // ver CrashLogParser), se guarda el mapa (+ su Workshop ID si se puede
    // resolver) en una lista para poder sacarlo luego de la rotación/RTV.
    private async Task TryRecordCrashedMapAsync(string logs, ServerConfig production, DateTime crashedAtUtc)
    {
        try
        {
            if (!CrashLogParser.TryExtractCrashedMap(logs, out var mapName))
                return;

            var workshopId = await TryResolveWorkshopIdAsync(mapName, production);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.CrashedMapRecords.Add(new CrashedMapRecord
            {
                CrashedAtUtc = crashedAtUtc,
                MapName = mapName,
                WorkshopId = workshopId,
                ServerName = production.Name,
            });
            await db.SaveChangesAsync();

            _logger.LogWarning(
                "ProductionDownAlert: registrado crash por mapa '{Map}' (workshop {WorkshopId})",
                mapName, workshopId ?? "desconocido");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProductionDownAlert: error registrando el mapa que crasheó");
        }
    }

    // Busca el ID de Workshop del mapa en la config viva de CS2-SimpleAdmin (mismo
    // fichero que usa el picker de mapas del panel) — el JSON trae un comentario en
    // la primera línea, hay que saltarlo antes de parsear.
    private async Task<string?> TryResolveWorkshopIdAsync(string mapName, ServerConfig production)
    {
        try
        {
            var path = $"{production.KubeHostCssPath}/configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json";
            var raw = await _ssh.ExecuteAsync($"cat {path} 2>/dev/null");
            var start = raw.IndexOf('{');
            if (start < 0)
                return null;

            using var doc = JsonDocument.Parse(raw[start..]);
            if (!doc.RootElement.TryGetProperty("WorkshopMaps", out var workshopMaps))
                return null;

            foreach (var prop in workshopMaps.EnumerateObject())
            {
                if (string.Equals(prop.Name, mapName, StringComparison.OrdinalIgnoreCase))
                    return prop.Value.ToString();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProductionDownAlert: error resolviendo workshop id de '{Map}'", mapName);
            return null;
        }
    }

    // Diagnóstico best-effort: último estado conocido del pod (para pillar OOMKilled,
    // Error, exit code) + las últimas líneas de log del contenedor que se cayó
    // (--previous, porque si K8s ya reinició el pod, "kubectl logs" a secas
    // devolvería los logs de la instancia NUEVA, no la que crasheó).
    private async Task<(string Diagnostics, string? Logs)> GetCrashDiagnosticsAsync(ServerConfig production)
    {
        try
        {
            // El selector del pod NO es necesariamente "app=<KubeDeployment>" (p.ej.
            // producción usa app=cs2, no app=cs2-server) — hay que leerlo del propio
            // deployment en vez de asumirlo, o el filtro no encuentra nada.
            var selector = (await _ssh.ExecuteAsync(
                $"kubectl get deployment {production.KubeDeployment} -n {production.KubeNamespace} " +
                "-o jsonpath='{.spec.selector.matchLabels.app}'")).Trim();

            if (string.IsNullOrWhiteSpace(selector))
                return ($"No se pudo leer el selector del deployment '{production.KubeDeployment}' (¿existe en el namespace '{production.KubeNamespace}'?).", null);

            var podNameRaw = (await _ssh.ExecuteAsync(
                $"kubectl get pods -n {production.KubeNamespace} -l app={selector} " +
                "--sort-by=.metadata.creationTimestamp -o jsonpath='{.items[-1:].metadata.name}'")).Trim();

            // Validación defensiva: si kubectl falló, esto trae su mensaje de error
            // multilínea en vez de un nombre de pod. No lo pases nunca a otro comando
            // shell tal cual — se ejecutaría línea a línea como si fueran órdenes.
            var podName = podNameRaw.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            var looksValid = podName.Length > 0 && !podName.Contains(' ') && podNameRaw.Count(c => c == '\n') == 0;

            if (!looksValid)
            {
                return ($"No se pudo localizar el pod (selector 'app={selector}' en namespace '{production.KubeNamespace}').\n" +
                       $"Salida de kubectl: {(string.IsNullOrWhiteSpace(podNameRaw) ? "(vacía)" : podNameRaw)}", null);
            }

            var stateInfo = (await _ssh.ExecuteAsync(
                $"kubectl describe pod {podName} -n {production.KubeNamespace} " +
                "| grep -E 'State:|Reason:|Exit Code:|Message:' | head -10")).Trim();

            // 200 líneas para el análisis del crash (el patrón "Created physics for X"
            // puede quedar a 30-40 líneas del "Watchdog timeout" si hay ruido de
            // conexiones de clientes de por medio — con --tail=40 se ha visto cortarse).
            var logs = (await _ssh.ExecuteAsync(
                $"kubectl logs {podName} -n {production.KubeNamespace} --previous --tail=200 2>/dev/null")).Trim();

            if (string.IsNullOrWhiteSpace(logs))
            {
                logs = (await _ssh.ExecuteAsync(
                    $"kubectl logs {podName} -n {production.KubeNamespace} --tail=200 2>/dev/null")).Trim();
            }

            // El email se queda con las últimas 40 líneas para no ser un ladrillo;
            // el análisis del crash usa las 200 completas (ver logs más arriba).
            var logsTailForEmail = string.Join('\n', logs.Split('\n').TakeLast(40));

            var diagnostics =
                $"Pod: {podName}\n\n" +
                $"Estado del pod:\n{(string.IsNullOrWhiteSpace(stateInfo) ? "(sin datos)" : stateInfo)}\n\n" +
                $"Últimas líneas de log:\n{(string.IsNullOrWhiteSpace(logsTailForEmail) ? "(sin logs disponibles)" : logsTailForEmail)}";

            return (diagnostics, logs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ProductionDownAlert: error obteniendo diagnóstico de la caída");
            return ($"No se pudo obtener el diagnóstico de la caída (error consultando kubectl): {ex.Message}", null);
        }
    }
}
