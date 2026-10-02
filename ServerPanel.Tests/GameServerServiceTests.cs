using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServerPanel.Contracts;
using ServerPanel.Services;

namespace ServerPanel.Tests;

public class GameServerServiceTests
{
    static IConfiguration Config(string ns = "valheim", string dep = "valheim-server",
        string? backupSource = null, string? backupDir = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OtherGames:0:Name"] = "Valheim",
            ["OtherGames:0:Host"] = "1.2.3.4",
            ["OtherGames:0:Port"] = "2456",
            ["OtherGames:0:KubeNamespace"] = ns,
            ["OtherGames:0:KubeDeployment"] = dep,
            ["OtherGames:0:BackupSource"] = backupSource,
            ["OtherGames:0:BackupDir"] = backupDir,
        }).Build();

    const string Src = "/var/lib/rancher/k3s/storage/valheim-config";
    const string Dst = "/root/valheim-safe";

    static (GameServerService Service, Mock<ISshService> Ssh, List<string> Commands) CreateWithBackup(string waitResult)
    {
        var commands = new List<string>();
        var ssh = new Mock<ISshService>();
        ssh.Setup(x => x.ExecuteAsync(It.IsAny<string>())).ReturnsAsync((string cmd) =>
        {
            commands.Add(cmd);
            if (cmd.Contains("seq 1 90")) return waitResult;
            if (cmd.Contains("BACKUP_OK")) return $"BACKUP_OK:{Dst}/valheim-server-20261002-211000.tar.gz";
            return "";
        });
        return (new GameServerService(ssh.Object, Config(backupSource: Src, backupDir: Dst),
            NullLogger<GameServerService>.Instance), ssh, commands);
    }

    static (GameServerService Service, Mock<ISshService> Ssh) Create(string readyReplicas = "1")
    {
        var ssh = new Mock<ISshService>();
        ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>())).ReturnsAsync(readyReplicas);
        return (new GameServerService(ssh.Object, Config(), NullLogger<GameServerService>.Instance), ssh);
    }

    [Fact]
    public void ReadsTheGamesFromConfiguration()
    {
        var (service, _) = Create();

        var game = Assert.Single(service.Games);
        Assert.Equal("Valheim", game.Name);
        Assert.Equal(2456, game.Port);
    }

    [Fact]
    public async Task StartScalesTheDeploymentToOneReplica()
    {
        var (service, ssh) = Create();

        await service.StartAsync(service.Games[0]);

        ssh.Verify(s => s.ExecuteAsync("kubectl scale deployment valheim-server -n valheim --replicas=1"));
    }

    [Fact]
    public async Task StopScalesTheDeploymentToZero()
    {
        var (service, ssh) = Create();

        await service.StopAsync(service.Games[0]);

        ssh.Verify(s => s.ExecuteAsync("kubectl scale deployment valheim-server -n valheim --replicas=0"));
    }

    [Fact]
    public async Task RestartRollsTheDeployment()
    {
        var (service, ssh) = Create();

        await service.RestartAsync(service.Games[0]);

        ssh.Verify(s => s.ExecuteAsync("kubectl rollout restart deployment/valheim-server -n valheim"));
    }

    [Theory]
    [InlineData("1", true)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("<no value>", false)]
    public async Task IsRunningFollowsTheReadyReplicas(string readyReplicas, bool expected)
    {
        var (service, _) = Create(readyReplicas);

        Assert.Equal(expected, await service.IsRunningAsync(service.Games[0]));
    }

    [Theory]
    [InlineData("valheim; rm -rf /", "valheim-server")]
    [InlineData("valheim", "valheim-server && reboot")]
    [InlineData("Valheim", "valheim-server")]
    public void RejectsNamesThatAreNotKubernetesNames(string ns, string dep)
    {
        var ssh = new Mock<ISshService>();

        Assert.Throws<InvalidOperationException>(() =>
            new GameServerService(ssh.Object, Config(ns, dep), NullLogger<GameServerService>.Instance));
    }

    [Fact]
    public async Task StopWithBackupScalesDownThenWaitsThenBacksUpInThatOrder()
    {
        var (service, _, commands) = CreateWithBackup("GONE");

        var summary = await service.StopAsync(service.Games[0]);

        Assert.Equal(3, commands.Count);
        Assert.Equal("kubectl scale deployment valheim-server -n valheim --replicas=0", commands[0]);
        Assert.Contains("seq 1 90", commands[1]);
        Assert.Contains($"tar -czf $F -C {Src} .", commands[2]);
        Assert.Contains("gzip -t", commands[2]);
        Assert.Contains("valheim-server-20261002-211000.tar.gz", summary);
    }

    [Fact]
    public async Task BackupOnlyReadsTheWorldAndNeverDeletesAnything()
    {
        var (service, _, commands) = CreateWithBackup("GONE");

        await service.StopAsync(service.Games[0]);

        var backup = commands[2];
        Assert.DoesNotContain("rm ", backup);
        Assert.DoesNotContain("mv ", backup);
        Assert.DoesNotContain("--remove-files", backup);
        Assert.StartsWith($"mkdir -p {Dst}", backup);
    }

    [Fact]
    public async Task StopDoesNotBackUpWhenThePodNeverFinishesShuttingDown()
    {
        var (service, _, commands) = CreateWithBackup("TIMEOUT");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.StopAsync(service.Games[0]));

        Assert.DoesNotContain(commands, c => c.Contains("tar -czf"));
    }

    [Fact]
    public async Task StopWithoutBackupConfiguredOnlyScalesDown()
    {
        var (service, ssh) = Create();

        await service.StopAsync(service.Games[0]);

        ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("/data/valheim", null)]                          // falta el destino
    [InlineData(null, "/root/valheim-safe")]                     // falta el origen
    [InlineData("/data/valheim", "/data/valheim/copias")]        // la copia dentro del origen
    [InlineData("/data/valheim", "/data/valheim")]               // mismo sitio
    [InlineData("/data/valheim; rm -rf /", "/root/valheim-safe")]
    [InlineData("/data/valheim", "/root/$(reboot)")]
    [InlineData("/data/../etc", "/root/valheim-safe")]
    [InlineData("relativa/valheim", "/root/valheim-safe")]
    public void RejectsUnsafeOrInconsistentBackupPaths(string? source, string? dir)
    {
        var ssh = new Mock<ISshService>();

        Assert.Throws<InvalidOperationException>(() =>
            new GameServerService(ssh.Object, Config(backupSource: source, backupDir: dir),
                NullLogger<GameServerService>.Instance));
    }
}
