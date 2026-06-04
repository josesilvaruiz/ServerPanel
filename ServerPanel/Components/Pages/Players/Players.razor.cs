using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using ServerPanel.Models;
using ServerPanel.Contracts;
using ServerPanel.Components.Pages.Players.Modals;

namespace ServerPanel.Components.Pages.Players;

public partial class Players
{
    // PlayerService, Logger and JS are injected via @inject in Players.razor

    private PlayersViewModel Model = new();
    private PlayerInfo? SelectedPlayer;

    private IEnumerable<PlayerInfo> PagedPlayers =>
        Model.Players.Skip((Model.CurrentPage - 1) * Model.PageSize).Take(Model.PageSize);

    private int TotalPages =>
        Math.Max(1, (int)Math.Ceiling(Model.Players.Count / (double)Model.PageSize));

    protected override async Task OnInitializedAsync()
    {
        Logger.LogInformation("Players page initialized");
        await LoadPlayers();
    }

    private async Task LoadPlayers()
    {
        try
        {
            Logger.LogInformation("Loading players...");
            Model.Loading = true;
            Model.Players = await PlayerService.GetPlayersAsync();
            Logger.LogInformation("Players loaded: {Count}", Model.Players.Count);
            Model.CurrentPage = 1;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error loading players");
            Model.StatusMessage = ex.Message;
        }
        finally
        {
            Model.Loading = false;
            StateHasChanged();
        }
    }

    private void ShowProfile(PlayerInfo player) => SelectedPlayer = player;
    private void CloseProfile() => SelectedPlayer = null;

    private string PingClass(int ping) => ping switch
    {
        <= 60 => "pl-ping-good",
        <= 120 => "pl-ping-ok",
        _ => "pl-ping-bad"
    };

    private IEnumerable<int> VisiblePages()
    {
        if (TotalPages <= 7)
            return Enumerable.Range(1, TotalPages);

        var pages = new List<int> { 1 };
        if (Model.CurrentPage > 3) pages.Add(0);
        var start = Math.Max(2, Model.CurrentPage - 1);
        var end   = Math.Min(TotalPages - 1, Model.CurrentPage + 1);
        for (var i = start; i <= end; i++) pages.Add(i);
        if (Model.CurrentPage < TotalPages - 2) pages.Add(0);
        pages.Add(TotalPages);
        return pages;
    }

    private int PagerFrom => (Model.CurrentPage - 1) * Model.PageSize + 1;
    private int PagerTo   => Math.Min(Model.CurrentPage * Model.PageSize, Model.Players.Count);

    private void NextPage() { if (Model.CurrentPage < TotalPages) Model.CurrentPage++; }
    private void PreviousPage() { if (Model.CurrentPage > 1) Model.CurrentPage--; }
    private void GoToPage(int page) { if (page >= 1 && page <= TotalPages) Model.CurrentPage = page; }

    private string UnbanTarget = "";

    private async Task HandleUnban()
    {
        if (string.IsNullOrWhiteSpace(UnbanTarget)) return;
        var target = UnbanTarget.Trim();
        try
        {
            await PlayerService.UnbanPlayerAsync(target);
            Model.StatusMessage = $"Ban levantado: {target}";
            UnbanTarget = "";
        }
        catch (Exception ex)
        {
            Model.StatusMessage = ex.Message;
        }
    }

    private async Task CopyToClipboard(string text)
    {
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
    }

    // Modal de razones
    private bool ShowReasonModal;
    private string ReasonTitle = string.Empty;
    private bool ShowReason;
    private bool ShowReasonDuration;
    private int ReasonDuration = 30;

    private PlayerInfo? SelectedActionPlayer;
    private string SelectedAction = string.Empty;
    private ModalAction SelectedModalAction = ModalAction.Kick;

    private void OpenReasonModal(PlayerInfo player, string action)
    {
        SelectedActionPlayer = player;
        SelectedAction = action;

        ReasonTitle = action.ToUpperInvariant();
        ReasonDuration = 30;

        ShowReason = action.Equals("kick", StringComparison.OrdinalIgnoreCase) || action.Equals("ban", StringComparison.OrdinalIgnoreCase);

        ShowReasonDuration =
            action.Equals("mute", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("gag", StringComparison.OrdinalIgnoreCase) ||
            action.Equals("ban", StringComparison.OrdinalIgnoreCase);

        ShowReasonModal = true;

        if (!Enum.TryParse<ModalAction>(action, true, out var parsed))
        {
            parsed = ModalAction.Kick;
        }
        SelectedModalAction = parsed;
    }

    private async Task OnReasonConfirm(ModalResult args)
    {
        Logger.LogInformation("[Confirm] acción={Action} jugador={Player} reason='{Reason}' duration={Duration}",
            SelectedAction, SelectedActionPlayer?.Name, args.Reason, args.Duration);

        if (SelectedActionPlayer == null) { ShowReasonModal = false; return; }

        try
        {
            switch (args.Action)
            {
                case ModalAction.Kick:
                    Logger.LogInformation("[Service] KickPlayerAsync({Name})", SelectedActionPlayer.Name);
                    await PlayerService.KickPlayerAsync(SelectedActionPlayer.Name, args.Reason);
                    Model.StatusMessage = $"{SelectedActionPlayer.Name} expulsado.";
                    break;
                case ModalAction.Mute:
                    Logger.LogInformation("[Service] MutePlayerAsync({Name}, {Min}min)", SelectedActionPlayer.Name, args.Duration);
                    await PlayerService.MutePlayerAsync(SelectedActionPlayer.Name, args.Duration);
                    Model.StatusMessage = $"{SelectedActionPlayer.Name} muteado {args.Duration} min.";
                    break;
                case ModalAction.Gag:
                    Logger.LogInformation("[Service] GagPlayerAsync({Name}, {Min}min)", SelectedActionPlayer.Name, args.Duration);
                    await PlayerService.GagPlayerAsync(SelectedActionPlayer.Name, args.Duration);
                    Model.StatusMessage = $"{SelectedActionPlayer.Name} gaggeado {args.Duration} min.";
                    break;
                case ModalAction.Ban:
                    Logger.LogInformation("[Service] BanPlayerAsync({Name}, {Min}min, '{Reason}')", SelectedActionPlayer.Name, args.Duration, args.Reason);
                    await PlayerService.BanPlayerAsync(SelectedActionPlayer.Name, args.Duration, args.Reason);
                    Model.StatusMessage = $"{SelectedActionPlayer.Name} baneado {args.Duration} min.";
                    break;
                case ModalAction.Unban:
                    await PlayerService.UnbanPlayerAsync(SelectedActionPlayer.Name);
                    Model.StatusMessage = $"{SelectedActionPlayer.Name} desbaneado.";
                    break;
                default:
                    Logger.LogWarning("[Confirm] Acción desconocida en ModalResult: {Action}", args.Action);
                    break;
            }
            await LoadPlayers();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Confirm] Error ejecutando {Action}", SelectedAction);
            Model.StatusMessage = ex.Message;
        }
        finally
        {
            ShowReasonModal = false;
            SelectedActionPlayer = null;
            SelectedAction = string.Empty;
        }
    }

    private Task OnReasonCancel()
    {
        ShowReasonModal = false;
        SelectedActionPlayer = null;
        SelectedAction = string.Empty;
        return Task.CompletedTask;
    }
}
