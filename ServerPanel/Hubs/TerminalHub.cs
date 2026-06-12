using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR;
using ServerPanel.Contracts;
using ServerPanel.Services;

namespace ServerPanel.Hubs;

public class TerminalHub : Hub
{
    sealed record TermInfo(SshShellSession Session, CancellationTokenSource Cts);

    private static readonly ConcurrentDictionary<string, TermInfo> _sessions = new();

    private readonly ISshService _ssh;
    private readonly ILogger<TerminalHub> _logger;

    public TerminalHub(ISshService ssh, ILogger<TerminalHub> logger)
    {
        _ssh    = ssh;
        _logger = logger;
    }

    static string Key(string connectionId, string sessionId) => $"{connectionId}:{sessionId}";

    public async Task OpenTerminal(string sessionId, uint cols, uint rows)
    {
        var key = Key(Context.ConnectionId, sessionId);
        if (_sessions.ContainsKey(key)) return;

        SshShellSession shell;
        try { shell = _ssh.OpenShell(Math.Max(cols, 10), Math.Max(rows, 5)); }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("Error", sessionId, ex.Message);
            return;
        }

        var cts = new CancellationTokenSource();
        _sessions[key] = new TermInfo(shell, cts);

        _ = Task.Run(async () =>
        {
            var buf = new byte[65536];
            try
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    int n = await shell.Stream.ReadAsync(buf, 0, buf.Length, cts.Token);
                    if (n == 0) break;
                    var b64 = Convert.ToBase64String(buf, 0, n);
                    await Clients.Caller.SendAsync("Output", sessionId, b64, cts.Token);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogDebug("Terminal read ended ({Id}): {Msg}", sessionId, ex.Message); }

            if (_sessions.TryRemove(key, out var info))
                info.Session.Dispose();

            try { await Clients.Caller.SendAsync("Closed", sessionId); } catch { }
        }, cts.Token);
    }

    public Task Input(string sessionId, string base64Data)
    {
        var key = Key(Context.ConnectionId, sessionId);
        if (!_sessions.TryGetValue(key, out var info)) return Task.CompletedTask;
        var data = Convert.FromBase64String(base64Data);
        info.Session.Stream.Write(data, 0, data.Length);
        info.Session.Stream.Flush();
        return Task.CompletedTask;
    }

    public Task Resize(string sessionId, uint cols, uint rows)
    {
        var key = Key(Context.ConnectionId, sessionId);
        if (_sessions.TryGetValue(key, out var info))
            info.Session.Resize(Math.Max(cols, 10), Math.Max(rows, 5));
        return Task.CompletedTask;
    }

    public Task CloseTerminal(string sessionId)
    {
        var key = Key(Context.ConnectionId, sessionId);
        if (_sessions.TryRemove(key, out var info))
        {
            info.Cts.Cancel();
            info.Session.Dispose();
        }
        return Task.CompletedTask;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var prefix = Context.ConnectionId + ":";
        foreach (var key in _sessions.Keys.Where(k => k.StartsWith(prefix)).ToList())
        {
            if (_sessions.TryRemove(key, out var info))
            {
                info.Cts.Cancel();
                info.Session.Dispose();
            }
        }
        return base.OnDisconnectedAsync(exception);
    }
}
