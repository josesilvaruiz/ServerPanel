using CoreRCON;
using System.Net;
using ServerPanel.Contracts;

namespace ServerPanel.Services;

public class RconService : IRconService
{
    private readonly string _host;
    private readonly ushort _port;
    private readonly string _password;
    private readonly ILogger<RconService> _logger;

    public RconService(IConfiguration configuration, ILogger<RconService> logger)
    {
        _logger   = logger;
        var s     = configuration.GetSection("Rcon");
        _host     = s["Host"]     ?? "127.0.0.1";
        _port     = ushort.TryParse(s["Port"], out var p) ? p : (ushort)27015;
        _password = s["Password"] ?? "";
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_password);

    public async Task<string> ExecuteAsync(string command, CancellationToken ct = default)
    {
        if (!IsConfigured)
            return "[RCON no configurado]";

        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(_host), _port);
            using var rcon = new RCON(endpoint, _password);
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
