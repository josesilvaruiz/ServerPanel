using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class Cs2MetricsCollectorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServerQueryService _serverQuery;
    private readonly ILogger<Cs2MetricsCollectorBackgroundService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public Cs2MetricsCollectorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IServerQueryService serverQuery,
        ILogger<Cs2MetricsCollectorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _serverQuery  = serverQuery;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Cs2MetricsCollector started. Interval: {Interval}s", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CollectAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }

        _logger.LogInformation("Cs2MetricsCollector stopped.");
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        try
        {
            // A2S UDP: estado, mapa, jugadores, nombre
            var info = await _serverQuery.GetServerInfoAsync();

            var snapshot = new Cs2ServerSnapshot
            {
                TimestampUtc   = DateTime.UtcNow,
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
                    await using var playerScope = _scopeFactory.CreateAsyncScope();
                    var playerService = playerScope.ServiceProvider.GetRequiredService<IPlayerService>();
                    var players = await playerService.GetPlayersBasicAsync();

                    sessions = players
                        .Where(p => !p.IsBot)
                        .Select(p => new Cs2PlayerSession
                        {
                            TimestampUtc = snapshot.TimestampUtc,
                            PlayerName   = p.Name,
                            UserId       = p.UserId,
                            Ping         = p.Ping,
                        })
                        .ToList();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error obteniendo sesiones de jugadores — snapshot CS2 se guarda igualmente");
                }
            }

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Cs2ServerSnapshots.Add(snapshot);
            if (sessions.Count > 0)
                db.Cs2PlayerSessions.AddRange(sessions);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "CS2 Snapshot saved | Players:{Players}/{Max} | Map:{Map} | Online:{Online} | Sessions:{Sessions}",
                snapshot.CurrentPlayers, snapshot.MaxPlayers, snapshot.Map, snapshot.IsOnline, sessions.Count);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting CS2 snapshot — will retry in {Interval}s", Interval.TotalSeconds);
        }
    }
}
