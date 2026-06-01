using Microsoft.Extensions.Logging;
using Moq;
using ServerPanel.Contracts;
using ServerPanel.Services;

namespace ServerPanel.Tests;

public class PlayerServiceTests
{
    private readonly Mock<ISshService> _ssh;
    private readonly PlayerService _sut;
    private string _capturedCommand = "";

    public PlayerServiceTests()
    {
        _ssh = new Mock<ISshService>();
        _ssh.Setup(s => s.ExecuteAsync(It.IsAny<string>()))
            .Callback<string>(cmd => _capturedCommand = cmd)
            .ReturnsAsync("");

        _sut = new PlayerService(_ssh.Object, Mock.Of<ILogger<PlayerService>>());
    }

    // ── Kick ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Kick_builds_correct_command()
    {
        await _sut.KickPlayerAsync("PlayerOne", "spam");

        Assert.Contains("css_kick", _capturedCommand);
        Assert.Contains("\\\"PlayerOne\\\"", _capturedCommand);
        Assert.Contains("\\\"spam\\\"", _capturedCommand);
    }

    [Fact]
    public async Task Kick_uses_default_reason_when_omitted()
    {
        await _sut.KickPlayerAsync("PlayerOne");

        Assert.Contains("Kicked by admin", _capturedCommand);
    }

    [Fact]
    public async Task Kick_sends_exactly_one_ssh_command()
    {
        await _sut.KickPlayerAsync("PlayerOne", "reason");

        _ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>()), Times.Once);
    }

    // ── Mute ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mute_builds_correct_command()
    {
        await _sut.MutePlayerAsync("PlayerOne", 45);

        Assert.Contains("css_mute", _capturedCommand);
        Assert.Contains("\\\"PlayerOne\\\"", _capturedCommand);
        Assert.Contains("45", _capturedCommand);
    }

    [Fact]
    public async Task Mute_includes_player_name_in_quotes()
    {
        await _sut.MutePlayerAsync("Some Player", 10);

        // El nombre debe ir entre comillas escapadas, no romper el comando
        Assert.Contains("\\\"Some Player\\\"", _capturedCommand);
    }

    [Fact]
    public async Task Mute_sends_exactly_one_ssh_command()
    {
        await _sut.MutePlayerAsync("PlayerOne", 30);

        _ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>()), Times.Once);
    }

    // ── Gag ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Gag_builds_correct_command()
    {
        await _sut.GagPlayerAsync("PlayerOne", 60);

        Assert.Contains("css_gag", _capturedCommand);
        Assert.Contains("\\\"PlayerOne\\\"", _capturedCommand);
        Assert.Contains("60", _capturedCommand);
    }

    [Fact]
    public async Task Gag_includes_player_name_in_quotes()
    {
        await _sut.GagPlayerAsync("Some Player", 20);

        Assert.Contains("\\\"Some Player\\\"", _capturedCommand);
    }

    // ── Ban ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Ban_builds_correct_command()
    {
        await _sut.BanPlayerAsync("PlayerOne", 120, "cheating");

        Assert.Contains("css_ban", _capturedCommand);
        Assert.Contains("\\\"PlayerOne\\\"", _capturedCommand);
        Assert.Contains("120", _capturedCommand);
        Assert.Contains("\\\"cheating\\\"", _capturedCommand);
    }

    [Fact]
    public async Task Ban_uses_default_reason_when_omitted()
    {
        await _sut.BanPlayerAsync("PlayerOne", 60);

        Assert.Contains("Banned by admin", _capturedCommand);
    }

    [Fact]
    public async Task Ban_sends_exactly_one_ssh_command()
    {
        await _sut.BanPlayerAsync("PlayerOne", 30, "reason");

        _ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>()), Times.Once);
    }

    // ── Unban ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unban_by_steamid64_queries_by_player_steamid()
    {
        await _sut.UnbanPlayerAsync("76561198000000000");

        _ssh.Verify(s => s.ExecuteAsync(It.Is<string>(cmd =>
            cmd.Contains("player_steamid") && cmd.Contains("76561198000000000"))), Times.Once);
    }

    [Fact]
    public async Task Unban_by_name_queries_by_player_name()
    {
        await _sut.UnbanPlayerAsync("PlayerOne");

        _ssh.Verify(s => s.ExecuteAsync(It.Is<string>(cmd =>
            cmd.Contains("player_name") && cmd.Contains("PlayerOne"))), Times.Once);
    }

    [Fact]
    public async Task Unban_sends_two_ssh_commands_delete_then_reload()
    {
        await _sut.UnbanPlayerAsync("PlayerOne");

        // 1: DELETE de sqlite, 2: css_plugins reload
        _ssh.Verify(s => s.ExecuteAsync(It.IsAny<string>()), Times.Exactly(2));
    }

    // ── Command structure ────────────────────────────────────────────────────

    [Theory]
    [InlineData("css_kick")]
    [InlineData("css_mute")]
    [InlineData("css_gag")]
    [InlineData("css_ban")]
    public async Task All_action_commands_run_inside_tmux_cs2_session(string cssCmd)
    {
        switch (cssCmd)
        {
            case "css_kick": await _sut.KickPlayerAsync("P", "r"); break;
            case "css_mute": await _sut.MutePlayerAsync("P", 1); break;
            case "css_gag":  await _sut.GagPlayerAsync("P", 1); break;
            case "css_ban":  await _sut.BanPlayerAsync("P", 1, "r"); break;
        }

        Assert.Contains("tmux send-keys -t cs2", _capturedCommand);
        Assert.Contains(cssCmd, _capturedCommand);
    }
}
