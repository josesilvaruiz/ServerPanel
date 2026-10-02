using System.Text.RegularExpressions;
using ServerPanel.Contracts;
using ServerPanel.Models;

namespace ServerPanel.Services;

/// <summary>
/// Otros juegos en el mismo clúster que CS2: igual que <see cref="Cs2ServerService"/>, por SSH con kubectl
/// (escalar el deployment a 1 / 0 réplicas, rollout restart).
/// </summary>
public partial class GameServerService : IGameServerService
{
    private readonly ISshService _ssh;
    private readonly ILogger<GameServerService> _logger;
    private readonly List<GameServerConfig> _games;

    public GameServerService(ISshService ssh, IConfiguration config, ILogger<GameServerService> logger)
    {
        _ssh = ssh;
        _logger = logger;
        _games = config.GetSection("OtherGames").Get<List<GameServerConfig>>() ?? [];
        foreach (var game in _games)
        {
            Validate(game);
        }
    }

    public IReadOnlyList<GameServerConfig> Games => _games;

    public async Task<bool> IsRunningAsync(GameServerConfig game)
    {
        try
        {
            var result = await _ssh.ExecuteAsync(
                $"kubectl get deployment {game.KubeDeployment} -n {game.KubeNamespace} -o jsonpath='{{.status.readyReplicas}}' 2>/dev/null || echo 0");
            var v = result.Trim();
            return !string.IsNullOrWhiteSpace(v) && v != "0" && v != "<no value>";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comprobando el estado de {Game}", game.Name);
            return false;
        }
    }

    public Task StartAsync(GameServerConfig game) =>
        Run(game, "arrancando", $"kubectl scale deployment {game.KubeDeployment} -n {game.KubeNamespace} --replicas=1");

    public Task StopAsync(GameServerConfig game) =>
        Run(game, "parando", $"kubectl scale deployment {game.KubeDeployment} -n {game.KubeNamespace} --replicas=0");

    public Task RestartAsync(GameServerConfig game) =>
        Run(game, "reiniciando", $"kubectl rollout restart deployment/{game.KubeDeployment} -n {game.KubeNamespace}");

    public Task<string> GetLogsAsync(GameServerConfig game, int tail = 60) =>
        _ssh.ExecuteAsync($"kubectl logs -n {game.KubeNamespace} deployment/{game.KubeDeployment} --tail={Math.Clamp(tail, 1, 500)} 2>/dev/null");

    private async Task Run(GameServerConfig game, string what, string command)
    {
        try
        {
            _logger.LogInformation("{Game}: {What} ({Dep} en {Ns})", game.Name, what, game.KubeDeployment, game.KubeNamespace);
            await _ssh.ExecuteAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Game}: error {What}", game.Name, what);
            throw;
        }
    }

    /// <summary>
    /// Namespace y deployment acaban dentro de un comando de shell: solo se aceptan nombres válidos de
    /// Kubernetes (minúsculas, dígitos y guiones), así una configuración mal escrita no puede inyectar nada.
    /// </summary>
    private static void Validate(GameServerConfig game)
    {
        if (!KubeName().IsMatch(game.KubeNamespace) || !KubeName().IsMatch(game.KubeDeployment))
        {
            throw new InvalidOperationException(
                $"OtherGames: '{game.Name}' tiene un KubeNamespace o KubeDeployment no válido " +
                "(solo minúsculas, dígitos y guiones).");
        }
    }

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")]
    private static partial Regex KubeName();
}
