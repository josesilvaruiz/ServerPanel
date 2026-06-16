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
    public string KubeConfigBasePath { get; set; } = "/root/cs2-config";
    public string KubeContainerCssPath { get; set; } = "/home/steam/cs2/game/csgo/addons/counterstrikesharp";
}
