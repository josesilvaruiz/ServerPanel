using System.Collections.Concurrent;
using ServerPanel.Contracts;

namespace ServerPanel.Services;

public class ManualActionTracker : IManualActionTracker
{
    private readonly ConcurrentDictionary<string, DateTime> _lastActionUtc = new();

    public void MarkAction(string serverName) =>
        _lastActionUtc[serverName] = DateTime.UtcNow;

    public bool WasRecentlyActedOn(string serverName, TimeSpan window) =>
        _lastActionUtc.TryGetValue(serverName, out var lastActionUtc)
        && DateTime.UtcNow - lastActionUtc < window;
}
