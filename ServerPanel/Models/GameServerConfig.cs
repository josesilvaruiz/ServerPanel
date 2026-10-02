namespace ServerPanel.Models;

/// <summary>Un servidor de otro juego (Valheim, …) desplegado en el mismo clúster que CS2.</summary>
public class GameServerConfig
{
    public string Name { get; set; } = "";

    /// <summary>Texto corto bajo el nombre (p. ej. "Mundo: Midgard").</summary>
    public string Description { get; set; } = "";

    /// <summary>Dirección para conectarse desde el juego, solo informativa.</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; }

    public string KubeNamespace { get; set; } = "";
    public string KubeDeployment { get; set; } = "";

    /// <summary>
    /// Carpeta del servidor (en el host) con el mundo y las partidas. Si junto con <see cref="BackupDir"/> está
    /// configurada, al parar el juego se guarda ahí una copia comprimida antes de dar la operación por terminada.
    /// Solo se lee: la copia nunca modifica el original.
    /// </summary>
    public string BackupSource { get; set; } = "";

    /// <summary>Carpeta (en el host, fuera de <see cref="BackupSource"/>) donde se dejan las copias.</summary>
    public string BackupDir { get; set; } = "";
}
