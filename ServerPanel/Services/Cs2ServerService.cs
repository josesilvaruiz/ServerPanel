using ServerPanel.Contracts;

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
}