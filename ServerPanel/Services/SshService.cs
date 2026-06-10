using System.Text.RegularExpressions;
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

    private static SshClient BuildSshClient(SshSettings s)
    {
        var client = new SshClient(s.Host, s.User, s.Password)
        {
            ConnectionInfo = { Timeout = TimeSpan.FromSeconds(10) }
        };
        return client;
    }

    private static Renci.SshNet.SftpClient BuildSftpClient(SshSettings s)
    {
        var client = new Renci.SshNet.SftpClient(s.Host, s.User, s.Password)
        {
            ConnectionInfo = { Timeout = TimeSpan.FromSeconds(10) }
        };
        return client;
    }

    public IAsyncDisposable OpenTunnel(uint localPort, string remoteHost, uint remotePort)
    {
        if (_settings == null)
            throw new InvalidOperationException("La configuración SSH no existe.");

        var client = BuildSshClient(_settings);
        client.Connect();

        var port = new Renci.SshNet.ForwardedPortLocal("127.0.0.1", localPort, remoteHost, remotePort);
        client.AddForwardedPort(port);
        port.Start();

        _logger.LogInformation("SSH Túnel abierto: localhost:{Local} → {Remote}:{RemotePort}", localPort, remoteHost, remotePort);

        return new SshTunnel(client, port, _logger);
    }

    private sealed class SshTunnel : IAsyncDisposable
    {
        private readonly SshClient _client;
        private readonly Renci.SshNet.ForwardedPortLocal _port;
        private readonly ILogger _logger;

        public SshTunnel(SshClient client, Renci.SshNet.ForwardedPortLocal port, ILogger logger)
        {
            _client = client;
            _port   = port;
            _logger = logger;
        }

        public ValueTask DisposeAsync()
        {
            _port.Stop();
            _client.Disconnect();
            _client.Dispose();
            _port.Dispose();
            _logger.LogInformation("SSH Túnel cerrado");
            return ValueTask.CompletedTask;
        }
    }

    public async Task UploadFileAsync(string remotePath, Stream content)
    {
        if (_settings == null) throw new InvalidOperationException("La configuración SSH no existe.");
        // Buffer first — the browser stream can't be read from a background thread
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms);
        ms.Position = 0;
        await Task.Run(() =>
        {
            using var client = BuildSftpClient(_settings);
            client.Connect();
            client.UploadFile(ms, remotePath, true);
            client.Disconnect();
        });
    }

    public async Task<Stream> DownloadFileAsync(string remotePath)
    {
        if (_settings == null) throw new InvalidOperationException("La configuración SSH no existe.");
        return await Task.Run(() =>
        {
            using var client = BuildSftpClient(_settings);
            client.Connect();
            var ms = new System.IO.MemoryStream();
            client.DownloadFile(remotePath, ms);
            client.Disconnect();
            ms.Position = 0;
            return (Stream)ms;
        });
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
                using var client = BuildSshClient(_settings);

                client.Connect();

                if (!client.IsConnected)
                {
                    throw new InvalidOperationException(
                        $"No se pudo conectar al servidor SSH {_settings.Host}");
                }

                _logger.LogInformation(
                    "SSH Conectado a {Host}",
                    _settings.Host);

                var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = TimeSpan.FromMinutes(10);
                var stdout = CleanOutput(cmd.Execute() ?? "");
                var stderr = CleanOutput(cmd.Error    ?? "");

                _logger.LogInformation("SSH ExitStatus: {ExitStatus}", cmd.ExitStatus);

                if (!string.IsNullOrWhiteSpace(stderr))
                    _logger.LogWarning("SSH STDERR: {Error}", stderr);

                client.Disconnect();

                if (string.IsNullOrEmpty(stderr)) return stdout;
                if (string.IsNullOrEmpty(stdout)) return stderr;
                return stdout.TrimEnd('\n') + "\n" + stderr;
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

    private static readonly Regex _ansi = new(
        @"\x1B(?:[@-Z\\-_]|\[[0-?]*[ -/]*[@-~])",
        RegexOptions.Compiled);

    private static string CleanOutput(string raw)
    {
        var text = _ansi.Replace(raw, "");
        // Normalize CRLF before simulating CR overwrites
        text = text.Replace("\r\n", "\n");
        var sb = new System.Text.StringBuilder();
        foreach (var line in text.Split('\n'))
        {
            if (sb.Length > 0) sb.Append('\n');
            // Simulate terminal CR overwrite (bare \r, not \r\n)
            var parts = line.Split('\r');
            sb.Append(parts[^1]);
        }
        return sb.ToString().Trim();
    }
}