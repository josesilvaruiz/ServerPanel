using System.Text;
using System.Text.Json;
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
    public async Task MutePlayerAsync(string playerName, int minutes)
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_mute \"{playerName}\" {minutes}' Enter\"";

        _logger.LogInformation(
            "Mute jugador: {Player}, Minutos: {Minutes}",
            playerName,
            minutes);

        await _ssh.ExecuteAsync(command);
    }


    // ── Gag ───────────────────────────────────────────────────────────────────
    public async Task GagPlayerAsync(string playerName, int minutes)
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_gag \"{playerName}\" {minutes}' Enter\"";

        _logger.LogInformation(
            "Gag jugador: {Player}, Minutos: {Minutes}",
            playerName,
            minutes);

        await _ssh.ExecuteAsync(command);
    }

    // ── Ban ───────────────────────────────────────────────────────────────────
    public async Task BanPlayerAsync(string playerName, int minutes, string reason = "Banned by admin")
    {
        var command =
            $"su - steam -c \"tmux send-keys -t cs2 'css_ban \\\"{playerName}\\\" {minutes} \\\"{reason}\\\"' Enter\"";

        await _ssh.ExecuteAsync(command);
    }

    // ── Unban ───────────────────────────────────────────────────────────────
    public async Task UnbanPlayerAsync(string player)
    {
        var safePlayer = player.Replace("'", "''");

        string whereClause;

        // SteamID64 = 17 dígitos
        if (System.Text.RegularExpressions.Regex.IsMatch(player, @"^\d{17}$"))
        {
            whereClause = $"player_steamid = '{player}'";
        }
        else
        {
            whereClause = $"player_name = '{safePlayer}'";
        }

        var deleteCommand =
            "sqlite3 /home/steam/cs2/game/csgo/addons/counterstrikesharp/plugins/CS2-SimpleAdmin/cs2-simpleadmin.sqlite " +
            $"\"DELETE FROM sa_bans WHERE {whereClause};\"";

        await _ssh.ExecuteAsync(deleteCommand);

        await Task.Delay(1000);

        var reloadCommand =
            "su - steam -c \"tmux send-keys -t cs2 'css_plugins reload 1' Enter\"";

        await _ssh.ExecuteAsync(reloadCommand);
    }

    // ── Permisos ─────────────────────────────────────────────────────────────

    private const string AdminsJsonPath =
        "/home/steam/cs2/game/csgo/addons/counterstrikesharp/configs/admins.json";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };

    private async Task<Dictionary<string, PermissionEntry>> ReadAdminsAsync()
    {
        var raw = await _ssh.ExecuteAsync($"cat {AdminsJsonPath} 2>/dev/null || echo {{}}");
        var trimmed = raw.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed == "{}")
            return new Dictionary<string, PermissionEntry>();

        return JsonSerializer.Deserialize<Dictionary<string, PermissionEntry>>(trimmed)
               ?? new Dictionary<string, PermissionEntry>();
    }

    private async Task WriteAdminsAsync(Dictionary<string, PermissionEntry> admins)
    {
        var json = JsonSerializer.Serialize(admins, JsonOpts);
        var b64  = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));

        await _ssh.ExecuteAsync(
            $"printf '%s' '{b64}' | base64 -d > {AdminsJsonPath}");

        await _ssh.ExecuteAsync(
            "su - steam -c \"tmux send-keys -t cs2 'css_reladmins' Enter\"");

        _logger.LogInformation("admins.json actualizado y recargado");
    }

    public async Task SetPermissionsAsync(string playerName, string steamId, string permission)
    {
        var admins = await ReadAdminsAsync();

        // Busca entrada existente por identity (SteamID)
        var existing = admins.FirstOrDefault(kv =>
            kv.Value.Identity.Equals(steamId, StringComparison.OrdinalIgnoreCase));

        PermissionEntry entry;
        string key;

        if (existing.Key is not null)
        {
            key   = existing.Key;
            entry = existing.Value;
        }
        else
        {
            key   = playerName;
            entry = new PermissionEntry { Identity = steamId, Immunity = 0 };
            admins[key] = entry;
        }

        var newFlags = permission
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var flag in newFlags)
            if (!entry.Flags.Contains(flag))
                entry.Flags.Add(flag);

        _logger.LogInformation(
            "SetPermissions: {Player} ({SteamId}) flags={Flags}",
            playerName, steamId, string.Join(", ", entry.Flags));

        await WriteAdminsAsync(admins);
    }

    public async Task<PermissionEntry?> GetPermissionsAsync(string player)
    {
        var admins = await ReadAdminsAsync();
        var existing = admins.FirstOrDefault(kv =>
            kv.Key.Equals(player, StringComparison.OrdinalIgnoreCase) ||
            kv.Value.Identity.Equals(player, StringComparison.OrdinalIgnoreCase));
        return existing.Key is not null ? existing.Value : null;
    }

    public async Task RemovePermissionAsync(string player, string flag)
    {
        var admins = await ReadAdminsAsync();

        var existing = admins.FirstOrDefault(kv =>
            kv.Key.Equals(player, StringComparison.OrdinalIgnoreCase) ||
            kv.Value.Identity.Equals(player, StringComparison.OrdinalIgnoreCase));

        if (existing.Key is null)
        {
            _logger.LogWarning("RemovePermission: jugador '{Player}' no encontrado en admins.json", player);
            return;
        }

        existing.Value.Flags.Remove(flag);

        // Si no quedan flags, eliminar la entrada por completo
        if (existing.Value.Flags.Count == 0)
            admins.Remove(existing.Key);

        _logger.LogInformation(
            "RemovePermission: {Player} flag={Flag} eliminado", player, flag);

        await WriteAdminsAsync(admins);
    }
}