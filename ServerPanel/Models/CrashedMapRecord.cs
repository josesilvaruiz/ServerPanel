namespace ServerPanel.Models;

public class CrashedMapRecord
{
    public long Id { get; set; }

    public DateTime CrashedAtUtc { get; set; }

    public string MapName { get; set; } = "";

    public string? WorkshopId { get; set; }

    public string? ServerName { get; set; }
}
