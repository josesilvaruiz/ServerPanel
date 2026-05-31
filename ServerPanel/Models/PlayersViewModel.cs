using System.Collections.Generic;

namespace ServerPanel.Models
{
    public class PlayersViewModel
    {
        public bool Loading { get; set; } = true;
        public string StatusMessage { get; set; } = string.Empty;
        public List<PlayerInfo> Players { get; set; } = new();
        public int CurrentPage { get; set; } = 1;

        private int _pageSize = 10;
        public int PageSize
        {
            get => _pageSize;
            set
            {
                _pageSize = value;
                CurrentPage = 1;
            }
        }
    }
}
