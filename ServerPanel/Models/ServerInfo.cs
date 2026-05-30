namespace ServerPanel.Models;

public class ServerInfo
{
    public bool IsOnline { get; set; }

    public string ServerName { get; set; } = "";

    public string Map { get; set; } = "";

    public string Folder { get; set; } = "";

    public string Game { get; set; } = "";

    public ushort AppId { get; set; }

    public int Players { get; set; }

    public int MaxPlayers { get; set; }

    public int Bots { get; set; }

    public string ServerType { get; set; } = "";

    public string Environment { get; set; } = "";

    public byte Visibility { get; set; }

    public byte Vac { get; set; }

    public string Version { get; set; } = "";

    public string Ip { get; set; } = "";

    public int Port { get; set; }

    public string RawResponse { get; set; } = "";
}