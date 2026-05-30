using System.Text.RegularExpressions;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class PlayerService : IPlayerService
{
    private readonly ISshService _ssh;
    private readonly ILogger<PlayerService> _logger;

    public PlayerService(
        ISshService ssh,
        ILogger<PlayerService> logger)
    {
        _ssh = ssh;
        _logger = logger;
    }

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        var players = new List<PlayerInfo>();

        try
        {
            var output = await _ssh.ExecuteAsync("su - steam -c \"tmux send-keys -t cs2 'status' Enter; sleep 1; tmux capture-pane -t cs2 -p\"");

            _logger.LogWarning(
                "STATUS RAW:\n{Output}",
                output);

            var lines = output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                var match = Regex.Match(
                    trimmed,
                    @"'([^']+)'");

                if (!match.Success)
                    continue;

                var name =
                    match.Groups[1].Value;

                var isBot =
                    trimmed.Contains(
                        "BOT",
                        StringComparison.OrdinalIgnoreCase);

                players.Add(new PlayerInfo
                {
                    Name = name,
                    SteamId = isBot
                        ? "BOT"
                        : "UNKNOWN",
                    Ping = 0,
                    IsBot = isBot
                });

                _logger.LogInformation(
                    "Player encontrado => {Name}",
                    name);
            }

            _logger.LogInformation(
                "Players encontrados: {Count}",
                players.Count);

            return players;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error obteniendo jugadores");

            return players;
        }
    }

    public async Task KickPlayerAsync(string playerName)
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_kick \\\"{playerName}\\\"' Enter\"";

        _logger.LogInformation(
            "Kick jugador: {Player}",
            playerName);

        await _ssh.ExecuteAsync(command);
    }

}