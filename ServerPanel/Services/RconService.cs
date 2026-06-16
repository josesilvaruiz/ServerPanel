using CoreRCON;
using System.Net;
using ServerPanel.Contracts;

namespace ServerPanel.Services;

public class RconService : IRconService
{
    private readonly IActiveServerService _activeServer;
    private readonly ILogger<RconService> _logger;

    public RconService(IActiveServerService activeServer, ILogger<RconService> logger)
    {
        _activeServer = activeServer;
        _logger       = logger;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_activeServer.Active.RconPassword);

    public async Task<string> ExecuteAsync(string command, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "[RCON no configurado]";

        var server = _activeServer.Active;

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(server.Host), server.Port);
            using var rcon = new RCON(endpoint, server.RconPassword);
            await rcon.ConnectAsync();
            var response = await rcon.SendCommandAsync(command);
            _logger.LogInformation("RCON '{Cmd}' => '{Out}'", command, response?.Trim());
            return string.IsNullOrWhiteSpace(response) ? "(sin respuesta)" : response.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RCON error para '{Cmd}'", command);
            return $"[RCON error: {ex.Message}]";
        }
    }
}
