using Microsoft.AspNetCore.Components;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Components.Pages;

public partial class Metrics
{
    ServerMetrics? Data;
    bool Loading;
    string? Error;

    double MemPercent => Data is { MemoryTotalMb: > 0 }
        ? Math.Round(Data.MemoryUsedMb * 100.0 / Data.MemoryTotalMb, 1)
        : 0;

    string CpuColor  => Data is null ? "#4ade80"
        : Data.CpuUsagePercent > 85 ? "#f87171"
        : Data.CpuUsagePercent > 65 ? "#fbbf24"
        : "#4ade80";
    string MemColor  => MemPercent > 85 ? "#f87171" : MemPercent > 65 ? "#fbbf24" : "#4ade80";
    string DiskColor => Data is null ? "#4ade80"
        : Data.DiskUsagePercent > 85 ? "#f87171"
        : Data.DiskUsagePercent > 65 ? "#fbbf24"
        : "#4ade80";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await LoadMetrics();
    }

    async Task LoadMetrics()
    {
        Loading = true;
        Error = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            Data = await MetricsService.GetMetricsAsync();
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }
        finally
        {
            Loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }
}
