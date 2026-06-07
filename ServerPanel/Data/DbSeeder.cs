using Microsoft.EntityFrameworkCore;
using ServerPanel.Models;

namespace ServerPanel.Data;

public static class DbSeeder
{
    public static async Task SeedSharedCommandsAsync(ApplicationDbContext db)
    {
        if (await db.SavedCmdGroups.AnyAsync(g => g.Owner == "all")) return;

        var groups = new List<SavedCmdGroup>
        {
            new()
            {
                Title = "🚀 Deploy — Panel",
                Owner = "all",
                SortOrder = 0,
                Commands =
                [
                    new() { Text = "cd /opt/ServerPanel/ServerPanel", SortOrder = 0 },
                    new() { Text = "git pull origin master", SortOrder = 1 },
                    new() { Text = "dotnet build", SortOrder = 2 },
                    new() { Text = "tmux kill-session -t serverpanel 2>/dev/null || true", SortOrder = 3 },
                    new() { Text = "tmux new-session -d -s serverpanel \"cd /opt/ServerPanel/ServerPanel && dotnet run\"", SortOrder = 4 },
                    new() { Text = "tmux ls", SortOrder = 5 },
                ]
            },
            new()
            {
                Title = "🌐 Deploy — Landing",
                Owner = "all",
                SortOrder = 1,
                Commands =
                [
                    new() { Text = "cd /var/www/landing", SortOrder = 0 },
                    new() { Text = "git pull origin master", SortOrder = 1 },
                    new() { Text = "npm install", SortOrder = 2 },
                    new() { Text = "npm run build", SortOrder = 3 },
                    new() { Text = "nginx -t", SortOrder = 4 },
                    new() { Text = "systemctl reload nginx", SortOrder = 5 },
                ]
            },
            new()
            {
                Title = "🎮 CS2 — Control",
                Owner = "all",
                SortOrder = 2,
                Commands =
                [
                    new() { Text = "su - steam -c 'tmux ls'", SortOrder = 0 },
                    new() { Text = "su - steam -c 'tmux attach -t cs2'", SortOrder = 1 },
                    new() { Text = "tmux send-keys -t cs2 'quit' Enter", SortOrder = 2 },
                    new() { Text = "pgrep -a cs2", SortOrder = 3 },
                    new() { Text = "kill -9 $(pgrep -f cs2)", SortOrder = 4 },
                ]
            },
            new()
            {
                Title = "🎮 CS2 — Logs",
                Owner = "all",
                SortOrder = 3,
                Commands =
                [
                    new() { Text = "su - steam -c 'tmux capture-pane -t cs2 -p -S -200'", SortOrder = 0 },
                    new() { Text = "tail -100 /home/steam/cs2/logs/server.txt", SortOrder = 1 },
                    new() { Text = "find /home/steam -name '*.log' -mmin -60", SortOrder = 2 },
                    new() { Text = "ls -lah /home/steam/cs2/game/csgo/addons", SortOrder = 3 },
                ]
            },
            new()
            {
                Title = "📊 Sistema",
                Owner = "all",
                SortOrder = 4,
                Commands =
                [
                    new() { Text = "uptime", SortOrder = 0 },
                    new() { Text = "free -h", SortOrder = 1 },
                    new() { Text = "df -h", SortOrder = 2 },
                    new() { Text = "top -bn1 | head -20", SortOrder = 3 },
                    new() { Text = "ss -tlnp", SortOrder = 4 },
                    new() { Text = "systemctl status nginx", SortOrder = 5 },
                ]
            },
            new()
            {
                Title = "🔧 Nginx",
                Owner = "all",
                SortOrder = 5,
                Commands =
                [
                    new() { Text = "nginx -t", SortOrder = 0 },
                    new() { Text = "systemctl reload nginx", SortOrder = 1 },
                    new() { Text = "systemctl restart nginx", SortOrder = 2 },
                    new() { Text = "systemctl status nginx", SortOrder = 3 },
                    new() { Text = "cat /var/log/nginx/error.log | tail -50", SortOrder = 4 },
                ]
            },
        };

        db.SavedCmdGroups.AddRange(groups);
        await db.SaveChangesAsync();
    }
}
