using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class PlayerService : IPlayerService
{
    private readonly ISshService _ssh;
    private readonly IRconService _rcon;
    private readonly IServerQueryService _serverQuery;
    private readonly ILogger<PlayerService> _logger;
    private readonly string _ns;
    private readonly string _dep;
    private readonly string _containerCssPath;

    private string AdminsJsonPath =>
        $"{_containerCssPath}/configs/admins.json";

    private string SqlitePath =>
        $"{_containerCssPath}/plugins/CS2-SimpleAdmin/cs2-simpleadmin.sqlite";

    public PlayerService(
        ISshService ssh,
        IRconService rcon,
        IServerQueryService serverQuery,
        ILogger<PlayerService> logger,
        IConfiguration configuration)
    {
        _ssh         = ssh;
        _rcon        = rcon;
        _serverQuery = serverQuery;
        _logger      = logger;
        var k8s = configuration.GetSection("Kubernetes");
        _ns               = k8s["Namespace"]        ?? "cs2";
        _dep              = k8s["Deployment"]       ?? "cs2-server";
        _containerCssPath = k8s["ContainerCssPath"] ?? "/home/steam/cs2/game/csgo/addons/counterstrikesharp";
    }

    private string KubeExec(string shellCmd) =>
        $"kubectl exec -n {_ns} deployment/{_dep} -- bash -c {ShellEscape(shellCmd)}";

    private static string ShellEscape(string s) =>
        "'" + s.Replace("'", "'\\''") + "'";

    public async Task<List<PlayerInfo>> GetPlayersBasicAsync()
    {
        try
        {
            var output = await _rcon.ExecuteAsync("status");
            _logger.LogInformation("STATUS RAW:\n{Output}", output);
            if (output.StartsWith("[RCON")) return [];
            return ParseStatusOutput(output);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error obteniendo jugadores (basic)");
            return [];
        }
    }

    public async Task<List<PlayerInfo>> GetPlayersAsync()
    {
        // Try status first
        var statusOutput = await _rcon.ExecuteAsync("status");
        _logger.LogInformation("STATUS RAW:\n{Output}", statusOutput);

        if (statusOutput.StartsWith("[RCON"))
            throw new InvalidOperationException($"RCON no disponible: {statusOutput}");

        var players = ParseStatusOutput(statusOutput);
        _logger.LogInformation("Players parseados de status: {Count}", players.Count);

        // Validation: if 0 and A2S_INFO says there are players → surface diagnostic
        if (players.Count == 0)
        {
            var serverInfo = await _serverQuery.GetServerInfoAsync();
            if (serverInfo.IsOnline && serverInfo.Players > 0)
                throw new PlayerStatusParseException(
                    $"El servidor tiene {serverInfo.Players} jugador(es) según A2S pero RCON 'status' no los devuelve en un formato reconocible.",
                    statusOutput);
        }

        // Enrichment: css_who → SteamID + clan
        var humanPlayers = players.Where(x => !x.IsBot && x.UserId > 0).ToList();
        foreach (var player in humanPlayers)
        {
            try
            {
                var whoOutput = await _rcon.ExecuteAsync($"css_who #{player.UserId}");
                var steam64Match   = Regex.Match(whoOutput, @"SteamID64:\s*""([^""]+)""");
                var clanMatch      = Regex.Match(whoOutput, @"Clan:\s*""([^""]*)""");
                var groupsMatch    = Regex.Match(whoOutput, @"Groups\/Flags:\s*""([^""]*)""");
                var communityMatch = Regex.Match(whoOutput, @"Community link:\s*""([^""]*)""");

                if (steam64Match.Success)   player.SteamId      = steam64Match.Groups[1].Value;
                if (clanMatch.Success)      player.Clan         = clanMatch.Groups[1].Value;
                if (groupsMatch.Success)    player.Groups       = groupsMatch.Groups[1].Value;
                if (communityMatch.Success) player.CommunityUrl = communityMatch.Groups[1].Value;

                _logger.LogInformation("WHO => UserId:{UserId} Name:{Name} Steam64:{Steam64}",
                    player.UserId, player.Name, player.SteamId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "css_who falló para {Player}", player.Name);
            }
        }

        return players;
    }

    public async Task KickPlayerAsync(string playerName, string reason = "Kicked by admin")
    {
        _logger.LogInformation("Kick jugador: {Player}, Motivo: {Reason}", playerName, reason);
        await _rcon.ExecuteAsync($"css_kick \"{EscapeArg(playerName)}\" \"{EscapeArg(reason)}\"");
    }

    public async Task MutePlayerAsync(string playerName, int minutes)
    {
        _logger.LogInformation("Mute jugador: {Player}, Minutos: {Minutes}", playerName, minutes);
        await _rcon.ExecuteAsync($"css_mute \"{EscapeArg(playerName)}\" {minutes}");
    }

    public async Task GagPlayerAsync(string playerName, int minutes)
    {
        _logger.LogInformation("Gag jugador: {Player}, Minutos: {Minutes}", playerName, minutes);
        await _rcon.ExecuteAsync($"css_gag \"{EscapeArg(playerName)}\" {minutes}");
    }

    public async Task BanPlayerAsync(string playerName, int minutes, string reason = "Banned by admin")
    {
        await _rcon.ExecuteAsync($"css_ban \"{EscapeArg(playerName)}\" {minutes} \"{EscapeArg(reason)}\"");
    }

    public async Task UnbanPlayerAsync(string player)
    {
        var safePlayer = player.Replace("'", "''");
        var whereClause = System.Text.RegularExpressions.Regex.IsMatch(player, @"^\d{17}$")
            ? $"player_steamid = '{player}'"
            : $"player_name = '{safePlayer}'";

        var sql = $"DELETE FROM sa_bans WHERE {whereClause};";
        var sqlB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(sql));
        await _ssh.ExecuteAsync(KubeExec($"echo {sqlB64} | base64 -d | sqlite3 {SqlitePath}"));

        await Task.Delay(1000);
        await _rcon.ExecuteAsync("css_plugins reload 1");
    }

    // ── Permisos ─────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private async Task<Dictionary<string, PermissionEntry>> ReadAdminsAsync()
    {
        var raw = await _ssh.ExecuteAsync(KubeExec($"cat {AdminsJsonPath} 2>/dev/null || echo {{}}"));
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
        await _ssh.ExecuteAsync(KubeExec($"echo {b64} | base64 -d > {AdminsJsonPath}"));
        await _rcon.ExecuteAsync("css_admins_reload");
        _logger.LogInformation("admins.json actualizado y recargado");
    }

    public async Task SetPermissionsAsync(string playerName, string steamId, string permission)
    {
        var admins = await ReadAdminsAsync();
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
            entry = new PermissionEntry { Identity = steamId, Immunity = 0, Groups = [] };
            admins[key] = entry;
        }

        entry.Flags  ??= [];
        entry.Groups ??= [];

        foreach (var flag in permission.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (!entry.Flags.Contains(flag))
                entry.Flags.Add(flag);

        _logger.LogInformation("SetPermissions: {Player} ({SteamId}) flags={Flags}",
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

    public Task<Dictionary<string, PermissionEntry>> GetAllAdminsAsync() => ReadAdminsAsync();

    public async Task RemovePermissionAsync(string player, string flag)
    {
        var admins = await ReadAdminsAsync();
        var existing = admins.FirstOrDefault(kv =>
            kv.Key.Equals(player, StringComparison.OrdinalIgnoreCase) ||
            kv.Value.Identity.Equals(player, StringComparison.OrdinalIgnoreCase));

        if (existing.Key is null)
        {
            _logger.LogWarning("RemovePermission: jugador '{Player}' no encontrado", player);
            return;
        }

        existing.Value.Flags.Remove(flag);
        if (existing.Value.Flags.Count == 0)
            admins.Remove(existing.Key);

        _logger.LogInformation("RemovePermission: {Player} flag={Flag}", player, flag);
        await WriteAdminsAsync(admins);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<PlayerInfo> ParseStatusOutput(string output)
    {
        var players = new List<PlayerInfo>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();

            // Player lines must contain a quoted name
            // CS2 uses single quotes 'name', CS:GO uses double quotes "name"
            var nameMatch = Regex.Match(trimmed, @"['""]([^'""]+)['""]");
            if (!nameMatch.Success) continue;

            var name  = nameMatch.Groups[1].Value;
            var isBot = trimmed.Contains("BOT", StringComparison.OrdinalIgnoreCase);
            int userId = 0, ping = 0;

            if (!isBot)
            {
                // Strip leading '#' (CS:GO format) so both formats parse the same way
                // CS:GO: "#  2 "Name" ..."  →  "2 "Name" ..."
                // CS2:   " 2  00:10  50 ..."  →  "2  00:10  50 ..."
                var parseable = trimmed.TrimStart('#').TrimStart();
                var m = Regex.Match(parseable, @"^(\d+)\s+\S+\s+(\d+)");
                if (m.Success)
                {
                    int.TryParse(m.Groups[1].Value, out userId);
                    int.TryParse(m.Groups[2].Value, out ping);
                }
            }

            players.Add(new PlayerInfo
            {
                UserId = userId, Name = name, Ping = ping, IsBot = isBot,
                SteamId = isBot ? "BOT" : "", Clan = "", Groups = "", CommunityUrl = ""
            });
        }
        return players;
    }

    private static string EscapeArg(string value) =>
        string.IsNullOrEmpty(value) ? "" : value.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

public sealed class PlayerStatusParseException(string message, string rawStatus) : Exception(message)
{
    public string RawStatus { get; } = rawStatus;
}
