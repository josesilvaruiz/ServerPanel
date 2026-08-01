using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Services;

// El historial de jugadores (Cs2MetricsCollectorBackgroundService) se basa en una foto cada
// 1 minuto — si alguien se conecta y se va antes de la siguiente foto, no queda ni rastro de
// que estuvo (pasa más de lo que parece: reconexiones de 10-20s son habituales). Este servicio
// lee directamente del log del servidor las líneas de conexión/desconexión (que el motor
// imprime siempre, sin depender de ningún sondeo) para tener una traza exacta con nombre y
// hora real, sin importar cuánto durara la sesión.
public class Cs2PlayerConnectionTrackerBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(20);

    // Ventana de log revisada cada poll — con solape sobre el intervalo para no perder líneas
    // si un poll se retrasa; el dedupe por timestamp exacto de la propia línea evita duplicados.
    private static readonly TimeSpan LogWindow = TimeSpan.FromSeconds(45);

    // "Nombre<slot><[U:1:accountId]><team>" STEAM USERID validated — misma línea trae nombre
    // y SteamID3 de un tirón, es la fuente más fiable para el connect.
    private static readonly Regex ConnectPattern = new(
        @"""(?<name>.+?)<\d+><\[U:1:(?<accid>\d+)\]><[^>]*>""\s+STEAM USERID validated",
        RegexOptions.Compiled);

    // SV:  Dropped client 'Nombre' from server(N): RAZÓN — no trae SteamID, se casa por nombre.
    private static readonly Regex DisconnectPattern = new(
        @"Dropped client '(?<name>.+?)' from server\(\d+\):",
        RegexOptions.Compiled);

    private readonly IActiveServerService _activeServer;
    private readonly ISshService _ssh;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<Cs2PlayerConnectionTrackerBackgroundService> _logger;

    public Cs2PlayerConnectionTrackerBackgroundService(
        IActiveServerService activeServer,
        ISshService ssh,
        IServiceScopeFactory scopeFactory,
        IConfiguration config,
        ILogger<Cs2PlayerConnectionTrackerBackgroundService> logger)
    {
        _activeServer = activeServer;
        _ssh = ssh;
        _scopeFactory = scopeFactory;
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cs2PlayerConnectionTracker started. Interval: {Interval}s", Interval.TotalSeconds);

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
                $"kubectl logs {podName} -n {production.KubeNamespace} --since={sinceSeconds}s --timestamps 2>/dev/null " +
                "| grep -E 'STEAM USERID validated|Dropped client'");

            if (string.IsNullOrWhiteSpace(logs)) return;

            var events = ParseEvents(logs, production.Name);
            if (events.Count == 0) return;

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var added = 0;
            foreach (var evt in events)
            {
                // Dedupe determinista: la misma línea de log siempre trae el mismo timestamp
                // exacto (microsegundos), así que si ya existe un evento igual (mismo jugador,
                // tipo y momento) es que ya lo vimos en un poll anterior con solape.
                var exists = await db.Cs2PlayerConnectionEvents.AnyAsync(e =>
                    e.PlayerName == evt.PlayerName &&
                    e.EventType == evt.EventType &&
                    e.TimestampUtc == evt.TimestampUtc, ct);

                if (exists) continue;

                db.Cs2PlayerConnectionEvents.Add(evt);
                added++;
            }

            if (added > 0)
                await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cs2PlayerConnectionTracker: error leyendo el log de producción");
        }
    }

    private List<Cs2PlayerConnectionEvent> ParseEvents(string logs, string serverName)
    {
        var result = new List<Cs2PlayerConnectionEvent>();

        foreach (var line in logs.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            // "kubectl logs --timestamps" antepone "2026-08-01T12:46:41.324334218+02:00 " a cada línea.
            var spaceIdx = line.IndexOf(' ');
            if (spaceIdx < 0) continue;
            if (!DateTimeOffset.TryParse(line[..spaceIdx], CultureInfo.InvariantCulture, DateTimeStyles.None, out var ts))
                continue;

            var rest = line[(spaceIdx + 1)..];
            var utc = ts.UtcDateTime;

            var connectMatch = ConnectPattern.Match(rest);
            if (connectMatch.Success)
            {
                var steamId64 = ulong.TryParse(connectMatch.Groups["accid"].Value, out var accId)
                    ? (accId + 76561197960265728UL).ToString()
                    : null;

                result.Add(new Cs2PlayerConnectionEvent
                {
                    TimestampUtc = utc,
                    EventType = Cs2ConnectionEventType.Connect,
                    PlayerName = connectMatch.Groups["name"].Value,
                    SteamId64 = steamId64,
                    ServerName = serverName,
                });
                continue;
            }

            var disconnectMatch = DisconnectPattern.Match(rest);
            if (disconnectMatch.Success)
            {
                result.Add(new Cs2PlayerConnectionEvent
                {
                    TimestampUtc = utc,
                    EventType = Cs2ConnectionEventType.Disconnect,
                    PlayerName = disconnectMatch.Groups["name"].Value,
                    ServerName = serverName,
                });
            }
        }

        return result;
    }

    // Mismo mecanismo que ProductionDownAlertBackgroundService/TickStallAlertBackgroundService:
    // el label del pod no es necesariamente "app=<KubeDeployment>", hay que leerlo del deployment.
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
}
