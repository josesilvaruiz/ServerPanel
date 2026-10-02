using Microsoft.AspNetCore.Components;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Components.Pages;

public partial class OtherGames
{
    [Inject] IGameServerService Games { get; set; } = default!;

    private sealed class GameState
    {
        public bool Loading = true;
        public bool Running;
        public string Message = "";
        public bool Error;
        public string? Logs;
    }

    private readonly Dictionary<string, GameState> _states = [];
    private readonly HashSet<string> _busy = [];

    private GameState StateOf(GameServerConfig game)
    {
        if (!_states.TryGetValue(game.Name, out var state))
        {
            state = new GameState();
            _states[game.Name] = state;
        }
        return state;
    }

    protected override async Task OnInitializedAsync() => await RefreshAll();

    private async Task RefreshAll() =>
        await Task.WhenAll(Games.Games.Select(Refresh));

    private async Task Refresh(GameServerConfig game)
    {
        var state = StateOf(game);
        state.Loading = true;
        state.Running = await Games.IsRunningAsync(game);
        state.Loading = false;
        if (state.Logs is not null)
        {
            state.Logs = await Games.GetLogsAsync(game);
        }
    }

    private Task Start(GameServerConfig game) =>
        Act(game, Games.StartAsync, "Arrancando… puede tardar un par de minutos en aceptar conexiones.", 5000);

    private Task Stop(GameServerConfig game) =>
        Act(game, Games.StopAsync, "Servidor detenido.", 3000);

    private Task Restart(GameServerConfig game) =>
        Act(game, Games.RestartAsync, "Reiniciando…", 5000);

    private async Task Act(GameServerConfig game, Func<GameServerConfig, Task> action, string done, int settleMs)
    {
        var state = StateOf(game);
        _busy.Add(game.Name);
        try
        {
            await action(game);
            await Task.Delay(settleMs); // deja que Kubernetes cambie de estado antes de volver a preguntar
            await Refresh(game);
            state.Message = $"{done} ({DateTime.Now:HH:mm:ss})";
            state.Error = false;
        }
        catch (Exception ex)
        {
            state.Message = ex.Message;
            state.Error = true;
        }
        finally
        {
            _busy.Remove(game.Name);
        }
    }

    private async Task ToggleLogs(GameServerConfig game)
    {
        var state = StateOf(game);
        if (state.Logs is not null)
        {
            state.Logs = null;
            return;
        }

        _busy.Add(game.Name);
        try
        {
            state.Logs = await Games.GetLogsAsync(game);
        }
        catch (Exception ex)
        {
            state.Message = ex.Message;
            state.Error = true;
        }
        finally
        {
            _busy.Remove(game.Name);
        }
    }
}
