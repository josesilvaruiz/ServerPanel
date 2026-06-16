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
        ILogger<Cs2ServerService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _ssh           = ssh;
        _rcon          = rcon;
        _activeServer  = activeServer;
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
            _logger.LogInformation("Actualizando CS2 via SteamCMD en el pod");

            var updateResult = await _ssh.ExecuteAsync(
                Kube($"exec -n {Ns} deployment/{Dep} -- bash -c " +
                     "\"/home/steam/steamcmd/steamcmd.sh +force_install_dir /home/steam/cs2 " +
                     "+login anonymous +app_update 730 validate +quit\" 2>/dev/null || echo 'steamcmd no disponible'"));

            await _ssh.ExecuteAsync(Kube($"rollout restart deployment/{Dep} -n {Ns}"));

            return new UpdateResult
            {
                Updated = true,
                Output = $"Actualización completada. Reiniciando servidor...\n{updateResult}"
            };
        }
        catch (Exception ex)
        {
            return new UpdateResult { Output = ex.Message };
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
        $"{ContainerCssPath}/configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json";

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
        var result = await _ssh.ExecuteAsync(KubeExec($"echo {pyB64} | base64 -d | python3"));
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
        [JsonPropertyName("title")]           public string Title           { get; set; } = "";
        [JsonPropertyName("preview_url")]     public string PreviewUrl      { get; set; } = "";
    }
}
