namespace ServerPanel.Contracts
{
    public interface ICs2ServerService
    {
        Task<bool> IsRunningAsync();
        Task StartAsync();
        Task StopAsync();
        Task RestartAsync();
    }
}
