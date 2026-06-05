using ServerPanel.Contracts;

namespace ServerPanel.Services;

public class PostgresTunnelService : IHostedService
{
    private readonly ISshService _ssh;
    private readonly ILogger<PostgresTunnelService> _logger;
    private IAsyncDisposable? _tunnel;

    public PostgresTunnelService(ISshService ssh, ILogger<PostgresTunnelService> logger)
    {
        _ssh    = ssh;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("PostgresTunnel: opening... LocalPort=15432 RemoteHost=localhost RemotePort=5432");
        _tunnel = _ssh.OpenTunnel(localPort: 15432, remoteHost: "localhost", remotePort: 5432);
        _logger.LogInformation("PostgresTunnel: opened — localhost:15432 → localhost:5432");
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_tunnel is not null)
            await _tunnel.DisposeAsync();
    }
}
