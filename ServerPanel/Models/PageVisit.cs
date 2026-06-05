namespace ServerPanel.Models;

public class PageVisit
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string Path { get; set; } = "";

    public string? Referrer { get; set; }

    public string? UserAgent { get; set; }
}
