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
            var output = await _ssh.ExecuteAsync(
                "su - steam -c \"tmux send-keys -t cs2 'status' Enter; sleep 1; tmux capture-pane -t cs2 -p\"");

            _logger.LogInformation(
                "STATUS RAW:\n{Output}",
                output);

            var lines = output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();

                var nameMatch =
                    Regex.Match(
                        trimmed,
                        @"'([^']+)'");

                if (!nameMatch.Success)
                    continue;

                var name =
                    nameMatch.Groups[1].Value;

                var isBot =
                    trimmed.Contains(
                        "BOT",
                        StringComparison.OrdinalIgnoreCase);

                int userId = 0;
                int ping = 0;

                if (!isBot)
                {
                    var statusMatch =
                        Regex.Match(
                            trimmed,
                            @"^\s*(\d+)\s+\S+\s+(\d+)");

                    if (statusMatch.Success)
                    {
                        int.TryParse(
                            statusMatch.Groups[1].Value,
                            out userId);

                        int.TryParse(
                            statusMatch.Groups[2].Value,
                            out ping);
                    }
                }

                players.Add(new PlayerInfo
                {
                    UserId = userId,
                    Name = name,
                    Ping = ping,
                    IsBot = isBot,
                    SteamId = isBot ? "BOT" : "",
                    Clan = "",
                    Groups = "",
                    CommunityUrl = ""
                });
            }

            _logger.LogInformation(
                "Players encontrados: {Count}",
                players.Count);

            var humanPlayers =
                players.Where(x => !x.IsBot)
                       .ToList();

            if (humanPlayers.Count == 0)
            {
                _logger.LogInformation(
                    "No hay jugadores humanos. Saltando css_who.");

                return players;
            }

            foreach (var player in humanPlayers)
            {
                try
                {
                    var whoOutput =
                        await _ssh.ExecuteAsync(
                            $"su - steam -c \"tmux send-keys -t cs2 'css_who #{player.UserId}' Enter; sleep 1; tmux capture-pane -t cs2 -p\"");

                    var steam64Match =
                        Regex.Match(
                            whoOutput,
                            @"SteamID64:\s*""([^""]+)""");

                    var steam2Match =
                        Regex.Match(
                            whoOutput,
                            @"SteamID2:\s*""([^""]+)""");

                    var clanMatch =
                        Regex.Match(
                            whoOutput,
                            @"Clan:\s*""([^""]*)""");

                    var groupsMatch =
                        Regex.Match(
                            whoOutput,
                            @"Groups\/Flags:\s*""([^""]*)""");

                    var communityMatch =
                        Regex.Match(
                            whoOutput,
                            @"Community link:\s*""([^""]*)""");

                    if (steam64Match.Success)
                    {
                        player.SteamId =
                            steam64Match.Groups[1].Value;
                    }

                    if (clanMatch.Success)
                    {
                        player.Clan =
                            clanMatch.Groups[1].Value;
                    }

                    if (groupsMatch.Success)
                    {
                        player.Groups =
                            groupsMatch.Groups[1].Value;
                    }

                    if (communityMatch.Success)
                    {
                        player.CommunityUrl =
                            communityMatch.Groups[1].Value;
                    }

                    _logger.LogInformation(
                        "WHO => UserId:{UserId} Name:{Name} Steam64:{Steam64}",
                        player.UserId,
                        player.Name,
                        player.SteamId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Error obteniendo WHO de {Player}",
                        player.Name);
                }
            }

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

    // ── Kick ──────────────────────────────────────────────────────────────────
    public async Task KickPlayerAsync(string playerName, string reason = "Kicked by admin")
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_kick \\\"{playerName}\\\" \\\"{reason}\\\"' Enter\"";

        _logger.LogInformation(
            "Kick jugador: {Player}, Motivo: {Reason}",
            playerName,
            reason);

        await _ssh.ExecuteAsync(command);
    }


    // ── Mute ──────────────────────────────────────────────────────────────────
    public async Task MutePlayerAsync(string playerName, string reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? "Muted by admin" : reason;
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_mute \"{playerName}\" \"{reason}\"' Enter\"";

        _logger.LogInformation(
            "Mute jugador: {Player}, Motivo: {Reason}",
            playerName,
            reason);

        await _ssh.ExecuteAsync(command);
    }


    // ── Gag ───────────────────────────────────────────────────────────────────
    public async Task GagPlayerAsync(string playerName, string reason)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? "Gagged by admin" : reason;
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_gag \"{playerName}\" \"{reason}\"' Enter\"";

        _logger.LogInformation(
            "Gag jugador: {Player}, Motivo: {Reason}",
            playerName,
            reason);

        await _ssh.ExecuteAsync(command);
    }

    // ── Ban ───────────────────────────────────────────────────────────────────
    public async Task BanPlayerAsync(string playerName, int minutes, string reason = "Banned by admin")
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_ban \\\"{playerName}\\\" {minutes} \\\"{reason}\\\"' Enter\"";

        await _ssh.ExecuteAsync(command);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    /// <summary>Escapes double-quotes inside shell arguments.</summary>
    private static string EscapeArg(string value) =>
        value.Replace("\"", "\\\"");
}