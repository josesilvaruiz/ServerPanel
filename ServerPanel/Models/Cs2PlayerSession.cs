namespace ServerPanel.Models;

public class Cs2PlayerSession
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string PlayerName { get; set; } = "";

    public int UserId { get; set; }

    public int Ping { get; set; }

    /// <summary>ServerConfig.Name del servidor al que pertenece la fila.</summary>
    public string ServerKey { get; set; } = "Producción";
}
