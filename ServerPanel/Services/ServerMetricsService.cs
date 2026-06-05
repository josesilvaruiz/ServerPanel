using System.Globalization;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class ServerMetricsService : IServerMetricsService
{
    private readonly ISshService _ssh;

    public ServerMetricsService(ISshService ssh) => _ssh = ssh;

    public async Task<ServerMetrics> GetMetricsAsync()
    {
        var metrics = new ServerMetrics();

        var freeOut   = await _ssh.ExecuteAsync("free -m");
        var dfOut     = await _ssh.ExecuteAsync("df -h /");
        var uptimeOut = await _ssh.ExecuteAsync("uptime");

        ParseFree(freeOut, metrics);
        ParseDf(dfOut, metrics);
        ParseUptime(uptimeOut, metrics);

        // Find the CS2 process with the highest RSS (= the game server, not helpers).
        // [c]s2 avoids matching the grep itself.
        var cs2Out = await _ssh.ExecuteAsync(
            "ps aux | grep -E '[c]s2' | sort -k6 -rn | awk 'NR==1{print $2,$6}'");

        var cs2Line = cs2Out.Trim();
        int? cs2Pid = null;
        if (!string.IsNullOrEmpty(cs2Line))
        {
            var parts = cs2Line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && int.TryParse(parts[0], out var pid)
                && long.TryParse(parts[1], out var rssKb))
            {
                cs2Pid = pid;
                metrics.Cs2Running  = true;
                metrics.Cs2MemoryMb = rssKb / 1024;
            }
        }

        // Two /proc/stat snapshots 1 s apart — measures both system CPU and CS2 CPU in one round-trip pair.
        var procStat1 = await _ssh.ExecuteAsync(
            cs2Pid.HasValue ? $"cat /proc/stat /proc/{cs2Pid}/stat" : "cat /proc/stat");
        await Task.Delay(1000);
        var procStat2 = await _ssh.ExecuteAsync(
            cs2Pid.HasValue ? $"cat /proc/stat /proc/{cs2Pid}/stat" : "cat /proc/stat");

        metrics.CpuUsagePercent = ComputeSystemCpu(procStat1, procStat2);
        if (cs2Pid.HasValue)
            metrics.Cs2CpuPercent = ComputeProcessCpu(procStat1, procStat2);

        return metrics;
    }

    // Parses the aggregate "cpu " line from /proc/stat and returns (idle, total) ticks.
    private static bool TryParseSystemTicks(string snapshot, out long idle, out long total)
    {
        idle  = 0;
        total = 0;
        var cpuLine = snapshot.Split('\n')
                              .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (cpuLine is null) return false;
        var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // cpu user nice system idle iowait irq softirq steal guest guest_nice
        if (parts.Length < 5) return false;
        total = parts.Skip(1).Sum(p => long.TryParse(p, out var v) ? v : 0);
        long.TryParse(parts[4], out idle);
        return total > 0;
    }

    private static double ComputeSystemCpu(string snap1, string snap2)
    {
        if (!TryParseSystemTicks(snap1, out var idle1, out var total1)) return 0;
        if (!TryParseSystemTicks(snap2, out var idle2, out var total2)) return 0;
        var totalDelta = total2 - total1;
        var idleDelta  = idle2  - idle1;
        if (totalDelta <= 0) return 0;
        return Math.Round(100.0 * (totalDelta - idleDelta) / totalDelta, 1);
    }

    // Parses /proc/{pid}/stat (last line of the snapshot) for process ticks.
    private static double ComputeProcessCpu(string snap1, string snap2)
    {
        static bool TryParseProcPidStat(string snapshot, out long procTicks, out long totalTicks)
        {
            procTicks  = 0;
            totalTicks = 0;
            var lines = snapshot.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
            // Last non-empty line is /proc/{pid}/stat (we concatenated /proc/stat first then /proc/{pid}/stat)
            var pidLine = lines.LastOrDefault();
            if (pidLine is null) return false;
            var pidParts = pidLine.Trim().Split(' ');
            if (pidParts.Length < 17) return false;
            if (!long.TryParse(pidParts[13], out var utime)  ||
                !long.TryParse(pidParts[14], out var stime)  ||
                !long.TryParse(pidParts[15], out var cutime) ||
                !long.TryParse(pidParts[16], out var cstime))
                return false;
            procTicks = utime + stime + cutime + cstime;
            return TryParseSystemTicks(snapshot, out _, out totalTicks);
        }

        if (!TryParseProcPidStat(snap1, out var proc1, out var total1)) return 0;
        if (!TryParseProcPidStat(snap2, out var proc2, out var total2)) return 0;
        var totalDelta = total2 - total1;
        var procDelta  = proc2  - proc1;
        if (totalDelta <= 0) return 0;
        return Math.Round(100.0 * procDelta / totalDelta, 1);
    }

    private static void ParseFree(string output, ServerMetrics m)
    {
        var line = output.Split('\n')
                         .FirstOrDefault(l => l.StartsWith("Mem:", StringComparison.OrdinalIgnoreCase));
        if (line is null) return;
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && long.TryParse(parts[1], out var total)
            && long.TryParse(parts[2], out var used))
        {
            m.MemoryTotalMb = total;
            m.MemoryUsedMb  = used;
        }
    }

    private static void ParseDf(string output, ServerMetrics m)
    {
        var line = output.Split('\n').Skip(1).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
        if (line is null) return;
        var pct = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                      .FirstOrDefault(p => p.EndsWith('%'));
        if (pct is not null
            && double.TryParse(pct.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            m.DiskUsagePercent = val;
    }

    private static void ParseUptime(string output, ServerMetrics m)
    {
        var idx = output.IndexOf("load average:", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return;
        var rest  = output[(idx + "load average:".Length)..].Trim();
        var first = rest.Split(',').FirstOrDefault()?.Trim();
        if (first is not null
            && double.TryParse(first, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            m.LoadAverage = val;
    }
}
