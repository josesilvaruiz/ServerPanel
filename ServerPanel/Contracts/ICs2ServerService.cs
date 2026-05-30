using ServerPanel.Models;

public interface ICs2ServerService
{
    Task<bool> IsRunningAsync();

    Task StartAsync();

    Task StopAsync();

    Task RestartAsync();

    Task<UpdateResult> UpdateAsync();
}