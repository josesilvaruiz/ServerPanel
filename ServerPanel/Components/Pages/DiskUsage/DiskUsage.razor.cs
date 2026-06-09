using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using ServerPanel.Contracts;
using System.Text;

namespace ServerPanel.Components.Pages;

public partial class DiskUsage : ComponentBase, IAsyncDisposable
{
    [Inject] ISshService              Ssh    { get; set; } = default!;
    [Inject] NavigationManager        Nav    { get; set; } = default!;
    [Inject] ILogger<DiskUsage>       Logger { get; set; } = default!;
    [Inject] IJSRuntime               JS     { get; set; } = default!;

    DotNetObjectReference<DiskUsage>? _dotNetRef;
    IJSObjectReference?               _dragCleanup;

    // ── Records ──
    record DiskInfo(string Size, string Used, string Avail, int UsedPct);
    record DiskEntry(string Path, string Name, string SizeHuman, long Bytes, bool IsDir);
    record BcPart(string Label, string Path);

    // ── Page state ──
    DiskInfo?       Disk;
    List<DiskEntry> Entries      = new();
    string          _currentPath = "/";
    bool            Loading      = true;
    string?         Error;

    // ── Context menu ──
    DiskEntry? _ctxTarget;
    double     _ctxX;
    double     _ctxY;

    // ── Rename modal ──
    DiskEntry? _renameTarget;
    string     _renameNewName   = "";
    bool       _renameDestExists;

    // ── Delete modal ──
    DiskEntry? _deleteTarget;
    int        _deleteCooldown;   // counts down 3→0 before button enables

    // ── Edit modal ──
    DiskEntry? _editTarget;
    string     _editContent     = "";
    bool       _editLoading;
    bool       _editSaveConfirm;

    // ── Upload state ──
    class UploadTask { public string Name = ""; public bool Done; public string? Error; }
    List<UploadTask> _uploads   = new();
    bool             _uploading;  // guard: prevents concurrent drop uploads

    // ── Shared action state ──
    bool    _actionRunning;
    string? _actionError;

    // ─────────────────────────────────────────
    // SECURITY HELPERS
    // ─────────────────────────────────────────

    // Single-quote escape: makes any path safe inside bash single-quoted strings.
    // 'path' → handles spaces, $, backticks, semicolons, etc.
    // The only character that breaks single-quoting is ' itself — we escape it as '\''
    static string Esc(string path) => "'" + path.Replace("'", "'\\''") + "'";

    // Block deletion/rename of root and first-level system directories.
    // A path like /home/user/backup is fine (depth 2); /home or /bin is not (depth 1).
    static bool IsDeleteBlocked(string path)
    {
        var segs = ("/" + path.Trim('/')).TrimEnd('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segs.Length <= 1; // depth 0 = /, depth 1 = /home /bin /etc etc.
    }

    // Validate that a rename target is a safe filename (no traversal, no injection).
    static string? ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))        return "El nombre no puede estar vacío.";
        if (name is "." or "..")                     return "Nombre reservado.";
        if (name.Contains('/'))                      return "El nombre no puede contener '/'.";
        if (name.Contains('\0'))                     return "El nombre contiene caracteres inválidos.";
        // Prevent relative traversal
        if (name.StartsWith(".."))                   return "El nombre no puede empezar por '..'.";
        return null; // OK
    }

    // ─────────────────────────────────────────
    // LIFECYCLE
    // ─────────────────────────────────────────
    ElementReference _wrapRef;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        _dotNetRef   = DotNetObjectReference.Create(this);
        _dragCleanup = await JS.InvokeAsync<IJSObjectReference>("spSetupDisk", _wrapRef, _dotNetRef);
        await ScanAsync("/");
    }

    // ── JS-invokable drag-upload methods ──────────────────────────────────────

    [JSInvokable]
    public Task BeginDrop(string[] fileNames)
    {
        if (_uploading) return Task.CompletedTask;
        _uploading = true;
        _uploads.Clear();
        foreach (var n in fileNames) _uploads.Add(new UploadTask { Name = n });
        return InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task UploadFromDrop(string targetPath, string fileName, IJSStreamReference streamRef)
    {
        var task = _uploads.FirstOrDefault(u => u.Name == fileName && !u.Done && u.Error is null)
                   ?? new UploadTask { Name = fileName };
        if (!_uploads.Contains(task)) _uploads.Add(task);
        try
        {
            Logger.LogInformation("DiskUsage DROP {Name} → {Path}", fileName, targetPath);
            var dest = targetPath.TrimEnd('/') + "/" + fileName;
            await using var stream = await streamRef.OpenReadStreamAsync(maxAllowedSize: 200L * 1024 * 1024);
            await Ssh.UploadFileAsync(dest, stream);
            task.Done = true;
        }
        catch (Exception ex)
        {
            task.Error = ex.Message;
            Logger.LogWarning(ex, "DiskUsage DROP failed {Name}", fileName);
        }
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnDropComplete()
    {
        try
        {
            // Show completed tasks briefly, then scan and clear
            await Task.Delay(1200);
            _uploads.Clear();
            await InvokeAsync(StateHasChanged);
            await ScanAsync(_currentPath);
        }
        finally
        {
            _uploading = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_dragCleanup is not null)
        {
            try { await _dragCleanup.InvokeVoidAsync("dispose"); } catch { }
            await _dragCleanup.DisposeAsync();
        }
        _dotNetRef?.Dispose();
    }

    // ─────────────────────────────────────────
    // SCAN
    // ─────────────────────────────────────────
    async Task ScanAsync(string path)
    {
        Loading      = true;
        Error        = null;
        _currentPath = path;
        CloseCtx();
        await InvokeAsync(StateHasChanged);
        try
        {
            var dir = path.TrimEnd('/') + "/";

            if (path == "/" || Disk is null)
            {
                var dfRaw = await Ssh.ExecuteAsync("df -h / | awk 'NR==2'");
                Disk = ParseDf(dfRaw.Trim());
            }

            // Paths come from the server's own filesystem — Esc() protects against
            // filenames with special characters reaching the shell.
            var cmd = $"du -sh {Esc(dir)}* 2>/dev/null | sort -rh | head -40" +
                      $" && echo '___DIRS___'" +
                      $" && find {Esc(dir)} -maxdepth 1 -mindepth 1 -type d 2>/dev/null | sort";

            var raw = await Ssh.ExecuteAsync(cmd);
            Entries = ParseEntries(raw);
        }
        catch (Exception ex) { Error = ex.Message; }
        finally
        {
            Loading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    // ─────────────────────────────────────────
    // CONTEXT MENU
    // ─────────────────────────────────────────
    void OpenCtx(MouseEventArgs args, DiskEntry e)
    {
        _ctxTarget   = e;
        _ctxX        = args.ClientX;
        _ctxY        = args.ClientY;
        _actionError = null;
    }

    void CloseCtx() => _ctxTarget = null;

    Task GoUpAsync()
    {
        if (_currentPath == "/") return Task.CompletedTask;
        var parent = System.IO.Path.GetDirectoryName(_currentPath.TrimEnd('/'))?.Replace('\\', '/') ?? "/";
        if (string.IsNullOrEmpty(parent)) parent = "/";
        return ScanAsync(parent);
    }

    // ─────────────────────────────────────────
    // OPEN MODALS
    // ─────────────────────────────────────────
    void OpenRename(DiskEntry e)
    {
        CloseCtx();
        _renameTarget     = e;
        _renameNewName    = e.Name;
        _renameDestExists = false;
        _actionError      = null;
    }

    void OpenDelete(DiskEntry e)
    {
        CloseCtx();
        if (IsDeleteBlocked(e.Path))
        {
            _deleteTarget = e;
            _actionError  = $"Directorio de sistema protegido. Entra dentro y borra elementos individuales.";
            _deleteCooldown = 0;
            return;
        }
        _deleteTarget   = e;
        _actionError    = null;
        _deleteCooldown = 3;
        _ = RunDeleteCooldownAsync();
    }

    async Task RunDeleteCooldownAsync()
    {
        while (_deleteCooldown > 0)
        {
            await Task.Delay(1000);
            if (_deleteTarget is null) return;
            _deleteCooldown--;
            await InvokeAsync(StateHasChanged);
        }
    }

    async Task OpenEditAsync(DiskEntry e)
    {
        CloseCtx();
        _editTarget      = e;
        _editContent     = "";
        _editLoading     = true;
        _editSaveConfirm = false;
        _actionError     = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            // Check mime type — refuse binary files before loading content
            var mime = await Ssh.ExecuteAsync($"file --mime-type -b {Esc(e.Path)} 2>/dev/null");
            mime = mime.Trim();
            if (!mime.StartsWith("text/") && mime != "application/json" &&
                mime != "application/xml" && mime != "application/x-sh" &&
                mime != "application/x-shellscript" && mime != "inode/x-empty")
            {
                _actionError = $"Archivo binario ({mime}) — no se puede editar como texto.";
                _editLoading = false;
                return;
            }

            var sizeRaw = await Ssh.ExecuteAsync($"wc -c < {Esc(e.Path)} 2>/dev/null");
            if (long.TryParse(sizeRaw.Trim(), out var size) && size > 512 * 1024)
            {
                _actionError = $"Archivo demasiado grande ({size / 1024} KB). Límite: 512 KB.";
                _editLoading = false;
                return;
            }
            _editContent = await Ssh.ExecuteAsync($"cat {Esc(e.Path)} 2>/dev/null");
        }
        catch (Exception ex) { _actionError = ex.Message; }
        finally
        {
            _editLoading = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    void CloseModals()
    {
        if (_actionRunning) return;
        _renameTarget     = null;
        _deleteTarget     = null;
        _editTarget       = null;
        _renameDestExists = false;
        _editSaveConfirm  = false;
        _actionError      = null;
        _deleteCooldown   = 0;
    }

    // ─────────────────────────────────────────
    // CONFIRM: RENAME
    // ─────────────────────────────────────────
    async Task ConfirmRenameAsync()
    {
        if (_renameTarget is null) return;

        // Validate new name before touching the server
        var validationError = ValidateFileName(_renameNewName);
        if (validationError is not null) { _actionError = validationError; return; }

        _actionError = null;

        // Build destination path from the original directory + validated new name only
        var segments = _renameTarget.Path.Split('/');
        var parentDir = string.Join("/", segments.SkipLast(1));
        if (parentDir.Length == 0) parentDir = "/";
        var dest = parentDir.TrimEnd('/') + "/" + _renameNewName.Trim();

        if (!_renameDestExists)
        {
            _actionRunning = true;
            await InvokeAsync(StateHasChanged);
            try
            {
                var check = await Ssh.ExecuteAsync($"test -e {Esc(dest)} && echo EXISTS || echo OK");
                if (check.Trim() == "EXISTS") { _renameDestExists = true; return; }
            }
            catch (Exception ex) { _actionError = ex.Message; return; }
            finally { _actionRunning = false; await InvokeAsync(StateHasChanged); }
        }

        _actionRunning = true;
        await InvokeAsync(StateHasChanged);
        try
        {
            Logger.LogWarning("DiskUsage RENAME {Src} → {Dst}", _renameTarget.Path, dest);
            await Ssh.ExecuteAsync($"mv {Esc(_renameTarget.Path)} {Esc(dest)}");
            CloseModals();
            await ScanAsync(_currentPath);
        }
        catch (Exception ex) { _actionError = ex.Message; }
        finally { _actionRunning = false; await InvokeAsync(StateHasChanged); }
    }

    // ─────────────────────────────────────────
    // CONFIRM: DELETE
    // ─────────────────────────────────────────
    async Task ConfirmDeleteAsync()
    {
        if (_deleteTarget is null) return;

        // Double-check protection at execution time (not just at modal open)
        if (IsDeleteBlocked(_deleteTarget.Path))
        {
            _actionError = "Operación bloqueada: directorio de sistema protegido.";
            return;
        }

        _actionRunning = true;
        _actionError   = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            Logger.LogWarning("DiskUsage DELETE {Path} (size: {Size})", _deleteTarget.Path, _deleteTarget.SizeHuman);
            var flag = _deleteTarget.IsDir ? "-rf" : "-f";
            await Ssh.ExecuteAsync($"rm {flag} {Esc(_deleteTarget.Path)}");
            CloseModals();
            await ScanAsync(_currentPath);
        }
        catch (Exception ex) { _actionError = ex.Message; }
        finally { _actionRunning = false; await InvokeAsync(StateHasChanged); }
    }

    // ─────────────────────────────────────────
    // CONFIRM: SAVE EDIT
    // ─────────────────────────────────────────
    async Task ConfirmSaveAsync()
    {
        if (_editTarget is null) return;
        _actionRunning = true;
        _actionError   = null;
        await InvokeAsync(StateHasChanged);
        try
        {
            Logger.LogWarning("DiskUsage EDIT {Path}", _editTarget.Path);
            // base64 encode the content so NO user-supplied character can escape the shell command
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(_editContent));
            await Ssh.ExecuteAsync($"printf '%s' '{b64}' | base64 -d > {Esc(_editTarget.Path)}");
            CloseModals();
        }
        catch (Exception ex) { _actionError = ex.Message; _editSaveConfirm = false; }
        finally { _actionRunning = false; await InvokeAsync(StateHasChanged); }
    }

    // ─────────────────────────────────────────
    // UPLOAD
    // ─────────────────────────────────────────
    async Task HandleUploadAsync(InputFileChangeEventArgs e)
    {
        var files = e.GetMultipleFiles(20);
        _uploads.Clear();
        foreach (var f in files)
            _uploads.Add(new UploadTask { Name = f.Name });

        await InvokeAsync(StateHasChanged);

        for (int i = 0; i < files.Count; i++)
        {
            var file = files[i];
            var task = _uploads[i];
            try
            {
                Logger.LogInformation("DiskUsage UPLOAD {Name} → {Path}", file.Name, _currentPath);
                var dest = _currentPath.TrimEnd('/') + "/" + file.Name;
                using var stream = file.OpenReadStream(maxAllowedSize: 200L * 1024 * 1024);
                await Ssh.UploadFileAsync(dest, stream);
                task.Done = true;
            }
            catch (Exception ex)
            {
                task.Error = ex.Message;
                Logger.LogWarning(ex, "DiskUsage UPLOAD failed {Name}", file.Name);
            }
            await InvokeAsync(StateHasChanged);
        }

        await ScanAsync(_currentPath);
        await Task.Delay(4000);
        _uploads.Clear();
        await InvokeAsync(StateHasChanged);
    }

    // ─────────────────────────────────────────
    // DOWNLOAD URL
    // ─────────────────────────────────────────
    string GetDownloadUrl(string path) =>
        Nav.ToAbsoluteUri("api/file/download?path=" + Uri.EscapeDataString(path)).ToString();

    // ─────────────────────────────────────────
    // PARSERS
    // ─────────────────────────────────────────
    static DiskInfo? ParseDf(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 5) return null;
        int.TryParse(parts[4].TrimEnd('%'), out var pct);
        return new DiskInfo(parts[1], parts[2], parts[3], pct);
    }

    static List<DiskEntry> ParseEntries(string raw)
    {
        const string sentinel = "___DIRS___";
        var idx     = raw.IndexOf(sentinel, StringComparison.Ordinal);
        var duPart  = idx >= 0 ? raw[..idx] : raw;
        var dirPart = idx >= 0 ? raw[(idx + sentinel.Length)..] : "";

        var dirs = new HashSet<string>(
            dirPart.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                   .Select(l => l.Trim().TrimEnd('/')),
            StringComparer.Ordinal);

        var entries = new List<DiskEntry>();
        foreach (var line in duPart.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = line.IndexOf('\t');
            if (tab < 0) continue;
            var sizeStr = line[..tab].Trim();
            var path    = line[(tab + 1)..].Trim().TrimEnd('/');
            if (string.IsNullOrEmpty(path)) continue;
            var name  = path.Split('/').LastOrDefault(p => p.Length > 0) ?? path;
            var bytes = ParseSizeToBytes(sizeStr);
            entries.Add(new DiskEntry(path, name, sizeStr, bytes, dirs.Contains(path)));
        }
        return entries;
    }

    static long ParseSizeToBytes(string s)
    {
        s = s.Trim().ToUpperInvariant();
        if (s.Length == 0) return 0;
        char unit = s[^1];
        if (!double.TryParse(s.Length > 1 ? s[..^1] : s,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v)) return 0;
        return unit switch { 'G' => (long)(v * 1073741824), 'M' => (long)(v * 1048576), 'K' => (long)(v * 1024), _ => (long)v };
    }

    static readonly HashSet<string> _textExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cfg", ".conf", ".config", ".json", ".yaml", ".yml",
        ".ini", ".sh", ".bash", ".env", ".log", ".xml", ".toml",
        ".md", ".csv", ".py", ".js", ".ts", ".css", ".html", ".properties"
    };

    static bool IsTextFile(string name) =>
        _textExtensions.Contains(Path.GetExtension(name));

    static List<BcPart> GetBreadcrumbParts(string path)
    {
        var parts = new List<BcPart> { new("raíz", "/") };
        if (path == "/") return parts;
        var acc = "";
        foreach (var seg in path.Trim('/').Split('/').Where(s => s.Length > 0))
        {
            acc += "/" + seg;
            parts.Add(new(seg, acc));
        }
        return parts;
    }
}
