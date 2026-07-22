using System.Net.Http.Json;
using ServerPanel.Contracts;

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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<ProductionDownAlertBackgroundService> _logger;

    // null = todavía no sabemos el estado (arranque del panel): no alertar en el primer poll.
    private bool? _lastKnownOnline;

    public ProductionDownAlertBackgroundService(
        IServerQueryService serverQuery,
        IActiveServerService activeServer,
        IManualActionTracker manualActionTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration config,
        ILogger<ProductionDownAlertBackgroundService> logger)
    {
        _serverQuery = serverQuery;
        _activeServer = activeServer;
        _manualActionTracker = manualActionTracker;
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
                await SendAlertAsync(production.Name, ct);
            }
        }

        _lastKnownOnline = isOnline;
    }

    private async Task SendAlertAsync(string serverName, CancellationToken ct)
    {
        var baseUrl = _config["Notifications:BaseUrl"];
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            _logger.LogWarning("ProductionDownAlert: Notifications:BaseUrl no configurado, no se envía el email");
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new
            {
                userId = _config["Notifications:AlertUserId"] ?? "admin",
                message = $"El servidor CS2 '{serverName}' ha dejado de responder.",
                channel = 0 // Email
            };

            var response = await client.PostAsJsonAsync($"{baseUrl}/api/Notifications", payload, ct);
            response.EnsureSuccessStatusCode();
            _logger.LogInformation("ProductionDownAlert: email de caída encolado para {Server}", serverName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ProductionDownAlert: fallo llamando a la API de Notifications");
        }
    }
}
