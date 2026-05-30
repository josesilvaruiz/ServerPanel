using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class Cs2ServerService : ICs2ServerService
{
    private readonly ISshService _ssh;
    private readonly ILogger<Cs2ServerService> _logger;

    public Cs2ServerService(
        ISshService ssh,
        ILogger<Cs2ServerService> logger)
    {
        _ssh = ssh;
        _logger = logger;
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
            const string command = "pkill -f cs2";

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
}