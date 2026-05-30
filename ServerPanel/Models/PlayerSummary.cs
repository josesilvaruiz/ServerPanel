namespace ServerPanel.Models;

public class PlayerSummary
{
    public int Humans { get; set; }

    public int Bots { get; set; }

    public int Total => Humans + Bots;

    public string RawResponse { get; set; } = "";
}