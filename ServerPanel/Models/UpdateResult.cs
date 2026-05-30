namespace ServerPanel.Models;

public class UpdateResult
{
    public bool Updated { get; set; }

    public bool AlreadyUpdated { get; set; }

    public string LocalBuild { get; set; } = string.Empty;

    public string RemoteBuild { get; set; } = string.Empty;

    public string Output { get; set; } = string.Empty;
}
