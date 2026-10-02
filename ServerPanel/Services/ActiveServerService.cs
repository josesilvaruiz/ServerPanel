using System.Collections.Concurrent;
using Microsoft.AspNetCore.Components.Authorization;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

// Registrado como Scoped: cada circuito de Blazor (y cada scope que crea un servicio en segundo plano)
// tiene su propio servidor "activo". La elección de un usuario se recuerda en memoria por nombre de
// usuario (sobrevive a recargas; se pierde al reiniciar el panel y vuelve al primero de la lista).
// Los servicios en segundo plano llaman a SetActive(...) en su propio scope: no tocan la elección de nadie.
public class ActiveServerService : IActiveServerService
{
    private static readonly ConcurrentDictionary<string, string> Selection = new();

    private readonly List<ServerConfig> _servers;
    private readonly AuthenticationStateProvider? _auth;
    private ServerConfig? _explicit;

    public event Action? OnChanged;

    public ActiveServerService(IConfiguration config, AuthenticationStateProvider? auth = null)
    {
        _auth = auth;
        _servers = config.GetSection("Servers").Get<List<ServerConfig>>() ?? [];

        if (_servers.Count == 0)
        {
            // Fallback: build one entry from the legacy sections
            _servers.Add(new ServerConfig
            {
                Name         = "Producción",
                Host         = config["ServerQuery:Host"] ?? config["Rcon:Host"] ?? "127.0.0.1",
                Port         = int.TryParse(config["ServerQuery:Port"], out var p) ? p : 27015,
                RconPassword = config["Rcon:Password"] ?? ""
            });
        }
    }

    public IReadOnlyList<ServerConfig> Servers => _servers;

    public ServerConfig Active
    {
        get
        {
            if (_explicit is not null) return _explicit;
            var user = CurrentUser();
            if (user is not null && Selection.TryGetValue(user, out var name))
                return _servers.FirstOrDefault(s => s.Name == name) ?? _servers[0];
            return _servers[0];
        }
    }

    public void SetActive(string name)
    {
        var found = _servers.FirstOrDefault(s => s.Name == name);
        if (found is null || found == Active) return;

        var user = CurrentUser();
        if (user is null) _explicit = found;   // scope sin usuario (segundo plano): fijo en este scope
        else Selection[user] = found.Name;
        OnChanged?.Invoke();
    }

    // En un circuito el proveedor devuelve una tarea ya completada, así que no bloquea.
    private string? CurrentUser()
    {
        try
        {
            var t = _auth?.GetAuthenticationStateAsync();
            if (t is null || !t.IsCompletedSuccessfully) return null;
            var id = t.Result.User.Identity;
            return id is { IsAuthenticated: true, Name: { Length: > 0 } n } ? n : null;
        }
        catch { return null; }
    }
}
