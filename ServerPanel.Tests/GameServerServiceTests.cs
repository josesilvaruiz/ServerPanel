using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServerPanel.Contracts;
using ServerPanel.Services;

namespace ServerPanel.Tests;

public class GameServerServiceTests
{
    static IConfiguration Config(string ns = "valheim", string dep = "valheim-server") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["OtherGames:0:Name"] = "Valheim",
            ["OtherGames:0:Host"] = "1.2.3.4",
            ["OtherGames:0:Port"] = "2456",
            ["OtherGames:0:KubeNamespace"] = ns,
            ["OtherGames:0:KubeDeployment"] = dep,
        }).Build();

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
}
