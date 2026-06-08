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

public partial class Terminal : IDisposable
{
    [Inject] ISshService Ssh { get; set; } = default!;
    [Inject] IDbContextFactory<ApplicationDbContext> DbFactory { get; set; } = default!;
    [Inject] AuthenticationStateProvider AuthState { get; set; } = default!;
    string _userEmail = "";
    [Inject] IJSRuntime JS { get; set; } = default!;
    [Inject] IConfiguration Config { get; set; } = default!;

    string HostLabel => Config["SshSettings:Host"] ?? "servidor";
    List<TerminalSession> Sessions = new();
    int _maxZ = 10;
    int _windowOffset = 0;
    DotNetObjectReference<Terminal>? _dotNetRef;
    TerminalSession? _activeSession;

    // ── Sidebar ──────────────────────────────────────────────
    bool SidebarOpen { get; set; } = true;
    bool _mobSidebarOpen = false;

    void ToggleSidebar()
    {
        SidebarOpen = !SidebarOpen;
        _mobSidebarOpen = SidebarOpen;
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
        await Task.Delay(100);
        var target = _activeSession ?? Sessions.FirstOrDefault();
        if (target == null) return;
        grp.IsRunning = true;
        StateHasChanged();
        foreach (var item in grp.Commands.ToList())
        {
            target.CurrentCommand = item.Text;
            await SendCommand(target);
        }
        grp.IsRunning = false;
        StateHasChanged();
    }

    void PasteCmd(string cmd)
    {
        var target = _activeSession ?? Sessions.FirstOrDefault();
        if (target == null) return;
        target.CurrentCommand = cmd;
        _mobSidebarOpen = false;
        StateHasChanged();
        _ = JS.InvokeVoidAsync("focusElement", $"inp-{target.Id}");
    }

    void HandleNewGroupKey(Microsoft.AspNetCore.Components.Web.KeyboardEventArgs e)
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

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        await RestoreLayout();
        await JS.InvokeVoidAsync("initSidebarResize", "term-sidebar", "term-sidebar-resize");
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
                            CurrentDir = item.CurrentDir, CurrentUser = item.CurrentUser,
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

    public void Dispose() => _dotNetRef?.Dispose();

    sealed class LayoutItem
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Minimized { get; set; }
        public int FontSize { get; set; } = 22;
        public string CurrentDir { get; set; } = "/root";
        public string CurrentUser { get; set; } = "root";
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
        await JS.InvokeVoidAsync("focusElement", $"inp-{session.Id}");
    }

    async Task SyncPositions()
    {
        foreach (var s in Sessions)
        {
            try
            {
                var pos = await JS.InvokeAsync<double[]?>("getWindowPos", $"win-{s.Id}");
                if (pos != null && pos.Length == 4)
                { s.Left = pos[0]; s.Top = pos[1]; s.Width = pos[2]; s.Height = pos[3]; }
            }
            catch { }
        }
        SaveLayout();
    }

    void SaveLayout()
    {
        var layout = Sessions.Select(s => new LayoutItem
        {
            Left = s.Left, Top = s.Top, Width = s.Width, Height = s.Height,
            Minimized = s.Minimized, FontSize = s.FontSize,
            CurrentDir = s.CurrentDir, CurrentUser = s.CurrentUser
        }).ToList();
        _ = JS.InvokeVoidAsync("localStorage.setItem", "term-layout",
            System.Text.Json.JsonSerializer.Serialize(layout));
    }

    void CloseSession(TerminalSession s)   { Sessions.Remove(s); SaveLayout(); StateHasChanged(); }
    void ClearSession(TerminalSession s)   { s.History.Clear(); StateHasChanged(); }
    void ToggleMinimize(TerminalSession s) { s.Minimized = !s.Minimized; SaveLayout(); StateHasChanged(); }
    void ToggleMaximize(TerminalSession s) { s.Maximized = !s.Maximized; StateHasChanged(); }
    void BringToFront(TerminalSession s)   { s.ZIndex = ++_maxZ; _activeSession = s; StateHasChanged(); }

    void ExpandSession(TerminalSession s)
    {
        s.Width  = 1200;
        s.Height = 780;
        s.Left   = Math.Max(0, s.Left - 250);
        s.Top    = Math.Max(0, s.Top  - 160);
        s.ZIndex = ++_maxZ;
        SaveLayout();
        StateHasChanged();
        _ = JS.InvokeVoidAsync("setWindowGeometry", $"win-{s.Id}", s.Left, s.Top, s.Width, s.Height);
    }

    async void CopySession(TerminalSession s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var e in s.History)
        {
            sb.AppendLine($"{e.Prompt} {e.Command}");
            if (!string.IsNullOrEmpty(e.Output)) sb.AppendLine(e.Output);
            sb.AppendLine();
        }
        await JS.InvokeVoidAsync("navigator.clipboard.writeText", sb.ToString());
        s.Copied = true; StateHasChanged();
        await Task.Delay(2000);
        s.Copied = false; StateHasChanged();
    }

    async Task HandleKey(KeyboardEventArgs e, TerminalSession s)
    {
        if (e.Key == "Enter") { await SendCommand(s); return; }
        if (e.Key == "ArrowUp")
        {
            if (s.CmdHistory.Count == 0) return;
            if (s.CmdHistoryIndex < s.CmdHistory.Count - 1) s.CmdHistoryIndex++;
            s.CurrentCommand = s.CmdHistory[s.CmdHistory.Count - 1 - s.CmdHistoryIndex];
            StateHasChanged(); return;
        }
        if (e.Key == "ArrowDown")
        {
            if (s.CmdHistoryIndex > 0) { s.CmdHistoryIndex--; s.CurrentCommand = s.CmdHistory[s.CmdHistory.Count - 1 - s.CmdHistoryIndex]; }
            else { s.CmdHistoryIndex = -1; s.CurrentCommand = ""; }
            StateHasChanged(); return;
        }
        if (e.Key == "l" && e.CtrlKey) ClearSession(s);
    }

    async Task SendCommand(TerminalSession s)
    {
        var cmd = s.CurrentCommand.Trim();
        if (string.IsNullOrEmpty(cmd) || s.IsRunning) return;
        s.CmdHistory.Add(cmd);
        s.CmdHistoryIndex = -1;
        s.CurrentCommand = "";
        s.IsRunning = true;
        var entry = new TermEntry { Prompt = s.Prompt, Command = cmd, IsRunning = true };
        s.History.Add(entry);
        StateHasChanged();
        await ScrollOutput(s);
        try
        {
            var suMatch = ParseSu(cmd);
            var isExit = cmd is "exit" or "logout";
            var isCd = cmd == "cd" || cmd.StartsWith("cd ") || cmd.StartsWith("cd\t");
            if (isExit && s.CurrentUser != "root")
            {
                s.CurrentUser = "root"; s.CurrentDir = "/root";
                entry.Output = ""; entry.IsRunning = false; return;
            }
            if (suMatch is not null)
            {
                var homeOut = (await Ssh.ExecuteAsync($"getent passwd {suMatch} | cut -d: -f6")).Trim();
                if (!string.IsNullOrEmpty(homeOut) && homeOut.StartsWith("/"))
                { s.CurrentUser = suMatch; s.CurrentDir = homeOut; entry.Output = $"→ {suMatch} ({homeOut})"; }
                else { entry.Output = $"Usuario '{suMatch}' no encontrado"; entry.IsError = true; }
                entry.IsRunning = false; return;
            }
            string sshCmd = isCd
                ? s.WrapCmd($"{(cmd == "cd" ? "cd ~" : cmd)} && pwd")
                : s.WrapCmd(cmd);
            var output = (await Ssh.ExecuteAsync(sshCmd)).TrimEnd();
            if (isCd)
            {
                var newDir = output.Split('\n').LastOrDefault(l => l.StartsWith("/"))?.Trim();
                if (!string.IsNullOrEmpty(newDir)) { s.CurrentDir = newDir; entry.Output = ""; }
                else { entry.Output = output; entry.IsError = !string.IsNullOrEmpty(output); }
            }
            else entry.Output = output;
            entry.IsRunning = false;
        }
        catch (Exception ex) { entry.Output = ex.Message; entry.IsError = true; entry.IsRunning = false; }
        finally
        {
            s.IsRunning = false;
            StateHasChanged();
            await ScrollOutput(s);
            await JS.InvokeVoidAsync("focusElement", $"inp-{s.Id}");
        }
    }

    async Task ScrollOutput(TerminalSession s) =>
        await JS.InvokeVoidAsync("scrollElementToBottom", $"out-{s.Id}");

    static string? ParseSu(string cmd)
    {
        var parts = cmd.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts[0] != "su") return null;
        if (parts.Length == 1) return "root";
        var last = parts[^1];
        return last.StartsWith("-") ? "root" : last;
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
    public string CurrentDir { get; set; } = "/root";
    public string CurrentUser { get; set; } = "root";
    public string Prompt => $"{CurrentUser}@cs2:{ShortDir}$ ";
    public string UserAtHost => $"{CurrentUser}@cs2:{CurrentDir}";
    string ShortDir => CurrentDir.StartsWith("/root") && CurrentUser == "root"
        ? "~" + CurrentDir[5..]
        : CurrentDir.StartsWith($"/home/{CurrentUser}")
            ? "~" + CurrentDir[(6 + CurrentUser.Length)..]
            : CurrentDir;

    public string WrapCmd(string inner)
    {
        var isRootHome = CurrentDir == "/root" || CurrentDir == "~";
        var cd = (CurrentUser == "root" && isRootHome) ? "" : $"cd '{CurrentDir.Replace("'", "'\\''")}' && ";
        var full = $"{cd}{inner}";
        return CurrentUser == "root" ? full : $"su - {CurrentUser} -c '{full.Replace("'", "\\'")}'";
    }

    public string CurrentCommand { get; set; } = "";
    public bool IsRunning { get; set; }
    public bool Copied { get; set; }
    public bool ShowHelp { get; set; }
    public List<TermEntry> History { get; set; } = new();
    public List<string> CmdHistory { get; set; } = new();
    public int CmdHistoryIndex { get; set; } = -1;
    public int FontSize { get; set; } = 22;

    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; } = 700;
    public double Height { get; set; } = 460;
    public int ZIndex { get; set; } = 10;
    public bool Minimized { get; set; }
    public bool Maximized { get; set; }
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

public class TermEntry
{
    public string Prompt { get; set; } = "";
    public string Command { get; set; } = "";
    public string Output { get; set; } = "";
    public bool IsRunning { get; set; }
    public bool IsError { get; set; }
}
