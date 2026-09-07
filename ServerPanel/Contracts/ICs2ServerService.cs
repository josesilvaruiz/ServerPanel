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

    IAsyncEnumerable<string> StreamLiveConsoleAsync(CancellationToken ct);

    Task<string> GetRecentConsoleAsync(int seconds = 3);

    Task<string> GetRolloutStatusAsync();

    Task<List<WorkshopMap>> GetWorkshopMapsAsync(string collectionId);

    Task UpdateSimpleAdminWorkshopMapsAsync(IEnumerable<WorkshopMap> maps);

    /// <summary>Reescribe rtv_maps.json con la colección actual — SimpleRTV lo relee en
    /// cada cambio de mapa, así que los mapas nuevos aparecen en RTV/nominate solos,
    /// sin recargar el plugin.</summary>
    Task UpdateRtvMapsAsync(IEnumerable<WorkshopMap> maps);

    Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(string workshopId);
}
