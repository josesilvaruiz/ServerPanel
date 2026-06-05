using Microsoft.EntityFrameworkCore;
using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class MetricsCollectorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IServerMetricsService _metricsService;
    private readonly ILogger<MetricsCollectorBackgroundService> _logger;

    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(1);

    public MetricsCollectorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IServerMetricsService metricsService,
        ILogger<MetricsCollectorBackgroundService> logger)
    {
        _scopeFactory  = scopeFactory;
        _metricsService = metricsService;
        _logger         = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MetricsCollector started. Interval: {Interval}s", Interval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            await CollectAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        }

        _logger.LogInformation("MetricsCollector stopped.");
    }

    private async Task CollectAsync(CancellationToken ct)
    {
        try
        {
            var metrics = await _metricsService.GetMetricsAsync();

            var snapshot = new VpsMetricSnapshot
            {
                TimestampUtc     = DateTime.UtcNow,
                CpuPercent       = metrics.CpuUsagePercent,
                MemoryUsedMb     = metrics.MemoryUsedMb,
                MemoryTotalMb    = metrics.MemoryTotalMb,
                DiskUsagePercent = metrics.DiskUsagePercent,
                LoadAverage      = metrics.LoadAverage,
            };

            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.VpsMetricSnapshots.Add(snapshot);
            _logger.LogInformation("Snapshot saving...");
            await db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Snapshot saved | Id:{Id} | TimestampUtc:{Ts} | CpuPercent:{Cpu}% | MemoryUsedMb:{Ram} | DiskUsagePercent:{Disk}% | LoadAverage:{Load}",
                snapshot.Id, snapshot.TimestampUtc, snapshot.CpuPercent,
                snapshot.MemoryUsedMb, snapshot.DiskUsagePercent, snapshot.LoadAverage);
        }
        catch (OperationCanceledException)
        {
            // shutdown normal, no registrar como error
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting VPS metrics snapshot — will retry in {Interval}s", Interval.TotalSeconds);
        }
    }
}
