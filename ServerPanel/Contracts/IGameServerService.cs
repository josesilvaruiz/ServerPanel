using ServerPanel.Models;

namespace ServerPanel.Contracts;

/// <summary>Arrancar, parar y reiniciar los servidores de "Otros juegos" (sección OtherGames de la configuración).</summary>
public interface IGameServerService
{
    IReadOnlyList<GameServerConfig> Games { get; }

    Task<bool> IsRunningAsync(GameServerConfig game);

    Task StartAsync(GameServerConfig game);

    Task StopAsync(GameServerConfig game);

    Task RestartAsync(GameServerConfig game);

    /// <summary>Últimas líneas del log del servidor.</summary>
    Task<string> GetLogsAsync(GameServerConfig game, int tail = 60);
}
