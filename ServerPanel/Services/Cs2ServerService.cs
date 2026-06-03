using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class Cs2ServerService : ICs2ServerService
{
    private readonly ISshService _ssh;
    private readonly ILogger<Cs2ServerService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public Cs2ServerService(
        ISshService ssh,
        ILogger<Cs2ServerService> logger,
        IHttpClientFactory httpClientFactory)
    {
        _ssh = ssh;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<bool> IsRunningAsync()
    {
        try
        {
            const string command = "pgrep -f cs2";

            _logger.LogInformation(
                "Comprobando estado del servidor CS2");

            var result = await _ssh.ExecuteAsync(command);

            _logger.LogInformation(
                "Resultado recibido: '{Result}'",
                result);

            var running = !string.IsNullOrWhiteSpace(result);

            _logger.LogInformation(
                "Estado calculado: {Running}",
                running);

            return running;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error comprobando estado del servidor CS2");

            return false;
        }
    }

    public async Task StartAsync()
    {
        try
        {
            const string command =
                "sudo -u steam tmux new-session -d -s cs2 'cd /home/steam/cs2 && ./start.sh'";

            _logger.LogInformation(
                "Iniciando servidor CS2");

            var result = await _ssh.ExecuteAsync(command);

            _logger.LogInformation(
                "Resultado Start: '{Result}'",
                result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error iniciando servidor CS2");

            throw;
        }
    }

    public async Task StopAsync()
    {
        try
        {
            const string command = "sudo -u steam tmux kill-session -t cs2";

            _logger.LogInformation(
                "Deteniendo servidor CS2");

            var result = await _ssh.ExecuteAsync(command);

            _logger.LogInformation(
                "Resultado Stop: '{Result}'",
                result);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error deteniendo servidor CS2");

            throw;
        }
    }

    public async Task RestartAsync()
    {
        try
        {
            _logger.LogInformation(
                "Reiniciando servidor CS2");

            await StopAsync();

            await Task.Delay(5000);

            await StartAsync();

            _logger.LogInformation(
                "Reinicio completado");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error reiniciando servidor CS2");

            throw;
        }
    }

    public async Task<string> ExecuteConsoleCommandAsync(string command)
    {
        // Ejecuta el comando en la consola CS2 y devuelve la salida
        var fullCommand = $"su - steam -c \"tmux send-keys -t cs2 '{command}' Enter; sleep 1; tmux capture-pane -t cs2 -p\"";
        _logger.LogInformation("Ejecutando comando consola: {Command}", command);
        var output = await _ssh.ExecuteAsync(fullCommand);
        return output;
    }

    public async Task<UpdateResult> UpdateAsync()
    {
        try
        {
            const string manifest =
                "/home/steam/cs2/steamapps/appmanifest_730.acf";

            var localBuild =
                (await _ssh.ExecuteAsync(
                    $"grep buildid {manifest} | head -1 | grep -o '[0-9]*'"))
                .Trim();

            var remoteBuild =
                (await _ssh.ExecuteAsync(
                    "su - steam -c \"/home/steam/steamcmd/steamcmd.sh +login anonymous +app_info_update 1 +app_info_print 730 +quit\" | grep -m1 buildid | grep -o '[0-9]*'"))
                .Trim();

            if (string.IsNullOrWhiteSpace(localBuild))
            {
                return new UpdateResult
                {
                    Output = "No se pudo obtener la build local."
                };
            }

            if (string.IsNullOrWhiteSpace(remoteBuild))
            {
                return new UpdateResult
                {
                    Output = "No se pudo obtener la build remota."
                };
            }

            if (localBuild == remoteBuild)
            {
                return new UpdateResult
                {
                    AlreadyUpdated = true,
                    LocalBuild = localBuild,
                    RemoteBuild = remoteBuild,
                    Output =
                        $"Servidor actualizado. Build {localBuild}"
                };
            }

            var pid =
                (await _ssh.ExecuteAsync(
                    "pgrep -f '/home/steam/cs2/game/bin/linuxsteamrt64/cs2'"))
                .Trim();

            if (!string.IsNullOrWhiteSpace(pid))
            {
                await _ssh.ExecuteAsync(
                    $"kill -9 {pid}");

                await Task.Delay(5000);
            }

            await _ssh.ExecuteAsync(
                "su - steam -c 'tmux kill-session -t cs2' || true");

            await Task.Delay(2000);

            await _ssh.ExecuteAsync(
                "su - steam -c '/home/steam/steamcmd/steamcmd.sh +force_install_dir /home/steam/cs2 +login anonymous +app_update 730 validate +quit'");

            await _ssh.ExecuteAsync(
                "su - steam -c \"tmux new-session -d -s cs2 'cd /home/steam/cs2 && ./start.sh'\"");

            return new UpdateResult
            {
                Updated = true,
                LocalBuild = localBuild,
                RemoteBuild = remoteBuild,
                Output =
                    $"Actualizado de {localBuild} a {remoteBuild}"
            };
        }
        catch (Exception ex)
        {
            return new UpdateResult
            {
                Output = ex.Message
            };
        }
    }

    public async Task<List<WorkshopMap>> GetWorkshopMapsAsync(string collectionId)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ServerPanel/1.0");

        // Step 1: get collection children
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

        if (children is null || children.Count == 0)
            return [];

        // Step 2: get details (name + preview image) for each item
        var fileParams = new Dictionary<string, string>
        {
            ["itemcount"] = children.Count.ToString()
        };
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

    const string SimpleAdminConfigPath =
        "/home/steam/cs2/game/csgo/addons/counterstrikesharp/configs/plugins/CS2-SimpleAdmin/CS2-SimpleAdmin.json";

    public async Task UpdateSimpleAdminWorkshopMapsAsync(IEnumerable<WorkshopMap> maps)
    {
        var mapList = maps.ToList();
        if (mapList.Count == 0)
            throw new InvalidOperationException("La lista de mapas está vacía");

        // Serialize maps as JSON and pass as base64 argument to Python
        var mapsJson = System.Text.Json.JsonSerializer.Serialize(
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

        var result = await _ssh.ExecuteAsync($"python3 -c \"{py}\"");

        _logger.LogInformation("Workshop update result: {Result}", result);

        if (!result.Contains("ok"))
            throw new InvalidOperationException($"Error: {result.Trim()}");
    }

    // ── Steam API DTOs ────────────────────────────────────────────────────────

    private sealed class CollectionDetailsRoot
    {
        [JsonPropertyName("response")]
        public CollectionResponse? Response { get; set; }
    }

    private sealed class CollectionResponse
    {
        [JsonPropertyName("collectiondetails")]
        public List<CollectionDetail>? CollectionDetails { get; set; }
    }

    private sealed class CollectionDetail
    {
        [JsonPropertyName("children")]
        public List<CollectionChild>? Children { get; set; }
    }

    private sealed class CollectionChild
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } = "";
    }

    private sealed class PublishedFileDetailsRoot
    {
        [JsonPropertyName("response")]
        public PublishedFileResponse? Response { get; set; }
    }

    private sealed class PublishedFileResponse
    {
        [JsonPropertyName("publishedfiledetails")]
        public List<PublishedFileDetail>? PublishedFileDetails { get; set; }
    }

    private sealed class PublishedFileDetail
    {
        [JsonPropertyName("publishedfileid")]
        public string PublishedFileId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("preview_url")]
        public string PreviewUrl { get; set; } = "";
    }
}