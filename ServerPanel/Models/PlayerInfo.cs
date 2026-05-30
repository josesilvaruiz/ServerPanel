namespace ServerPanel.Models
{
    public class PlayerInfo
    {
        public string Name { get; set; } = "";

        public string SteamId { get; set; } = "";

        public int Ping { get; set; }

        public bool IsBot { get; set; }
    }
}
