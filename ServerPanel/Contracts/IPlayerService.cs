using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IPlayerService
{
    Task<List<PlayerInfo>> GetPlayersAsync();
    Task KickPlayerAsync(string playerName, string reason = "Kicked by admin");
    Task MutePlayerAsync(string playerName, string reason);
    Task GagPlayerAsync(string playerName, string reason);
    Task BanPlayerAsync(string playerName, int minutes, string reason = "Banned by admin");
    Task UnbanPlayerAsync(string playerName);
}