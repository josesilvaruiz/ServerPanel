using ServerPanel.Services;

namespace ServerPanel.Contracts
{
    public interface ISshService
    {
        Task<string> ExecuteAsync(string command);

        /// <summary>Downloads a remote file into a MemoryStream via SFTP.</summary>
        Task<Stream> DownloadFileAsync(string remotePath);

        /// <summary>Uploads a stream to a remote path via SFTP.</summary>
        Task UploadFileAsync(string remotePath, Stream content);

        /// <summary>
        /// Abre un túnel local localPort → remoteHost:remotePort.
        /// Dispose del resultado cierra el túnel y desconecta.
        /// </summary>
        IAsyncDisposable OpenTunnel(uint localPort, string remoteHost, uint remotePort);

        /// <summary>Abre un shell interactivo con PTY en el servidor remoto.</summary>
        SshShellSession OpenShell(uint cols, uint rows);

        /// <summary>Ejecuta un comando SSH y devuelve su output línea a línea en tiempo real.</summary>
        IAsyncEnumerable<string> StreamCommandAsync(string command, CancellationToken ct);
    }
}
