namespace ServerPanel.Models;

public class ServerConfig
{
    public string Name         { get; set; } = "";
    public string Host         { get; set; } = "127.0.0.1";
    public int    Port         { get; set; } = 27015;
    public string RconPassword { get; set; } = "";

    // Kubernetes
    public string KubeNamespace      { get; set; } = "cs2";
    public string KubeDeployment     { get; set; } = "cs2-server";
    /// <summary>Valor del label app= de los pods del deployment (para kubectl top).</summary>
    public string KubePodLabel       { get; set; } = "cs2";
    /// <summary>Colección de Workshop de este servidor (selector de mapas del admin).</summary>
    public string WorkshopCollectionId { get; set; } = "3736332535";
    /// <summary>Nombres de otros servidores que montan el mismo volumen del juego: se reinician tras una actualización.</summary>
    public List<string> PvSharedWith { get; set; } = new();
    public string KubeConfigBasePath { get; set; } = "/root/cs2-config";
    public string KubeContainerCssPath { get; set; } = "/home/steam/cs2/game/csgo/addons/counterstrikesharp";
    public string KubeHostCssPath { get; set; } = "/root/cs2-config/addons/counterstrikesharp";
}
