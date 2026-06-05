using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IServerMetricsService
{
    Task<ServerMetrics> GetMetricsAsync();
}
