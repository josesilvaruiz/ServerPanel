namespace ServerPanel.Models;

public class KubernetesSettings
{
    public string Namespace { get; set; } = "cs2";
    public string Deployment { get; set; } = "cs2-server";
    public string ConfigBasePath { get; set; } = "/root/cs2-config";
}
