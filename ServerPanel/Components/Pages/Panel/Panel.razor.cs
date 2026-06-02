using Microsoft.AspNetCore.Components;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Components.Pages;

public partial class Panel
{
    [Inject] ICs2ServerService Cs2 { get; set; } = default!;
    [Inject] IServerQueryService QueryService { get; set; } = default!;

    private bool Loading = true;
    private bool ActionRunning = false;
    private bool IsRunning = false;
    private ServerInfo? ServerInfo;
    private string StatusMessage = string.Empty;

    protected override async Task OnInitializedAsync()
    {
        await RefreshStatus();
    }

    private async Task RefreshStatus()
    {
        try
        {
            Loading = true;
            IsRunning = await Cs2.IsRunningAsync();
            if (IsRunning)
            {
                ServerInfo = await QueryService.GetServerInfoAsync();
            }
            else
            {
                ServerInfo = null;
            }
            StatusMessage = $"Última actualización: {DateTime.Now:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.ToString();
        }
        finally
        {
            Loading = false;
        }
    }

    private async Task StartServer()
    {
        try
        {
            ActionRunning = true;
            await Cs2.StartAsync();
            await Task.Delay(5000);
            await RefreshStatus();
            StatusMessage = "Servidor iniciado correctamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            ActionRunning = false;
        }
    }

    private async Task StopServer()
    {
        try
        {
            ActionRunning = true;
            await Cs2.StopAsync();
            await Task.Delay(3000);
            await RefreshStatus();
            StatusMessage = "Servidor detenido correctamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            ActionRunning = false;
        }
    }

    private async Task RestartServer()
    {
        try
        {
            ActionRunning = true;
            await Cs2.RestartAsync();
            await Task.Delay(8000);
            await RefreshStatus();
            StatusMessage = "Servidor reiniciado correctamente.";
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            ActionRunning = false;
        }
    }

    private async Task UpdateServer()
    {
        try
        {
            ActionRunning = true;
            StatusMessage = "Actualizando servidor...";
            var result = await Cs2.UpdateAsync();
            await RefreshStatus();
            if (result.AlreadyUpdated)
            {
                StatusMessage = $"El servidor ya estaba actualizado (Build {result.LocalBuild})";
            }
            else if (result.Updated)
            {
                StatusMessage = $"Actualizado de {result.LocalBuild} a {result.RemoteBuild}";
            }
            else
            {
                StatusMessage = string.IsNullOrWhiteSpace(result.Output)
                    ? "No se pudo completar la actualización."
                    : result.Output;
            }
        }
        catch (Exception ex)
        {
            StatusMessage = ex.Message;
        }
        finally
        {
            ActionRunning = false;
        }
    }
}
