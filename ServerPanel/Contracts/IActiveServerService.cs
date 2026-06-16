using ServerPanel.Models;

namespace ServerPanel.Contracts;

public interface IActiveServerService
{
    ServerConfig              Active  { get; }
    IReadOnlyList<ServerConfig> Servers { get; }
    void SetActive(string name);
    event Action? OnChanged;
}
