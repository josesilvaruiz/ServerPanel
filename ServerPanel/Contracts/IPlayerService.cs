using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IPlayerService
{
    Task<List<PlayerInfo>> GetPlayersAsync();
    Task KickPlayerAsync(string playerName, string reason = "Kicked by admin");
    Task MutePlayerAsync(string playerName, int minutes);
    Task GagPlayerAsync(string playerName, int minutes);
    Task BanPlayerAsync(string playerName, int minutes, string reason = "Banned by admin");
    Task UnbanPlayerAsync(string player);
    Task SetPermissionsAsync(string playerName, string steamId, string permission);
    Task RemovePermissionAsync(string steamId, string flag);
    Task<PermissionEntry?> GetPermissionsAsync(string steamId);
}