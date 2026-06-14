using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IServerQueryService
{
    Task<ServerInfo> GetServerInfoAsync();

    Task<List<PlayerInfo>> GetPlayersAsync();
}
