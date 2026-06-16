using Microsoft.EntityFrameworkCore;
using ServerPanel.Models;

namespace ServerPanel.Data;

public static class DbSeeder
{
    // Bump this version to force a full reseed of shared groups
    const string Version = "v3";
    const string VersionMarker = $"__seeder_version:{Version}";

    public static async Task SeedSharedCommandsAsync(ApplicationDbContext db)
    {
        Console.WriteLine("[Seeder] Verificando grupos compartidos...");

        var hasVersion = await db.SavedCmdGroups.AnyAsync(g => g.Owner == VersionMarker);
        if (hasVersion) { Console.WriteLine("[Seeder] Ya en versión actual."); return; }

        // Remove all previous shared groups (including old version markers)
        var old = await db.SavedCmdGroups.Where(g => g.Owner == "all" || g.Owner.StartsWith("__seeder_version")).ToListAsync();
        if (old.Count > 0) { db.SavedCmdGroups.RemoveRange(old); await db.SaveChangesAsync(); }

        Console.WriteLine("[Seeder] Insertando grupos compartidos...");

        var groups = new List<SavedCmdGroup>
        {
            // ── Deploy ─────────────────────────────────────────
            new()
            {
                Title = "🚀 Deploy — Panel",
                Owner = "all", SortOrder = 0,
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
                Title = "🚀 Deploy — Landing",
                Owner = "all", SortOrder = 1,
                Commands =
                [
                    new() { Text = "cd /var/www/landing", SortOrder = 0 },
                    new() { Text = "git pull origin master", SortOrder = 1 },
                    new() { Text = "npm install", SortOrder = 2 },
                    new() { Text = "npm run build", SortOrder = 3 },
                    new() { Text = "nginx -t && systemctl reload nginx", SortOrder = 4 },
                ]
            },

            // ── CS2 Kubernetes ─────────────────────────────────
            new()
            {
                Title = "🎮 CS2 — Pods",
                Owner = "all", SortOrder = 2,
                Commands =
                [
                    new() { Text = "kubectl get pods -n cs2", SortOrder = 0 },
                    new() { Text = "kubectl get pods -n cs2 -o wide", SortOrder = 1 },
                    new() { Text = "kubectl get pods -A", SortOrder = 2 },
                    new() { Text = "kubectl describe pod -n cs2", SortOrder = 3 },
                    new() { Text = "kubectl get events -n cs2 --sort-by=.lastTimestamp", SortOrder = 4 },
                ]
            },
            new()
            {
                Title = "🎮 CS2 — Logs",
                Owner = "all", SortOrder = 3,
                Commands =
                [
                    new() { Text = "kubectl logs -f deployment/cs2-server -n cs2", SortOrder = 0 },
                    new() { Text = "kubectl logs -n cs2 deployment/cs2-server --tail=100", SortOrder = 1 },
                    new() { Text = "kubectl logs -f job/update-manual -n cs2", SortOrder = 2 },
                ]
            },
            new()
            {
                Title = "🎮 CS2 — Control",
                Owner = "all", SortOrder = 4,
                Commands =
                [
                    new() { Text = "kubectl rollout restart deployment/cs2-server -n cs2", SortOrder = 0 },
                    new() { Text = "kubectl rollout status deployment/cs2-server -n cs2", SortOrder = 1 },
                    new() { Text = "kubectl scale deployment cs2-server -n cs2 --replicas=0", SortOrder = 2 },
                    new() { Text = "kubectl scale deployment cs2-server -n cs2 --replicas=1", SortOrder = 3 },
                    new() { Text = "kubectl exec -it -n cs2 deployment/cs2-server -c cs2 -- bash", SortOrder = 4 },
                    new() { Text = "kubectl top pods -n cs2", SortOrder = 5 },
                ]
            },
            new()
            {
                Title = "🎮 CS2 — Update",
                Owner = "all", SortOrder = 5,
                Commands =
                [
                    new() { Text = "kubectl get cronjob -n cs2", SortOrder = 0 },
                    new() { Text = "kubectl create job update-manual --from=cronjob/cs2-auto-update -n cs2", SortOrder = 1 },
                    new() { Text = "kubectl logs -f job/update-manual -n cs2", SortOrder = 2 },
                ]
            },

            // ── Sistema ────────────────────────────────────────
            new()
            {
                Title = "📊 Sistema — Recursos",
                Owner = "all", SortOrder = 6,
                Commands =
                [
                    new() { Text = "uptime", SortOrder = 0 },
                    new() { Text = "free -h", SortOrder = 1 },
                    new() { Text = "df -h", SortOrder = 2 },
                    new() { Text = "top -bn1 | head -20", SortOrder = 3 },
                    new() { Text = "cat /proc/loadavg", SortOrder = 4 },
                    new() { Text = "ps aux | grep cs2", SortOrder = 5 },
                ]
            },
            new()
            {
                Title = "📊 Sistema — Red",
                Owner = "all", SortOrder = 7,
                Commands =
                [
                    new() { Text = "ss -tlnp", SortOrder = 0 },
                    new() { Text = "ip a", SortOrder = 1 },
                    new() { Text = "curl -s ifconfig.me", SortOrder = 2 },
                    new() { Text = "netstat -s | grep -i error", SortOrder = 3 },
                ]
            },
            new()
            {
                Title = "📊 Sistema — Ficheros",
                Owner = "all", SortOrder = 8,
                Commands =
                [
                    new() { Text = "ls -lah /home/steam", SortOrder = 0 },
                    new() { Text = "find /home/steam -name '*.log' -mmin -60", SortOrder = 1 },
                    new() { Text = "tail -100 /home/steam/cs2/logs/server.txt", SortOrder = 2 },
                ]
            },
            new()
            {
                Title = "📊 Sistema — Usuarios",
                Owner = "all", SortOrder = 9,
                Commands =
                [
                    new() { Text = "who", SortOrder = 0 },
                    new() { Text = "last | head -20", SortOrder = 1 },
                    new() { Text = "id steam", SortOrder = 2 },
                ]
            },

            // ── Herramientas ───────────────────────────────────
            new()
            {
                Title = "🔧 Herramientas — Nginx",
                Owner = "all", SortOrder = 10,
                Commands =
                [
                    new() { Text = "nginx -t", SortOrder = 0 },
                    new() { Text = "systemctl reload nginx", SortOrder = 1 },
                    new() { Text = "systemctl restart nginx", SortOrder = 2 },
                    new() { Text = "systemctl status nginx", SortOrder = 3 },
                    new() { Text = "cat /var/log/nginx/error.log | tail -50", SortOrder = 4 },
                ]
            },
            new()
            {
                Title = "🔧 Herramientas — tmux",
                Owner = "all", SortOrder = 11,
                Commands =
                [
                    new() { Text = "tmux ls", SortOrder = 0 },
                    new() { Text = "tmux new -s nombre", SortOrder = 1 },
                    new() { Text = "tmux attach -t cs2", SortOrder = 2 },
                    new() { Text = "tmux kill-session -t cs2", SortOrder = 3 },
                    new() { Text = "tmux send-keys -t cs2 'cmd' Enter", SortOrder = 4 },
                    new() { Text = "tmux capture-pane -t cs2 -p", SortOrder = 5 },
                    new() { Text = "tmux rename-session -t old new", SortOrder = 6 },
                ]
            },
        };

        db.SavedCmdGroups.AddRange(groups);
        // Version marker so we don't reseed next boot
        db.SavedCmdGroups.Add(new SavedCmdGroup { Title = "version", Owner = VersionMarker, SortOrder = 0 });
        await db.SaveChangesAsync();
        Console.WriteLine("[Seeder] Grupos insertados correctamente.");
    }
}
