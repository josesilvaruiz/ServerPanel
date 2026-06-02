using ServerPanel.Models;

public interface ICs2ServerService
{
    Task<bool> IsRunningAsync();

    Task StartAsync();

    Task StopAsync();

    Task RestartAsync();

    Task<UpdateResult> UpdateAsync();

    Task<string> ExecuteConsoleCommandAsync(string command);

    Task<List<WorkshopMap>> GetWorkshopMapsAsync(string collectionId);
}
