using Microsoft.AspNetCore.Components;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Components.Pages;

public partial class Admin
{
    // Cs2ServerService and PlayerService are injected via @inject in Admin.razor

    string ActiveSection = "map";

    string PermSub = "get";
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
    string ConsoleOutput = "";

    bool ToastVisible;
    bool ToastOk;
    string ToastMessage = "";

    protected override async Task OnInitializedAsync()
    {
        await Task.WhenAll(LoadMaps(), FetchCurrentMap());
    }

    async Task FetchCurrentMap()
    {
        try
        {
            var info = await ServerQueryService.GetServerInfoAsync();
            CurrentMap = info.Map;
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

    void SetTab(string tab)
    {
        ActiveSection = tab;
        DraggedMap = null;
        if (tab == "map" && WorkshopMaps.Count == 0)
            _ = LoadMaps();
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
        StateHasChanged();
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
            StateHasChanged();
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

    async Task HandleConsole()
    {
        if (string.IsNullOrWhiteSpace(ConsoleCommand)) return;
        try
        {
            ConsoleOutput = await Cs2ServerService.ExecuteConsoleCommandAsync(ConsoleCommand);
            ConsoleCommand = "";
        }
        catch (Exception ex)
        {
            ShowToast(false, ex.Message);
        }
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
