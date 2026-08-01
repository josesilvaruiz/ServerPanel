namespace ServerPanel.Models;

public enum Cs2ConnectionEventType
{
    Connect,
    Disconnect,
}

public class Cs2PlayerConnectionEvent
{
    public long Id { get; set; }

    public DateTime TimestampUtc { get; set; }

    public Cs2ConnectionEventType EventType { get; set; }

    public string PlayerName { get; set; } = "";

    /// <summary>SteamID64, resuelto a partir del SteamID3 del log. Null en desconexiones
    /// (esa línea no trae el SteamID) — se casan por nombre al construir sesiones.</summary>
    public string? SteamId64 { get; set; }

    public string? ServerName { get; set; }
}
