using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MinecraftConsoleRemoteTests
{
    [Theory]
    [InlineData("list", "list")]
    [InlineData("  list  ", "list")]
    [InlineData("/say hi", "say hi")]
    [InlineData("/list", "list")]
    public void Normalize_strips_one_leading_slash(string raw, string expected)
    {
        Assert.True(MinecraftConsoleRemote.TryNormalizeCommand(raw, out var command, out var error));
        Assert.Null(error);
        Assert.Equal(expected, command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(" / ")]
    public void Normalize_rejects_empty(string? raw)
    {
        Assert.False(MinecraftConsoleRemote.TryNormalizeCommand(raw, out _, out var error));
        Assert.Equal(MinecraftConsoleRemote.EmptyCommandHint, error);
    }

    [Fact]
    public void Normalize_rejects_too_long()
    {
        var raw = new string('a', MinecraftConsoleRemote.MaxCommandChars + 1);
        Assert.False(MinecraftConsoleRemote.TryNormalizeCommand(raw, out _, out var error));
        Assert.Equal(MinecraftConsoleRemote.CommandTooLongHint, error);
    }

    [Fact]
    public void Rcon_remote_command_uses_localhost_secret_and_base64_payload()
    {
        Assert.True(MinecraftConsoleRemote.TryBuildRconCommand("/say hello world", out var remote, out var error));
        Assert.Null(error);
        Assert.StartsWith("sudo python3 -c ", remote, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", remote, StringComparison.Ordinal);
        Assert.Contains("25575", remote, StringComparison.Ordinal);
        Assert.Contains("/etc/mcmgr/rcon.secret", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("say hello world", remote, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("say hello world")), remote, StringComparison.Ordinal);
    }

    [Fact]
    public void Logs_command_is_journalctl_cat_for_minecraft_unit()
    {
        var cmd = MinecraftConsoleRemote.LogsCommand(200);
        Assert.Equal("sudo journalctl -u minecraft -n 200 --no-pager -o cat", cmd);
        Assert.DoesNotContain("25575", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_line_count_is_clamped()
    {
        Assert.Equal(50, MinecraftConsoleRemote.ClampLogLines(1));
        Assert.Equal(500, MinecraftConsoleRemote.ClampLogLines(9999));
        Assert.Equal(200, MinecraftConsoleRemote.ClampLogLines(200));
    }

    [Fact]
    public void Send_requires_joinable_minecraft_and_a_command()
    {
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: false, busy: false, "list"));
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: true, "list"));
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: false, ""));
        Assert.True(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: false, "/list"));
        Assert.Equal(
            MinecraftConsoleRemote.VmStoppedHint,
            MinecraftConsoleRemote.SendDisabledReason(vm1Running: false, minecraftJoinable: false, busy: false, "list"));
        Assert.Equal(
            MinecraftConsoleRemote.MinecraftStoppedHint,
            MinecraftConsoleRemote.SendDisabledReason(vm1Running: true, minecraftJoinable: false, busy: false, "list"));
    }

    [Fact]
    public void Refresh_requires_vm1_running()
    {
        Assert.False(MinecraftConsoleRemote.CanRefresh(vm1Running: false, busy: false));
        Assert.True(MinecraftConsoleRemote.CanRefresh(vm1Running: true, busy: false));
        Assert.False(MinecraftConsoleRemote.CanRefresh(vm1Running: true, busy: true));
    }

    [Fact]
    public void Transcript_line_prefixes_the_command()
    {
        var line = MinecraftConsoleRemote.FormatTranscriptLine("list", "There are 0 of a max of 20 players online:");
        Assert.StartsWith("> list", line, StringComparison.Ordinal);
        Assert.Contains("There are 0", line, StringComparison.Ordinal);
    }

    [Fact]
    public void Unreachable_rcon_uses_novice_copy()
    {
        var run = SshExecResult.Fail("could not reach Minecraft RCON on localhost", exitStatus: 5);
        Assert.Equal(MinecraftConsoleRemote.RconUnreachableHint, MinecraftConsoleRemote.OperatorHintFromRcon(run));
    }
}
