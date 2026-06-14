using ServerPanel.Models;

public interface ICs2ServerService
{
    Task<bool> IsRunningAsync();

    Task StartAsync();

    Task StopAsync();

    Task RestartAsync();

    Task<UpdateResult> UpdateAsync();

    Task<string> ExecuteConsoleCommandAsync(string command);

    Task<string> GetLiveConsoleAsync();

    Task<string> GetRecentConsoleAsync(int seconds = 3);

    Task<string> GetRolloutStatusAsync();

    Task<List<WorkshopMap>> GetWorkshopMapsAsync(string collectionId);

    Task UpdateSimpleAdminWorkshopMapsAsync(IEnumerable<WorkshopMap> maps);
}
