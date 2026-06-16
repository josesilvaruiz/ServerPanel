using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class ActiveServerService : IActiveServerService
{
    private readonly List<ServerConfig> _servers;
    private ServerConfig _active;

    public event Action? OnChanged;

    public ActiveServerService(IConfiguration config)
    {
        _servers = config.GetSection("Servers").Get<List<ServerConfig>>() ?? [];

        if (_servers.Count == 0)
        {
            // Fallback: build one entry from the legacy sections
            _servers.Add(new ServerConfig
            {
                Name         = "Producción",
                Host         = config["ServerQuery:Host"] ?? config["Rcon:Host"] ?? "127.0.0.1",
                Port         = int.TryParse(config["ServerQuery:Port"], out var p) ? p : 27015,
                RconPassword = config["Rcon:Password"] ?? ""
            });
        }

        _active = _servers[0];
    }

    public ServerConfig              Active  => _active;
    public IReadOnlyList<ServerConfig> Servers => _servers;

    public void SetActive(string name)
    {
        var found = _servers.FirstOrDefault(s => s.Name == name);
        if (found is null || found == _active) return;
        _active = found;
        OnChanged?.Invoke();
    }
}
