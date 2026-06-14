namespace ServerPanel.Contracts;

public interface IRconService
{
    bool IsConfigured { get; }
    Task<string> ExecuteAsync(string command, CancellationToken ct = default);
}
