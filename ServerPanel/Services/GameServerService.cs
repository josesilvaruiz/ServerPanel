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

    public async Task<string> StopAsync(GameServerConfig game)
    {
        await Run(game, "parando", $"kubectl scale deployment {game.KubeDeployment} -n {game.KubeNamespace} --replicas=0");

        if (!HasBackup(game))
            return "Servidor detenido.";

        // La copia se hace con el servidor ya apagado: solo entonces el mundo está completo en disco.
        var wait = (await _ssh.ExecuteAsync(WaitForPodsGoneCommand(game))).Trim();
        if (!wait.EndsWith("GONE"))
            throw new InvalidOperationException(
                $"{game.Name}: la parada se ha pedido, pero el servidor no terminó de apagarse a tiempo. " +
                "No se hizo la copia de seguridad para no copiar un mundo a medio guardar; revisa el estado.");

        var result = (await _ssh.ExecuteAsync(BackupCommand(game))).Trim();
        const string ok = "BACKUP_OK:";
        if (!result.Contains(ok))
            throw new InvalidOperationException(
                $"{game.Name}: servidor detenido, pero la copia de seguridad ha fallado: {result}");

        var file = result[(result.LastIndexOf(ok, StringComparison.Ordinal) + ok.Length)..].Trim();
        _logger.LogInformation("{Game}: copia de seguridad en {File}", game.Name, file);
        return $"Servidor detenido. Copia de seguridad: {file}";
    }

    public Task RestartAsync(GameServerConfig game) =>
        Run(game, "reiniciando", $"kubectl rollout restart deployment/{game.KubeDeployment} -n {game.KubeNamespace}");

    public Task<string> GetLogsAsync(GameServerConfig game, int tail = 60) =>
        _ssh.ExecuteAsync($"kubectl logs -n {game.KubeNamespace} deployment/{game.KubeDeployment} --tail={Math.Clamp(tail, 1, 500)} 2>/dev/null");

    private static bool HasBackup(GameServerConfig game) =>
        !string.IsNullOrEmpty(game.BackupSource) && !string.IsNullOrEmpty(game.BackupDir);

    /// <summary>Espera (hasta ~3 min) a que no quede ningún pod del deployment; imprime GONE o TIMEOUT.</summary>
    private static string WaitForPodsGoneCommand(GameServerConfig game) =>
        $"SEL=$(kubectl get deployment {game.KubeDeployment} -n {game.KubeNamespace} " +
        "-o go-template='{{range $k,$v := .spec.selector.matchLabels}}{{$k}}={{$v}},{{end}}' | sed 's/,$//'); " +
        "R=TIMEOUT; for i in $(seq 1 90); do " +
        $"if [ -n \"$SEL\" ] && [ -z \"$(kubectl get pods -n {game.KubeNamespace} -l \"$SEL\" -o name 2>/dev/null)\" ]; then R=GONE; break; fi; " +
        "sleep 2; done; echo $R";

    /// <summary>Comprime BackupSource (solo lectura) en BackupDir y comprueba que el archivo es válido.</summary>
    private static string BackupCommand(GameServerConfig game) =>
        $"mkdir -p {game.BackupDir} && chmod 700 {game.BackupDir} && " +
        $"F={game.BackupDir}/{game.KubeDeployment}-$(date +%Y%m%d-%H%M%S).tar.gz && " +
        $"tar -czf $F -C {game.BackupSource} . && gzip -t $F && echo BACKUP_OK:$F";

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

        // BackupSource y BackupDir van dentro de un comando de shell: solo rutas absolutas simples.
        // Las dos o ninguna, y la copia nunca puede escribirse dentro del origen.
        var hasSource = !string.IsNullOrEmpty(game.BackupSource);
        var hasDir = !string.IsNullOrEmpty(game.BackupDir);
        if (hasSource != hasDir ||
            (hasSource && (!SafePath().IsMatch(game.BackupSource) || game.BackupSource.Contains("..") ||
                           !SafePath().IsMatch(game.BackupDir) || game.BackupDir.Contains("..") ||
                           game.BackupDir.TrimEnd('/') == game.BackupSource.TrimEnd('/') ||
                           game.BackupDir.TrimEnd('/').StartsWith(game.BackupSource.TrimEnd('/') + "/"))))
        {
            throw new InvalidOperationException(
                $"OtherGames: '{game.Name}' tiene un BackupSource/BackupDir no válido (rutas absolutas simples, " +
                "las dos juntas, y BackupDir fuera de BackupSource).");
        }
    }

    [GeneratedRegex("^[a-z0-9]([-a-z0-9]{0,61}[a-z0-9])?$")]
    private static partial Regex KubeName();

    [GeneratedRegex("^/[A-Za-z0-9_./-]+$")]
    private static partial Regex SafePath();
}
