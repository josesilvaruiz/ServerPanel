namespace ServerPanel.Models;

public class VpsMetricSnapshot
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; }
    public double CpuPercent { get; set; }
    public long MemoryUsedMb { get; set; }
    public long MemoryTotalMb { get; set; }
    public double DiskUsagePercent { get; set; }
    public double LoadAverage { get; set; }
}
