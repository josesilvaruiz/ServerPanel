namespace ServerPanel.Models;

public class Cs2ServerSnapshot
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public bool IsOnline { get; set; }

    public int CurrentPlayers { get; set; }

    public int MaxPlayers { get; set; }

    public string? Map { get; set; }

    public string? ServerName { get; set; }
}
