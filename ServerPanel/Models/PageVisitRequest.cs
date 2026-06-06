namespace ServerPanel.Models;

public class PageVisitRequest
{
    public string Path { get; set; } = "";
    public string? Language { get; set; }
    public int? ScreenWidth { get; set; }
    public int? ScreenHeight { get; set; }
    public int? TimeOnPageSeconds { get; set; }
}
