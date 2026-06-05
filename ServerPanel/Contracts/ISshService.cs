namespace ServerPanel.Contracts
{
    public interface ISshService
    {
        Task<string> ExecuteAsync(string command);

        /// <summary>
        /// Abre un túnel local localPort → remoteHost:remotePort.
        /// Dispose del resultado cierra el túnel y desconecta.
        /// </summary>
        IAsyncDisposable OpenTunnel(uint localPort, string remoteHost, uint remotePort);
    }
}
