namespace ServerPanel.Models;

public class ServerMetrics
{
    public long MemoryUsedMb { get; set; }
    public long MemoryTotalMb { get; set; }
    public double DiskUsagePercent { get; set; }
    public double CpuUsagePercent { get; set; }
    public double LoadAverage { get; set; }
    public double Cs2CpuPercent { get; set; }
    public long Cs2MemoryMb { get; set; }
    public bool Cs2Running { get; set; }
}
