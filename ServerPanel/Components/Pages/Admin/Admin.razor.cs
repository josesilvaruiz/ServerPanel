using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using ServerPanel.Contracts;
using ServerPanel.Models;
using System.Linq;

namespace ServerPanel.Components.Pages;

public partial class Admin : IDisposable
{
    [Inject] IJSRuntime JS { get; set; } = default!;

    string ActiveSection = "map";
    CancellationTokenSource? _consoleCts;

    string PermSub = "get";
    Dictionary<string, PermissionEntry> AllAdmins = new();
    bool AllAdminsLoading;
    int AdminPage = 1;
    const int AdminPageSize = 10;
    int AdminTotalPages => Math.Max(1, (int)Math.Ceiling(AllAdmins.Count / (double)AdminPageSize));
    IEnumerable<KeyValuePair<string, PermissionEntry>> PagedAdmins =>
        AllAdmins.Skip((AdminPage - 1) * AdminPageSize).Take(AdminPageSize);
    int AdminPagerFrom => (AdminPage - 1) * AdminPageSize + 1;
    int AdminPagerTo   => Math.Min(AdminPage * AdminPageSize, AllAdmins.Count);

    IEnumerable<int> AdminVisiblePages()
    {
        if (AdminTotalPages <= 7)
            return Enumerable.Range(1, AdminTotalPages);
        var pages = new List<int> { 1 };
        if (AdminPage > 3) pages.Add(0);
        var start = Math.Max(2, AdminPage - 1);
        var end   = Math.Min(AdminTotalPages - 1, AdminPage + 1);
        for (var i = start; i <= end; i++) pages.Add(i);
        if (AdminPage < AdminTotalPages - 2) pages.Add(0);
        pages.Add(AdminTotalPages);
        return pages;
    }

    void AdminNextPage()     { if (AdminPage < AdminTotalPages) AdminPage++; }
    void AdminPrevPage()     { if (AdminPage > 1) AdminPage--; }
    void AdminGoToPage(int p) { if (p >= 1 && p <= AdminTotalPages) AdminPage = p; }
    string SharedSteamId = "";
    string PermPlayerName = "";
    HashSet<string> SelectedPerms = new();
    string RemoveFlag = "";
    PermissionEntry? LoadedEntry;

    private record PermOption(string Value, string Label, string Icon, string Desc);

    private List<PermOption> PermOptions { get; } = new()
    {
        new("@css/root",        "Root",       "ti-crown",            "Acceso total"),
        new("@css/reservation", "Reserva",    "ti-ticket",           "Slot reservado"),
        new("@css/generic",     "Genérico",   "ti-shield",           "Admin básico"),
        new("@css/kick",        "Kick",       "ti-user-off",         "Expulsar jugadores"),
        new("@css/ban",         "Ban",        "ti-ban",              "Banear jugadores"),
        new("@css/unban",       "Unban",      "ti-lock-open",        "Quitar baneos"),
        new("@css/vip",         "VIP",        "ti-diamond",          "Estado VIP"),
        new("@css/slay",        "Slay",       "ti-skull",            "Matar/dañar jugadores"),
        new("@css/changemap",   "Changemap",  "ti-map",              "Cambiar mapa"),
        new("@css/cvar",        "Cvar",       "ti-adjustments",      "Modificar cvars"),
        new("@css/config",      "Config",     "ti-file-settings",    "Ejecutar configs"),
        new("@css/chat",        "Chat",       "ti-message",          "Chat de administrador"),
        new("@css/vote",        "Vote",       "ti-checkbox",         "Crear votaciones"),
        new("@css/password",    "Password",   "ti-lock",             "Cambiar contraseña"),
        new("@css/rcon",        "RCON",       "ti-terminal",         "Comandos RCON"),
        new("@css/cheats",      "Cheats",     "ti-mood-crazy-happy", "Usar sv_cheats"),
    };

    void TogglePerm(string value)
    {
        if (!SelectedPerms.Add(value))
            SelectedPerms.Remove(value);
    }

    string ConsoleCommand = "";
    List<string> _consoleLines = new();
    const int MaxConsoleLines = 300;
    bool ConsoleCopied = false;
    bool ShowCmdHelp = false;

    void AppendConsoleLine(string line)
    {
        _consoleLines.Add(line);
        if (_consoleLines.Count > MaxConsoleLines)
            _consoleLines.RemoveRange(0, _consoleLines.Count - MaxConsoleLines);
    }

    string GetConsoleDisplay() =>
        _consoleLines.Count == 0 ? "Conectando al servidor..." : string.Join('\n', _consoleLines);

    private record CmdEntry(string Cmd, string Desc);
    private record CmdCategory(string Name, string Icon, CmdEntry[] Commands);

    private readonly CmdCategory[] CmdCategories =
    [
        new("Admin.Cmd.Match", "ti-trophy", [
            new("mp_restartgame 1",        "Admin.Cmd.RestartGame"),
            new("mp_roundtime 2",          "Admin.Cmd.RoundTime"),
            new("mp_maxrounds 30",         "Admin.Cmd.MaxRounds"),
            new("mp_halftime 1",           "Admin.Cmd.Halftime"),
            new("mp_overtime_enable 1",    "Admin.Cmd.Overtime"),
            new("mp_warmuptime 30",        "Admin.Cmd.WarmupTime"),
            new("mp_warmup_end",           "Admin.Cmd.WarmupEnd"),
            new("mp_pause_match",          "Admin.Cmd.PauseMatch"),
            new("mp_unpause_match",        "Admin.Cmd.UnpauseMatch"),
        ]),
        new("Admin.Cmd.Players", "ti-users", [
            new("status",                  "Admin.Cmd.StatusPlayers"),
            new("kick #id",                "Admin.Cmd.KickById"),
            new("banid 60 #id",            "Admin.Cmd.BanId"),
            new("mp_friendlyfire 1",       "Admin.Cmd.FriendlyFire"),
            new("mp_autoteambalance 1",    "Admin.Cmd.AutoBalance"),
            new("mp_limitteams 0",         "Admin.Cmd.TeamLimit"),
            new("sv_alltalk 1",            "Admin.Cmd.AllTalk"),
        ]),
        new("Admin.Cmd.Bots", "ti-robot", [
            new("bot_add",                 "Admin.Cmd.BotAdd"),
            new("bot_add_t",               "Admin.Cmd.BotAddT"),
            new("bot_add_ct",              "Admin.Cmd.BotAddCT"),
            new("bot_kick",                "Admin.Cmd.BotKick"),
            new("bot_stop 1",              "Admin.Cmd.BotStop"),
            new("bot_difficulty 3",        "Admin.Cmd.BotDifficulty"),
            new("bot_quota 5",             "Admin.Cmd.BotQuota"),
        ]),
        new("Admin.Cmd.Maps", "ti-map", [
            new("changelevel de_dust2",    "Admin.Cmd.Dust2"),
            new("changelevel de_mirage",   "Admin.Cmd.Mirage"),
            new("changelevel de_inferno",  "Admin.Cmd.Inferno"),
            new("changelevel de_nuke",     "Admin.Cmd.Nuke"),
            new("changelevel de_ancient",  "Admin.Cmd.Ancient"),
            new("changelevel de_anubis",   "Admin.Cmd.Anubis"),
            new("changelevel de_vertigo",  "Admin.Cmd.Vertigo"),
        ]),
        new("Admin.Cmd.Server", "ti-server", [
            new("sv_cheats 1",             "Admin.Cmd.CheatsOn"),
            new("sv_cheats 0",             "Admin.Cmd.CheatsOff"),
            new("sv_password \"1234\"",    "Admin.Cmd.SetPassword"),
            new("sv_password \"\"",        "Admin.Cmd.RemovePassword"),
            new("sv_lan 0",                "Admin.Cmd.OnlineMode"),
            new("sv_airaccelerate 12",     "Admin.Cmd.AirAccelerate"),
            new("sv_gravity 800",          "Admin.Cmd.Gravity"),
            new("exec gamemode_competitive","Admin.Cmd.ExecCompetitive"),
        ]),
        new("Admin.Cmd.Practice", "ti-target", [
            new("sv_cheats 1; sv_infinite_ammo 1; sv_grenade_trajectory_prac_pipreview 1; sv_showimpacts 1", "Admin.Cmd.PracticeMode"),
            new("noclip",                  "Admin.Cmd.Noclip"),
            new("give weapon_flashbang",   "Admin.Cmd.Flashbang"),
            new("give weapon_smokegrenade","Admin.Cmd.Smoke"),
            new("give weapon_molotov",     "Admin.Cmd.Molotov"),
            new("mp_buy_anywhere 1",       "Admin.Cmd.BuyAnywhere"),
        ]),
    ];

    bool ToastVisible;
    bool ToastOk;
    string ToastMessage = "";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await Task.WhenAll(LoadMaps(), FetchCurrentMap());
    }

    async Task FetchCurrentMap()
    {
        try
        {
            var info = await ServerQueryService.GetServerInfoAsync();
            CurrentMap = info.Map;
            await InvokeAsync(StateHasChanged);
        }
        catch
        {
            CurrentMap = null;
        }
    }

    const string WorkshopCollectionId = "3736332535";

    List<WorkshopMap> WorkshopMaps = new();
    WorkshopMap? DraggedMap;
    bool DropActive;
    bool MapLoading;
    string? MapError;
    string MapSearch = "";
    string? CurrentMap;
    async Task DownloadConfig()
    {
        try
        {
            await Cs2ServerService.UpdateSimpleAdminWorkshopMapsAsync(WorkshopMaps);
            ShowToast(true, "WorkshopMaps actualizado en el servidor");
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
    }

    void SetTab(string tab)
    {
        ActiveSection = tab;
        DraggedMap = null;
        StateHasChanged();
        if (tab == "map" && WorkshopMaps.Count == 0)
            _ = LoadMaps();
        if (tab == "console")
            StartLiveConsole();
        else
            StopLiveConsole();
    }

    void StartLiveConsole()
    {
        StopLiveConsole();
        _consoleLines.Clear();
        _consoleCts = new CancellationTokenSource();
        _ = StreamLoopAsync(_consoleCts.Token);
    }

    async Task StreamLoopAsync(CancellationToken token)
    {
        try
        {
            await foreach (var line in Cs2ServerService.StreamLiveConsoleAsync(token))
            {
                AppendConsoleLine(line);
                await InvokeAsync(async () =>
                {
                    StateHasChanged();
                    try { await JS.InvokeVoidAsync("scrollConsoleToBottom", "live-console-pre"); } catch { }
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            await InvokeAsync(() =>
            {
                AppendConsoleLine($"[Error de conexión: {ex.Message}]");
                StateHasChanged();
            });
        }
    }

    void StopLiveConsole()
    {
        _consoleCts?.Cancel();
        _consoleCts?.Dispose();
        _consoleCts = null;
    }

    public void Dispose() => StopLiveConsole();

    async Task HandleLoadAllAdmins()
    {
        PermSub = "list";
        AllAdminsLoading = true;
        StateHasChanged();
        try
        {
            AllAdmins = await PlayerService.GetAllAdminsAsync();
            AdminPage = 1;
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
        finally
        {
            AllAdminsLoading = false;
            StateHasChanged();
        }
    }

    async Task HandleGetPerms()
    {
        if (string.IsNullOrWhiteSpace(SharedSteamId)) return;
        try
        {
            LoadedEntry = await PlayerService.GetPermissionsAsync(SharedSteamId);
            ShowToast(LoadedEntry != null,
                LoadedEntry != null
                    ? $"{LoadedEntry.Flags.Count} flag(s) cargados"
                    : "Usuario no encontrado en admins.json");
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
    }

    async Task HandleSetPerms()
    {
        if (string.IsNullOrWhiteSpace(PermPlayerName)) return;
        try
        {
            await PlayerService.SetPermissionsAsync(PermPlayerName, SharedSteamId, string.Join(";", SelectedPerms));
            LoadedEntry = await PlayerService.GetPermissionsAsync(SharedSteamId);
            ShowToast(true, $"Permisos asignados a {PermPlayerName}");
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
    }

    async Task HandleRemovePerm()
    {
        if (string.IsNullOrWhiteSpace(SharedSteamId) || string.IsNullOrWhiteSpace(RemoveFlag)) return;
        try
        {
            await PlayerService.RemovePermissionAsync(SharedSteamId, RemoveFlag);
            LoadedEntry = await PlayerService.GetPermissionsAsync(SharedSteamId);
            ShowToast(true, $"Flag {RemoveFlag} eliminado");
            RemoveFlag = "";
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
    }

    async Task LoadMaps()
    {
        MapLoading = true;
        MapError = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            WorkshopMaps = await Cs2ServerService.GetWorkshopMapsAsync(WorkshopCollectionId);
        }
        catch (Exception ex)
        {
            MapError = ex.Message;
        }
        finally
        {
            MapLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    async Task HandleMapDrop()
    {
        DropActive = false;
        if (DraggedMap is null) return;
        var map = DraggedMap;
        DraggedMap = null;
        try
        {
            await Cs2ServerService.ExecuteConsoleCommandAsync($"host_workshop_map {map.Id}");
            CurrentMap = map.Name;
            ShowToast(true, $"Cargando: {map.Name}");
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
    }

    async void CopyConsole()
    {
        if (_consoleLines.Count == 0) return;
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", GetConsoleDisplay());
        ConsoleCopied = true;
        StateHasChanged();
        await Task.Delay(2000);
        ConsoleCopied = false;
        StateHasChanged();
    }

    async Task HandleConsoleKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") await HandleConsole();
    }

    async Task HandleConsole()
    {
        if (string.IsNullOrWhiteSpace(ConsoleCommand)) return;
        var cmd = ConsoleCommand.Trim();
        ConsoleCommand = "";

        AppendConsoleLine($"> {cmd}");
        await InvokeAsync(StateHasChanged);

        try
        {
            var response = await Cs2ServerService.ExecuteConsoleCommandAsync(cmd);
            if (!string.IsNullOrWhiteSpace(response))
            {
                foreach (var line in response.Split('\n'))
                    if (!string.IsNullOrWhiteSpace(line))
                        AppendConsoleLine($"  {line.Trim()}");
            }
        }
        catch (Exception ex)
        {
            AppendConsoleLine($"[Error RCON: {ex.Message}]");
        }

        await InvokeAsync(StateHasChanged);
        await JS.InvokeVoidAsync("scrollConsoleToBottom", "live-console-pre");
    }

    async void ShowToast(bool ok, string msg)
    {
        ToastVisible = true;
        ToastOk = ok;
        ToastMessage = msg;
        StateHasChanged();
        await Task.Delay(3000);
        ToastVisible = false;
        StateHasChanged();
    }
}
