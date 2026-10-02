using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class Cs2MetricsCollectorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Cs2MetricsCollectorBackgroundService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public Cs2MetricsCollectorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<Cs2MetricsCollectorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cs2MetricsCollector started. Interval: {Interval}s", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            // Todos los servidores configurados, cada uno en su propio scope con su servidor fijado:
            // lo que se guarda no depende de lo que cada usuario tenga seleccionado en la UI.
            string[] names;
            await using (var s = _scopeFactory.CreateAsyncScope())
                names = s.ServiceProvider.GetRequiredService<IActiveServerService>().Servers.Select(x => x.Name).ToArray();

            foreach (var name in names)
                await CollectAsync(name, stoppingToken);

            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }

        _logger.LogInformation("Cs2MetricsCollector stopped.");
    }

    private async Task CollectAsync(string serverName, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IActiveServerService>().SetActive(serverName);

            // A2S UDP: estado, mapa, jugadores, nombre
            var info = await sp.GetRequiredService<IServerQueryService>().GetServerInfoAsync();

            var snapshot = new Cs2ServerSnapshot
            {
                TimestampUtc   = DateTime.UtcNow,
                ServerKey      = serverName,
                IsOnline       = info.IsOnline,
                CurrentPlayers = info.Players,
                MaxPlayers     = info.MaxPlayers,
                Map            = info.Map,
                ServerName     = info.ServerName,
            };

            // Una sola llamada SSH: tmux status → nombres, userIds, pings (sin css_who)
            var sessions = new List<Cs2PlayerSession>();
            if (info.IsOnline)
            {
                try
                {
                    var players = await sp.GetRequiredService<IPlayerService>().GetPlayersBasicAsync();

                    sessions = players
                        .Where(p => !p.IsBot)
                        .Select(p => new Cs2PlayerSession
                        {
                            TimestampUtc = snapshot.TimestampUtc,
                            ServerKey    = serverName,
                            PlayerName   = p.Name,
                            UserId       = p.UserId,
                            Ping         = p.Ping,
                        })
                        .ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error obteniendo sesiones de jugadores de {Server} — snapshot CS2 se guarda igualmente", serverName);
                }
            }

            var db = sp.GetRequiredService<ApplicationDbContext>();
            db.Cs2ServerSnapshots.Add(snapshot);
            if (sessions.Count > 0)
                db.Cs2PlayerSessions.AddRange(sessions);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CS2 Snapshot saved | {Server} | Players:{Players}/{Max} | Map:{Map} | Online:{Online} | Sessions:{Sessions}",
                serverName, snapshot.CurrentPlayers, snapshot.MaxPlayers, snapshot.Map, snapshot.IsOnline, sessions.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting CS2 snapshot of {Server} — will retry in {Interval}s", serverName, Interval.TotalSeconds);
        }
    }
}
