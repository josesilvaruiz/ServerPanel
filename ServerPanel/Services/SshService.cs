using Renci.SshNet;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

public class SshService : ISshService
{
    private readonly ILogger<SshService> _logger;
    private readonly SshSettings? _settings;

    public SshService(
        IConfiguration configuration,
        ILogger<SshService> logger)
    {
        _logger = logger;

        _settings = configuration
            .GetSection("Ssh")
            .Get<SshSettings>();

        if (_settings == null)
        {
            _logger.LogError(
                "No existe la sección 'Ssh' en appsettings.json");
        }
    }

    public async Task<string> ExecuteAsync(string command)
    {
        if (_settings == null)
        {
            throw new InvalidOperationException(
                "La configuración SSH no existe. Añade la sección 'Ssh' a appsettings.json.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Host))
        {
            throw new InvalidOperationException(
                "Ssh:Host no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_settings.User))
        {
            throw new InvalidOperationException(
                "Ssh:User no está configurado.");
        }

        if (string.IsNullOrWhiteSpace(_settings.Password))
        {
            throw new InvalidOperationException(
                "Ssh:Password no está configurado.");
        }

        try
        {
            _logger.LogInformation(
                "SSH Ejecutando comando: {Command}",
                command);

            return await Task.Run(() =>
            {
                using var client = new SshClient(
                    _settings.Host,
                    _settings.User,
                    _settings.Password);

                client.Connect();

                if (!client.IsConnected)
                {
                    throw new InvalidOperationException(
                        $"No se pudo conectar al servidor SSH {_settings.Host}");
                }

                _logger.LogInformation(
                    "SSH Conectado a {Host}",
                    _settings.Host);

                var result = client.RunCommand(command);

                _logger.LogInformation(
                    "SSH ExitStatus: {ExitStatus}",
                    result.ExitStatus);

                if (!string.IsNullOrWhiteSpace(result.Error))
                {
                    _logger.LogWarning(
                        "SSH STDERR: {Error}",
                        result.Error);
                }

                client.Disconnect();

                return result.Result ?? string.Empty;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error ejecutando comando SSH");

            throw;
        }
    }
}