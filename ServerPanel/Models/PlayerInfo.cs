namespace ServerPanel.Models
{
    public class PlayerInfo
    {
        public int UserId { get; set; }

        public string Name { get; set; } = "";

        public string SteamId { get; set; } = "";

        public string CommunityUrl { get; set; } = "";

        public string Clan { get; set; } = "";

        public string Groups { get; set; } = "";

        public int Ping { get; set; }

        public bool IsBot { get; set; }
    }
}
