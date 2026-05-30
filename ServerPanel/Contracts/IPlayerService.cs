using ServerPanel.Models;

namespace ServerPanel.Contracts
{
    public interface IPlayerService
    {
        Task<List<PlayerInfo>> GetPlayersAsync();
        Task KickPlayerAsync(string playerName);
    }
}
