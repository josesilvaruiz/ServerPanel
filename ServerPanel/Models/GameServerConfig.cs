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
}
