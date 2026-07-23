using System.Text.Json;
using System.Text.Json.Serialization;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class Cs2ServerService : ICs2ServerService
{
    private readonly ISshService _ssh;
    private readonly IRconService _rcon;
    private readonly IActiveServerService _activeServer;
    private readonly IManualActionTracker _manualActionTracker;
    private readonly ILogger<Cs2ServerService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    private string Ns  => _activeServer.Active.KubeNamespace;
    private string Dep => _activeServer.Active.KubeDeployment;
    private string ConfigBasePath   => _activeServer.Active.KubeConfigBasePath;
    private string ContainerCssPath => _activeServer.Active.KubeContainerCssPath;

    private string Kube(string args) => $"kubectl {args}";

    private string KubeExec(string shellCmd) =>
        Kube($"exec -n {Ns} deployment/{Dep} -- bash -c {ShellEscape(shellCmd)}");

    private static string ShellEscape(string s) =>
        "'" + s.Replace("'", "'\\''") + "'";

    public Cs2ServerService(
        ISshService ssh,
        IRconService rcon,
        IActiveServerService activeServer,
        IManualActionTracker manualActionTracker,
        ILogger<Cs2ServerService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _ssh           = ssh;
        _rcon          = rcon;
        _activeServer  = activeServer;
        _manualActionTracker = manualActionTracker;
        _logger        = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> IsRunningAsync()
    {
        try
        {
            var result = await _ssh.ExecuteAsync(
                Kube($"get deployment {Dep} -n {Ns} -o jsonpath='{{.status.readyReplicas}}' 2>/dev/null || echo 0"));
            var v = result.Trim();
            var running = !string.IsNullOrWhiteSpace(v) && v != "0" && v != "<no value>";
            _logger.LogInformation("CS2 K8s readyReplicas='{V}' running={R}", v, running);
            return running;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comprobando estado CS2");
            return false;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            _logger.LogInformation("Escalando {Dep} a 1 réplica", Dep);
            await _ssh.ExecuteAsync(Kube($"scale deployment {Dep} -n {Ns} --replicas=1"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error iniciando servidor CS2");
            throw;
        }
    }

    public async Task StopAsync()
    {
        // Antes de ejecutar: si falla el kubectl, mejor pecar de no alertar en un stop fallido
        // que dejar una ventana sin marcar en la que un poll pille el servidor cayendo.
        _manualActionTracker.MarkAction(_activeServer.Active.Name);
        try
        {
            _logger.LogInformation("Escalando {Dep} a 0 réplicas", Dep);
            await _ssh.ExecuteAsync(Kube($"scale deployment {Dep} -n {Ns} --replicas=0"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deteniendo servidor CS2");
            throw;
        }
    }

    public async Task RestartAsync()
    {
        _manualActionTracker.MarkAction(_activeServer.Active.Name);
        try
        {
            _logger.LogInformation("Reiniciando deployment {Dep}", Dep);
            await _ssh.ExecuteAsync(Kube($"rollout restart deployment/{Dep} -n {Ns}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reiniciando servidor CS2");
            throw;
        }
    }

    public async Task<string> GetLiveConsoleAsync()
    {
        return await _ssh.ExecuteAsync(
            Kube($"logs -n {Ns} deployment/{Dep} --tail=200 2>/dev/null"));
    }

    public IAsyncEnumerable<string> StreamLiveConsoleAsync(CancellationToken ct) =>
        _ssh.StreamCommandAsync(
            Kube($"logs -f -n {Ns} deployment/{Dep} --tail=50 2>/dev/null"),
            ct);

    public async Task<string> GetRecentConsoleAsync(int seconds = 3)
    {
        return await _ssh.ExecuteAsync(
            Kube($"logs -n {Ns} deployment/{Dep} --since={seconds}s 2>/dev/null"));
    }

    public async Task<string> GetRolloutStatusAsync()
    {
        return await _ssh.ExecuteAsync(
            Kube($"rollout status deployment/{Dep} -n {Ns} --timeout=5s 2>&1"));
    }

    public async Task<string> ExecuteConsoleCommandAsync(string command)
    {
        _logger.LogInformation("Ejecutando comando consola via RCON: {Cmd}", command);
        var output = await _rcon.ExecuteAsync(command);
        return output;
    }

    public async Task<UpdateResult> UpdateAsync()
    {
        try
        {
            // Buildid actual publicado en Steam y el que hay instalado en el PV.
            var remoteBuild = await GetRemoteBuildIdAsync();
            var localBuild  = (await _ssh.ExecuteAsync(
                KubeExec("cat /home/steam/cs2/.buildid 2>/dev/null || echo none"))).Trim();

            _logger.LogInformation("CS2 update: local={Local} remote={Remote}", localBuild, remoteBuild);

            // Ya está al día: no tocar nada.
            if (!string.IsNullOrWhiteSpace(remoteBuild) && remoteBuild == localBuild)
            {
                return new UpdateResult
                {
                    AlreadyUpdated = true,
                    LocalBuild     = localBuild,
                    RemoteBuild    = remoteBuild,
                    Output         = $"El servidor ya está en el build {localBuild}."
                };
            }

            // Actualizar SteamCMD sobre el PV (persiste reinicios). Sin tragar errores.
            _logger.LogInformation("Actualizando CS2 via SteamCMD sobre el PV");
            var updateOutput = await _ssh.ExecuteAsync(KubeExec(
                "/home/steam/steamcmd/steamcmd.sh +force_install_dir /home/steam/cs2 " +
                "+login anonymous +app_update 730 +quit"));

            // Fijar el buildid en .buildid para que panel y cronjob queden sincronizados.
            if (!string.IsNullOrWhiteSpace(remoteBuild))
                await _ssh.ExecuteAsync(KubeExec($"echo {remoteBuild} > /home/steam/cs2/.buildid"));

            // Reinicio limpio (rápido, ~7s, persiste desde el PV).
            await _ssh.ExecuteAsync(Kube($"rollout restart deployment/{Dep} -n {Ns}"));

            return new UpdateResult
            {
                Updated     = true,
                LocalBuild  = string.IsNullOrWhiteSpace(localBuild) ? "desconocido" : localBuild,
                RemoteBuild = string.IsNullOrWhiteSpace(remoteBuild) ? "actual" : remoteBuild,
                Output      = $"Actualización aplicada, reiniciando servidor...\n{updateOutput}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error actualizando CS2");
            return new UpdateResult { Output = ex.Message };
        }
    }

    private async Task<string> GetRemoteBuildIdAsync()
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.UserAgent.ParseAdd("ServerPanel/1.0");
            var json = await client.GetStringAsync("https://api.steamcmd.net/v1/info/730");
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement
                .GetProperty("data").GetProperty("730")
                .GetProperty("depots").GetProperty("branches")
                .GetProperty("public").GetProperty("buildid").GetString() ?? "";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo obtener el buildid remoto de Steam");
            return "";
        }
    }

    public async Task<List<WorkshopMap>> GetWorkshopMapsAsync(string collectionId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ServerPanel/1.0");

        var collectionBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["collectioncount"] = "1",
            ["publishedfileids[0]"] = collectionId
        });

        var collectionResp = await client.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetCollectionDetails/v1/",
            collectionBody);

        collectionResp.EnsureSuccessStatusCode();
        var collectionJson = await collectionResp.Content.ReadAsStringAsync();
        var collectionRoot = JsonSerializer.Deserialize<CollectionDetailsRoot>(collectionJson);
        var children = collectionRoot?.Response?.CollectionDetails?.FirstOrDefault()?.Children;

        if (children is null || children.Count == 0) return [];

        var fileParams = new Dictionary<string, string> { ["itemcount"] = children.Count.ToString() };
        for (var i = 0; i < children.Count; i++)
            fileParams[$"publishedfileids[{i}]"] = children[i].PublishedFileId;

        var detailsResp = await client.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            new FormUrlEncodedContent(fileParams));

        detailsResp.EnsureSuccessStatusCode();
        var detailsJson = await detailsResp.Content.ReadAsStringAsync();
        var detailsRoot = JsonSerializer.Deserialize<PublishedFileDetailsRoot>(detailsJson);

        return detailsRoot?.Response?.PublishedFileDetails
            ?.Select(d => new WorkshopMap
            {
                Id = d.PublishedFileId,
                Name = d.Title,
                PreviewUrl = d.PreviewUrl
            })
            .ToList() ?? [];
    }

    private string SimpleAdminConfigPath =>
        $"{_activeServer.Active.KubeHostCssPath}/configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json";

    public async Task UpdateSimpleAdminWorkshopMapsAsync(IEnumerable<WorkshopMap> maps)
    {
        var mapList = maps.ToList();
        if (mapList.Count == 0)
            throw new InvalidOperationException("La lista de mapas está vacía");

        var mapsJson = JsonSerializer.Serialize(
            mapList.ToDictionary(m => m.Name, m => long.Parse(m.Id)));
        var mapsB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(mapsJson));

        var py = "import json,base64,sys;" +
                 $"p='{SimpleAdminConfigPath}';" +
                 $"m=json.loads(base64.b64decode('{mapsB64}').decode());" +
                 "c=open(p).read();" +
                 "s=c.index('{');comm=c[:s];data=json.loads(c[s:]);" +
                 "data['WorkshopMaps']=m;" +
                 "open(p,'w').write(comm+json.dumps(data,indent=2));" +
                 "print('ok')";

        var pyB64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(py));
        // Run on the SSH host directly — SimpleAdminConfigPath is now the host-side path
        // (the container user has no write permission via kubectl exec)
        var result = await _ssh.ExecuteAsync($"echo {pyB64} | base64 -d | python3");
        _logger.LogInformation("Workshop update result: {Result}", result);

        if (!result.Contains("ok"))
            throw new InvalidOperationException($"Error: {result.Trim()}");
    }

    // ── Steam API DTOs ────────────────────────────────────────────────────────

    private sealed class CollectionDetailsRoot
    {
        [JsonPropertyName("response")] public CollectionResponse? Response { get; set; }
    }

    private sealed class CollectionResponse
    {
        [JsonPropertyName("collectiondetails")] public List<CollectionDetail>? CollectionDetails { get; set; }
    }

    private sealed class CollectionDetail
    {
        [JsonPropertyName("children")] public List<CollectionChild>? Children { get; set; }
    }

    private sealed class CollectionChild
    {
        [JsonPropertyName("publishedfileid")] public string PublishedFileId { get; set; } = "";
    }

    private sealed class PublishedFileDetailsRoot
    {
        [JsonPropertyName("response")] public PublishedFileResponse? Response { get; set; }
    }

    private sealed class PublishedFileResponse
    {
        [JsonPropertyName("publishedfiledetails")] public List<PublishedFileDetail>? PublishedFileDetails { get; set; }
    }

    private sealed class PublishedFileDetail
    {
        [JsonPropertyName("publishedfileid")] public string PublishedFileId { get; set; } = "";
        [JsonPropertyName("result")]           public int    Result          { get; set; }
        [JsonPropertyName("title")]           public string Title           { get; set; } = "";
        [JsonPropertyName("preview_url")]     public string PreviewUrl      { get; set; } = "";
        [JsonPropertyName("time_updated")]    public long   TimeUpdated     { get; set; }
    }

    // result == 1 significa "ok" en la API de Steam Workshop; cualquier otro valor
    // (9 = no encontrado, 15 = acceso denegado, etc.) significa que el item no es usable.
    private const int SteamWorkshopResultOk = 1;

    public async Task<WorkshopItemInfo?> GetWorkshopItemInfoAsync(string workshopId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ServerPanel/1.0");

        var resp = await client.PostAsync(
            "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["itemcount"] = "1",
                ["publishedfileids[0]"] = workshopId
            }));

        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync();
        var root = JsonSerializer.Deserialize<PublishedFileDetailsRoot>(json);
        var detail = root?.Response?.PublishedFileDetails?.FirstOrDefault();

        if (detail is null)
            return null;

        return new WorkshopItemInfo
        {
            Found = detail.Result == SteamWorkshopResultOk,
            Title = detail.Title,
            LastUpdatedUtc = detail.TimeUpdated > 0
                ? DateTimeOffset.FromUnixTimeSeconds(detail.TimeUpdated).UtcDateTime
                : null
        };
    }
}
