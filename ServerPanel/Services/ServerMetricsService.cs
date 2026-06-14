using System.Globalization;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class ServerMetricsService : IServerMetricsService
{
    private readonly ISshService _ssh;
    private readonly string _ns;
    private readonly string _dep;

    public ServerMetricsService(ISshService ssh, IConfiguration configuration)
    {
        _ssh = ssh;
        _ns  = configuration["Kubernetes:Namespace"]  ?? "cs2";
        _dep = configuration["Kubernetes:Deployment"] ?? "cs2-server";
    }

    public async Task<ServerMetrics> GetMetricsAsync()
    {
        var metrics = new ServerMetrics();

        var freeOut   = await _ssh.ExecuteAsync("free -m");
        var dfOut     = await _ssh.ExecuteAsync("df -h /");
        var uptimeOut = await _ssh.ExecuteAsync("uptime");

        ParseFree(freeOut, metrics);
        ParseDf(dfOut, metrics);
        ParseUptime(uptimeOut, metrics);

        // CS2 pod resource usage via kubectl top
        var topOut = await _ssh.ExecuteAsync(
            $"kubectl top pod -n {_ns} -l app=cs2 --no-headers 2>/dev/null | head -1");

        if (!string.IsNullOrWhiteSpace(topOut))
            ParseKubectlTop(topOut.Trim(), metrics);

        // CPU usage for the node
        var stat1 = await _ssh.ExecuteAsync("cat /proc/stat");
        await Task.Delay(1000);
        var stat2 = await _ssh.ExecuteAsync("cat /proc/stat");
        metrics.CpuUsagePercent = ComputeSystemCpu(stat1, stat2);

        return metrics;
    }

    private static void ParseKubectlTop(string line, ServerMetrics m)
    {
        // Format: "cs2-server-xxx   500m   1200Mi"
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return;

        var cpuStr = parts[1];
        var memStr = parts[2];

        // Memory: "1200Mi" → MB
        if (memStr.EndsWith("Mi", StringComparison.OrdinalIgnoreCase)
            && long.TryParse(memStr[..^2], out var memMi))
        {
            m.Cs2MemoryMb = memMi;
            m.Cs2Running  = true;
        }
        else if (memStr.EndsWith("Gi", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(memStr[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var memGi))
        {
            m.Cs2MemoryMb = (long)(memGi * 1024);
            m.Cs2Running  = true;
        }

        // CPU: "500m" → millicores → approximate % (500m = 0.5 core ≈ 50% on 1 core)
        if (cpuStr.EndsWith('m') && double.TryParse(cpuStr[..^1],
            NumberStyles.Float, CultureInfo.InvariantCulture, out var milli))
        {
            m.Cs2CpuPercent = Math.Round(milli / 10.0, 1); // rough: 1000m = 100%
        }
    }

    private static bool TryParseSystemTicks(string snapshot, out long idle, out long total)
    {
        idle  = 0;
        total = 0;
        var cpuLine = snapshot.Split('\n')
            .FirstOrDefault(l => l.StartsWith("cpu ", StringComparison.Ordinal));
        if (cpuLine is null) return false;
        var parts = cpuLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
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
