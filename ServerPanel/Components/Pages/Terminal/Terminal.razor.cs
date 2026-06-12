using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using ServerPanel.Contracts;
using ServerPanel.Data;
using ServerPanel.Models;

namespace ServerPanel.Components.Pages;

public partial class Terminal : IAsyncDisposable
{
    [Inject] ISshService Ssh { get; set; } = default!;
    [Inject] IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] AuthenticationStateProvider AuthState { get; set; } = default!;
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] IConfiguration Config { get; set; } = default!;

    string _userEmail = "";
    string HostLabel => Config["SshSettings:Host"] ?? Config["Ssh:Host"] ?? "servidor";

    List<TerminalSession> Sessions = new();
    int _maxZ = 10;
    int _windowOffset = 0;
    DotNetObjectReference<Terminal>? _dotNetRef;
    TerminalSession? _activeSession;

    // ── Cmd Panel (dockable) ─────────────────────────────────
    bool SidebarOpen { get; set; } = true;
    bool _mobSidebarOpen = false;
    string _cmdDock = "left";
    double _cmdPanelLeft = 0, _cmdPanelTop = 0;
    double _cmdPanelWidth = 260, _cmdPanelHeight = 400;
    bool _showExport = false;
    CmdGroup? _exportGroup = null;
    bool _cmdPanelJsInit = false;

    string CmdPanelStyle => _cmdDock switch {
        "left"   => $"left:0;top:0;width:{_cmdPanelWidth}px;height:100%",
        "right"  => $"right:0;top:0;width:{_cmdPanelWidth}px;height:100%",
        "top"    => $"left:0;top:0;width:100%;height:{_cmdPanelHeight}px",
        "bottom" => $"left:0;bottom:0;width:100%;height:{_cmdPanelHeight}px",
        _ => $"left:{_cmdPanelLeft}px;top:{_cmdPanelTop}px;width:{_cmdPanelWidth}px;height:{_cmdPanelHeight}px"
    };

    string WorkspacePadding => SidebarOpen ? _cmdDock switch {
        "left"   => $"margin-left:{_cmdPanelWidth}px",
        "right"  => $"margin-right:{_cmdPanelWidth}px",
        "top"    => $"margin-top:{_cmdPanelHeight}px",
        "bottom" => $"margin-bottom:{_cmdPanelHeight}px",
        _ => ""
    } : "";

    [JSInvokable]
    public void OnCmdPanelDocked(string dock, double x, double y, double w, double h)
    {
        _cmdDock = dock;
        if (dock == "float") { _cmdPanelLeft = x; _cmdPanelTop = y; }
        _cmdPanelWidth = w; _cmdPanelHeight = h;
        _ = JS.InvokeVoidAsync("localStorage.setItem", "cmd-dock",
            System.Text.Json.JsonSerializer.Serialize(new { dock, x, y, w, h }));
        StateHasChanged();
    }

    [JSInvokable]
    public void OnCmdPanelGeometry(double x, double y, double w, double h)
    {
        if (_cmdDock == "float") { _cmdPanelLeft = x; _cmdPanelTop = y; }
        _cmdPanelWidth = w; _cmdPanelHeight = h;
        StateHasChanged();
    }

    void ToggleSidebar()
    {
        SidebarOpen = !SidebarOpen;
        _mobSidebarOpen = SidebarOpen;
        if (SidebarOpen) _cmdPanelJsInit = false;
    }

    List<CmdGroup> CmdGroups { get; set; } = new();
    bool ShowNewGroup { get; set; }
    string NewGroupTitle { get; set; } = string.Empty;
    bool NewGroupShared { get; set; } = false;
    string _sidebarTab { get; set; } = "shared";
    IEnumerable<CmdGroup> TabGroups => _sidebarTab == "shared"
        ? CmdGroups.Where(g => g.IsShared)
        : CmdGroups.Where(g => !g.IsShared);

    IEnumerable<IGrouping<string, CmdGroup>> TabGroupsByTopic =>
        TabGroups.GroupBy(g => ExtractTopic(g.Title));

    static string ExtractTopic(string title)
    {
        var clean = System.Text.RegularExpressions.Regex.Replace(title.Trim(), @"^\P{L}+\s*", "");
        var idx = clean.IndexOf(" — ", StringComparison.Ordinal);
        return idx > 0 ? clean[..idx] : clean;
    }

    static string ExtractSubtitle(string title)
    {
        var idx = title.IndexOf(" — ", StringComparison.Ordinal);
        return idx > 0 ? title[(idx + 3)..] : title;
    }

    HashSet<string> _expandedTopics = new();
    bool IsTopicExpanded(string key) => _expandedTopics.Contains(key);
    void ToggleTopic(string key)
    {
        if (!_expandedTopics.Remove(key)) _expandedTopics.Add(key);
    }

    protected override void OnInitialized()
    {
        _dotNetRef = DotNetObjectReference.Create(this);
    }

    async void AddGroup()
    {
        var title = NewGroupTitle.Trim();
        if (string.IsNullOrEmpty(title)) return;
        var owner = NewGroupShared ? "all" : _userEmail;
        await using var db = await DbFactory.CreateDbContextAsync();
        var grp = new SavedCmdGroup { Title = title, Owner = owner, SortOrder = CmdGroups.Count };
        db.SavedCmdGroups.Add(grp);
        await db.SaveChangesAsync();
        CmdGroups.Add(new CmdGroup { DbId = grp.Id, Title = title, Owner = owner, Expanded = true });
        NewGroupTitle = string.Empty;
        NewGroupShared = false;
        ShowNewGroup = false;
        StateHasChanged();
    }

    async void DeleteGroup(CmdGroup grp)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var entity = await db.SavedCmdGroups.FindAsync(grp.DbId);
        if (entity != null) { db.SavedCmdGroups.Remove(entity); await db.SaveChangesAsync(); }
        CmdGroups.Remove(grp);
        StateHasChanged();
    }

    async void AddCmd(CmdGroup grp)
    {
        var text = grp.NewCmd.Trim();
        if (string.IsNullOrEmpty(text)) return;
        await using var db = await DbFactory.CreateDbContextAsync();
        var cmd = new SavedCmd { GroupId = grp.DbId, Text = text, SortOrder = grp.Commands.Count };
        db.SavedCmds.Add(cmd);
        await db.SaveChangesAsync();
        grp.Commands.Add(new CmdItem { DbId = cmd.Id, Text = text });
        grp.NewCmd = string.Empty;
        grp.ShowAdd = false;
        StateHasChanged();
    }

    async void DeleteCmd(CmdGroup grp, CmdItem item)
    {
        await using var db = await DbFactory.CreateDbContextAsync();
        var entity = await db.SavedCmds.FindAsync(item.DbId);
        if (entity != null) { db.SavedCmds.Remove(entity); await db.SaveChangesAsync(); }
        grp.Commands.Remove(item);
        StateHasChanged();
    }

    async void RunScript(CmdGroup grp)
    {
        if (grp.IsRunning || grp.Commands.Count == 0) return;
        if (_activeSession == null && Sessions.Count == 0) OpenNewTerminal();
        await Task.Delay(150);
        var target = _activeSession ?? Sessions.FirstOrDefault();
        if (target == null) return;
        grp.IsRunning = true;
        StateHasChanged();
        foreach (var item in grp.Commands.ToList())
        {
            await JS.InvokeVoidAsync("xtermSendLine", target.Id, item.Text);
            await Task.Delay(80);
        }
        grp.IsRunning = false;
        StateHasChanged();
    }

    void PasteCmd(TerminalSession? target, string cmd)
    {
        target ??= _activeSession ?? Sessions.FirstOrDefault();
        if (target == null) return;
        _mobSidebarOpen = false;
        _ = JS.InvokeVoidAsync("xtermPaste", target.Id, cmd);
    }

    // Overload for sidebar button (no session ref available in razor)
    void PasteCmd(string cmd) => PasteCmd(null, cmd);

    void HandleNewGroupKey(KeyboardEventArgs e)
    {
        if (e.Key == "Enter") AddGroup();
        else if (e.Key == "Escape") { ShowNewGroup = false; NewGroupTitle = string.Empty; }
    }

    string _cmdGroupsError = "";

    async Task LoadCmdGroups()
    {
        _cmdGroupsError = "";
        for (int i = 0; i < 5; i++)
        {
            try
            {
                await using var db = await DbFactory.CreateDbContextAsync();
                var groups = await db.SavedCmdGroups
                    .Include(g => g.Commands.OrderBy(c => c.SortOrder))
                    .Where(g => g.Owner == "all" || g.Owner == _userEmail)
                    .OrderBy(g => g.Owner == "all" ? 0 : 1).ThenBy(g => g.SortOrder)
                    .ToListAsync();
                CmdGroups = groups.Select(g => new CmdGroup
                {
                    DbId = g.Id,
                    Title = g.Title,
                    Owner = g.Owner,
                    Expanded = false,
                    Commands = g.Commands.Select(c => new CmdItem { DbId = c.Id, Text = c.Text }).ToList()
                }).ToList();
                StateHasChanged();
                return;
            }
            catch (Exception ex)
            {
                _cmdGroupsError = ex.Message;
                if (i < 4) await Task.Delay(2000);
            }
        }
        StateHasChanged();
    }

    [JSInvokable]
    public void OnWindowGeometry(string winId, double left, double top, double width, double height)
    {
        var sessionId = winId.StartsWith("win-") ? winId[4..] : winId;
        var s = Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (s != null) { s.Left = left; s.Top = top; s.Width = width; s.Height = height; }
        SaveLayout();
    }

    async Task ExportScript(string os)
    {
        if (_exportGroup == null) return;
        var cmds = _exportGroup.Commands.Select(c => c.Text).ToList();
        var safeName = System.Text.RegularExpressions.Regex.Replace(
            ExtractSubtitle(_exportGroup.Title).ToLower(), @"[^\w]+", "-").Trim('-');
        if (string.IsNullOrEmpty(safeName)) safeName = "commands";
        string content, filename;
        switch (os)
        {
            case "sh":
                content = $"#!/bin/bash\n# {_exportGroup.Title}\nset -e\n\n" + string.Join("\n", cmds);
                filename = safeName + ".sh"; break;
            case "ps1":
                content = $"# {_exportGroup.Title}\n\n" + string.Join("\n", cmds);
                filename = safeName + ".ps1"; break;
            default:
                content = $"@echo off\nREM {_exportGroup.Title}\n\n" + string.Join("\r\n", cmds);
                filename = safeName + ".bat"; break;
        }
        await JS.InvokeVoidAsync("downloadTextFile", filename, content);
        _showExport = false;
        _exportGroup = null;
    }

    string _prevDockForInit = "";

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await RestoreLayout();
            try
            {
                var json = await JS.InvokeAsync<string?>("localStorage.getItem", "cmd-dock");
                if (!string.IsNullOrEmpty(json))
                {
                    var d = System.Text.Json.JsonDocument.Parse(json).RootElement;
                    _cmdDock = d.GetProperty("dock").GetString() ?? "left";
                    if (_cmdDock == "float") {
                        _cmdPanelLeft = d.GetProperty("x").GetDouble();
                        _cmdPanelTop  = d.GetProperty("y").GetDouble();
                    }
                    _cmdPanelWidth  = d.GetProperty("w").GetDouble();
                    _cmdPanelHeight = d.GetProperty("h").GetDouble();
                }
            }
            catch { }
        }
        if (SidebarOpen && !_cmdPanelJsInit)
        {
            _cmdPanelJsInit = true;
            _prevDockForInit = _cmdDock;
            await Task.Delay(50);
            await JS.InvokeVoidAsync("initCmdPanel", "cmd-panel", "cmd-panel-titlebar", _dotNetRef);
            await InitCmdResizeHandles();
        }
        else if (SidebarOpen && _prevDockForInit != _cmdDock)
        {
            _prevDockForInit = _cmdDock;
            await Task.Delay(30);
            await InitCmdResizeHandles();
        }

        // Init xterm for sessions whose div is already in the DOM
        foreach (var s in Sessions.Where(s => !s.XtermInited && !s.Minimized).ToList())
        {
            try
            {
                var ready = await JS.InvokeAsync<bool>("initXterm", s.Id, s.FontSize);
                if (ready) s.XtermInited = true;
            }
            catch { /* JS error — will retry on next render */ }
        }
    }

    async Task InitCmdResizeHandles()
    {
        if (_cmdDock == "float")
            foreach (var dir in new[] { "se", "sw", "ne", "nw" })
                await JS.InvokeVoidAsync("initCmdPanelResize", "cmd-panel", $"cmd-rh-{dir}", _dotNetRef, dir);
        else
            await JS.InvokeVoidAsync("initCmdPanelResize", "cmd-panel", "cmd-edge-resize", _dotNetRef,
                _cmdDock == "left" ? "e" : _cmdDock == "right" ? "w" : _cmdDock == "top" ? "s" : "n");
    }

    async Task RestoreLayout()
    {
        var auth = await AuthState.GetAuthenticationStateAsync();
        _userEmail = auth.User.FindFirstValue(ClaimTypes.Email) ?? "";
        await LoadCmdGroups();
        try
        {
            var json = await JS.InvokeAsync<string?>("localStorage.getItem", "term-layout");
            if (!string.IsNullOrEmpty(json))
            {
                var items = System.Text.Json.JsonSerializer.Deserialize<List<LayoutItem>>(json);
                if (items != null && items.Count > 0)
                {
                    foreach (var item in items)
                    {
                        var s = new TerminalSession
                        {
                            Id = Guid.NewGuid().ToString("N"),
                            Left = item.Left, Top = item.Top,
                            Width = item.Width, Height = item.Height,
                            Minimized = item.Minimized, FontSize = item.FontSize,
                            ZIndex = ++_maxZ
                        };
                        Sessions.Add(s);
                    }
                    StateHasChanged();
                    foreach (var s in Sessions) await InitWindowJs(s);
                    return;
                }
            }
        }
        catch { }
        OpenNewTerminal();
    }

    public async ValueTask DisposeAsync()
    {
        _dotNetRef?.Dispose();
        try { await JS.InvokeVoidAsync("disposeAllXterms"); } catch { }
    }

    sealed class LayoutItem
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Minimized { get; set; }
        public int FontSize { get; set; } = 15;
    }

    void OpenNewTerminal()
    {
        var offset = _windowOffset * 28;
        _windowOffset = (_windowOffset + 1) % 10;
        var session = new TerminalSession
        {
            Id = Guid.NewGuid().ToString("N"),
            Left = 60 + offset,
            Top = 60 + offset,
            Width = 700,
            Height = 460,
            ZIndex = ++_maxZ
        };
        Sessions.Add(session);
        StateHasChanged();
        _ = InitWindowJs(session);
    }

    async Task InitWindowJs(TerminalSession session)
    {
        await Task.Delay(50);
        await JS.InvokeVoidAsync("setWindowGeometry", $"win-{session.Id}",
            session.Left, session.Top, session.Width, session.Height);
        await JS.InvokeVoidAsync("initDragWindow", $"win-{session.Id}", $"titlebar-{session.Id}", _dotNetRef);
        foreach (var dir in new[] { "nw", "ne", "sw", "se" })
            await JS.InvokeVoidAsync("initResizeWindow", $"win-{session.Id}", $"rh-{dir}-{session.Id}", _dotNetRef, dir);
    }

    void SaveLayout()
    {
        var layout = Sessions.Select(s => new LayoutItem
        {
            Left = s.Left, Top = s.Top, Width = s.Width, Height = s.Height,
            Minimized = s.Minimized, FontSize = s.FontSize
        }).ToList();
        _ = JS.InvokeVoidAsync("localStorage.setItem", "term-layout",
            System.Text.Json.JsonSerializer.Serialize(layout));
    }

    void CloseSession(TerminalSession s)
    {
        _ = JS.InvokeVoidAsync("disposeXterm", s.Id);
        Sessions.Remove(s);
        SaveLayout();
        StateHasChanged();
    }

    void ClearSession(TerminalSession s) =>
        _ = JS.InvokeVoidAsync("xtermClear", s.Id);

    void ToggleMinimize(TerminalSession s)
    {
        if (s.Maximized) { s.Maximized = false; }
        else
        {
            s.Minimized = !s.Minimized;
            if (!s.Minimized)
            {
                // Restore — fit xterm after render
                s.XtermInited = false;
            }
        }
        SaveLayout();
        StateHasChanged();
    }

    void ToggleMaximize(TerminalSession s)
    {
        s.Maximized = !s.Maximized;
        StateHasChanged();
        _ = JS.InvokeVoidAsync("xtermFit", s.Id);
    }

    void BringToFront(TerminalSession s)
    {
        s.ZIndex = ++_maxZ;
        _activeSession = s;
        StateHasChanged();
        _ = JS.InvokeVoidAsync("xtermFocus", s.Id);
    }

    async Task ExpandSession(TerminalSession s)
    {
        try
        {
            var pos = await JS.InvokeAsync<double[]?>("getWindowPos", $"win-{s.Id}");
            if (pos is { Length: 4 }) { s.Left = pos[0]; s.Top = pos[1]; s.Width = pos[2]; s.Height = pos[3]; }
        }
        catch { }

        if (s.IsExpanded)
        {
            s.Left = s.PreExpandLeft; s.Top    = s.PreExpandTop;
            s.Width = s.PreExpandWidth; s.Height = s.PreExpandHeight;
            s.IsExpanded = false;
        }
        else
        {
            s.PreExpandLeft = s.Left; s.PreExpandTop    = s.Top;
            s.PreExpandWidth = s.Width; s.PreExpandHeight = s.Height;
            s.Width = 1200; s.Height = 780;
            s.Left  = Math.Max(0, s.Left - 250);
            s.Top   = Math.Max(0, s.Top  - 160);
            s.IsExpanded = true;
        }
        s.ZIndex = ++_maxZ;
        SaveLayout();
        await JS.InvokeVoidAsync("setWindowGeometry", $"win-{s.Id}", s.Left, s.Top, s.Width, s.Height);
        await JS.InvokeVoidAsync("xtermFit", s.Id);
        StateHasChanged();
    }

    async void CopySession(TerminalSession s)
    {
        await JS.InvokeVoidAsync("xtermCopyAll", s.Id);
        s.Copied = true; StateHasChanged();
        await Task.Delay(2000);
        s.Copied = false; StateHasChanged();
    }

    async Task OnFontSizeChange(TerminalSession s, ChangeEventArgs e)
    {
        s.FontSize = int.Parse(e.Value!.ToString()!);
        SaveLayout();
        await JS.InvokeVoidAsync("setXtermFontSize", s.Id, s.FontSize);
    }

    // ── Help categories ──────────────────────────────────────
    sealed record HelpCmd(string Cmd, string Desc);
    sealed record HelpCat(string Name, string Icon, HelpCmd[] Commands);

    HelpCat[] HelpCategories =
    [
        new("tmux", "ti-layout-sidebar", [
            new("tmux ls",                             "Listar sesiones"),
            new("tmux attach -t cs2",                  "Conectar a sesión cs2"),
            new("tmux new -s nombre",                  "Nueva sesión"),
            new("tmux kill-session -t cs2",            "Matar sesión cs2"),
            new("tmux send-keys -t cs2 'cmd' Enter",   "Enviar comando"),
            new("tmux capture-pane -t cs2 -p",         "Capturar salida"),
            new("tmux rename-session -t old new",      "Renombrar sesión"),
        ]),
        new("Sistema", "ti-cpu", [
            new("uptime",                              "Tiempo encendido"),
            new("free -h",                             "RAM usada/libre"),
            new("df -h",                               "Espacio en disco"),
            new("top -bn1 | head -20",                 "Snapshot procesos"),
            new("ps aux | grep cs2",                   "Procesos CS2"),
            new("cat /proc/loadavg",                   "Carga del sistema"),
        ]),
        new("Red", "ti-network", [
            new("ss -tlnp",                            "Puertos en escucha"),
            new("ip a",                                "Interfaces de red"),
            new("curl -s ifconfig.me",                 "IP pública"),
            new("netstat -s | grep -i error",          "Errores de red"),
        ]),
        new("Ficheros", "ti-folder", [
            new("ls -lah /home/steam",                 "Directorio steam"),
            new("du -sh /home/steam/cs2",              "Tamaño CS2"),
            new("find /home/steam -name '*.log' -mmin -60", "Logs recientes"),
            new("tail -100 /home/steam/cs2/logs/server.txt", "Últimas líneas log"),
        ]),
        new("Servicio CS2", "ti-device-gamepad", [
            new("pgrep -a cs2",                        "Ver PID servidor"),
            new("kill -9 $(pgrep -f cs2)",             "Forzar parada CS2"),
            new("su - steam -c 'tmux ls'",             "Sesiones steam"),
            new("ls -lah /home/steam/cs2/game/csgo/addons", "Addons"),
        ]),
        new("Usuarios", "ti-users", [
            new("who",                                 "Usuarios conectados"),
            new("last | head -20",                     "Últimos logins"),
            new("id steam",                            "Info usuario steam"),
        ]),
    ];
}

public class TerminalSession
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Terminal";
    public bool ShowHelp { get; set; }
    public bool Copied { get; set; }
    public bool XtermInited { get; set; }
    public int FontSize { get; set; } = 15;

    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 700;
    public double Height { get; set; } = 460;
    public int ZIndex { get; set; } = 10;
    public bool Minimized { get; set; }
    public bool Maximized { get; set; }
    public bool IsExpanded { get; set; }
    public double PreExpandLeft { get; set; }
    public double PreExpandTop { get; set; }
    public double PreExpandWidth { get; set; } = 700;
    public double PreExpandHeight { get; set; } = 460;
}

public class CmdGroup
{
    public long DbId { get; set; }
    public string Title { get; set; } = "";
    public string Owner { get; set; } = "";
    public bool IsShared => Owner == "all";
    public List<CmdItem> Commands { get; set; } = new();
    public bool Expanded { get; set; } = true;
    public bool ShowAdd { get; set; }
    public bool IsRunning { get; set; }
    public string NewCmd { get; set; } = string.Empty;
}

public class CmdItem
{
    public long DbId { get; set; }
    public string Text { get; set; } = "";
}
