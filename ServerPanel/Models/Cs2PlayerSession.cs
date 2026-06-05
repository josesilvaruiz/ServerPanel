namespace ServerPanel.Models;

public class Cs2PlayerSession
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string PlayerName { get; set; } = "";

    public int UserId { get; set; }

    public int Ping { get; set; }
}
