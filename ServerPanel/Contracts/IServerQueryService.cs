using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IServerQueryService
{
    Task<ServerInfo> GetServerInfoAsync();

    Task<ServerInfo> GetServerInfoAsync(ServerConfig server);

    Task<List<PlayerInfo>> GetPlayersAsync();
}
