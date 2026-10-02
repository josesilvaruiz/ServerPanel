using ServerPanel.Models;

namespace ServerPanel.Contracts;

/// <summary>Arrancar, parar y reiniciar los servidores de "Otros juegos" (sección OtherGames de la configuración).</summary>
public interface IGameServerService
{
    IReadOnlyList<GameServerConfig> Games { get; }

    Task<bool> IsRunningAsync(GameServerConfig game);

    Task StartAsync(GameServerConfig game);

    /// <summary>
    /// Apaga el servidor (SIGTERM: el juego guarda el mundo) y, si el juego tiene copia de seguridad
    /// configurada, espera a que el pod termine y guarda una copia. Devuelve un resumen para mostrar.
    /// </summary>
    Task<string> StopAsync(GameServerConfig game);

    Task RestartAsync(GameServerConfig game);

    /// <summary>Últimas líneas del log del servidor.</summary>
    Task<string> GetLogsAsync(GameServerConfig game, int tail = 60);
}
