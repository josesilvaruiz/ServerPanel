namespace ServerPanel.Contracts
{
    public interface ISshService
    {
        Task<string> ExecuteAsync(string command);
    }
}
