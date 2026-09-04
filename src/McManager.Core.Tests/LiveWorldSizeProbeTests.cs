using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class LiveWorldSizeProbeTests
{
    [Fact]
    public void Command_is_sudo_du_on_the_normalized_world_path()
    {
        Assert.True(
            LiveWorldSizeProbe.TryCreateCommand("/opt/mcmgr/server/world/", out var command, out var error),
            error);
        Assert.Contains("sudo bash -c", command, StringComparison.Ordinal);
        Assert.Contains("HOME=\"${HOME:-/home/ubuntu}\"", command, StringComparison.Ordinal);
        Assert.Contains("du -sb --", command, StringComparison.Ordinal);
        Assert.Contains("/opt/mcmgr/server/world", command, StringComparison.Ordinal);
        Assert.Contains(LiveWorldSizeProbe.MissingMarker, command, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", command, StringComparison.Ordinal);
        Assert.DoesNotContain("zip", command, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("friend", command, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/tmp/world")]
    [InlineData("/opt/mcmgr/server/mods")]
    [InlineData("/opt/mcmgr/server/world;rm -rf /")]
    public void Rejects_unsafe_world_paths(string? worldPath)
    {
        Assert.False(LiveWorldSizeProbe.TryCreateCommand(worldPath, out var command, out var error));
        Assert.True(string.IsNullOrEmpty(command));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Parses_gnu_du_bytes_line()
    {
        Assert.True(
            LiveWorldSizeProbe.TryParse("1234567890\t/opt/mcmgr/server/world\n", out var bytes, out var error),
            error);
        Assert.Equal(1234567890L, bytes);
        Assert.Equal("1.1 GB", LiveWorldSizeProbe.FormatGb(bytes));
    }

    [Fact]
    public void Parses_du_when_a_file_vanished_warning_is_present()
    {
        var stdout =
            """
            du: cannot access '/opt/mcmgr/server/world/session.lock': No such file or directory
            4096	/opt/mcmgr/server/world
            """;
        Assert.True(LiveWorldSizeProbe.TryParse(stdout, out var bytes, out var error), error);
        Assert.Equal(4096L, bytes);
    }

    [Fact]
    public void Missing_folder_marker_is_an_error()
    {
        Assert.False(
            LiveWorldSizeProbe.TryParse(LiveWorldSizeProbe.MissingMarker, out _, out var error));
        Assert.Contains("not on the game VM", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Garbage_stdout_is_an_error()
    {
        Assert.False(LiveWorldSizeProbe.TryParse("no numbers here", out _, out var error));
        Assert.Contains("parse", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Display_is_dash_until_a_running_vm_has_bytes()
    {
        Assert.Equal("…", LiveWorldSizeProbe.FormatDisplay(1, vmRunning: true, measuring: true));
        Assert.Equal("—", LiveWorldSizeProbe.FormatDisplay(1_073_741_824, vmRunning: false, measuring: false));
        Assert.Equal("—", LiveWorldSizeProbe.FormatDisplay(null, vmRunning: true, measuring: false));
        Assert.Equal("1.0 GB", LiveWorldSizeProbe.FormatDisplay(1_073_741_824, vmRunning: true, measuring: false));
    }

    [Fact]
    public void Refresh_is_gated_for_two_minutes_after_an_attempt()
    {
        var t0 = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromMinutes(2), LiveWorldSizeProbe.RefreshCooldown);
        Assert.True(LiveWorldSizeProbe.CanRefresh(true, false, t0, lastAttemptUtc: null));
        Assert.False(LiveWorldSizeProbe.CanRefresh(true, false, t0.AddMinutes(1), t0));
        Assert.False(LiveWorldSizeProbe.CanRefresh(true, false, t0.AddMinutes(2).AddSeconds(-1), t0));
        Assert.True(LiveWorldSizeProbe.CanRefresh(true, false, t0.AddMinutes(2), t0));
        Assert.False(LiveWorldSizeProbe.CanRefresh(false, false, t0, lastAttemptUtc: null));
        Assert.False(LiveWorldSizeProbe.CanRefresh(true, measuring: true, t0, lastAttemptUtc: null));
    }

    [Fact]
    public void Refresh_title_explains_stopped_measuring_and_cooldown()
    {
        var t0 = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            LiveWorldSizeProbe.VmStoppedHint,
            LiveWorldSizeProbe.RefreshTitle(false, false, t0, null));
        Assert.Equal(
            LiveWorldSizeProbe.MeasuringHint,
            LiveWorldSizeProbe.RefreshTitle(true, true, t0, null));
        Assert.Equal(
            LiveWorldSizeProbe.EnabledHint,
            LiveWorldSizeProbe.RefreshTitle(true, false, t0, null));
        Assert.Contains(
            "Wait about 2 minutes",
            LiveWorldSizeProbe.RefreshTitle(true, false, t0.AddSeconds(5), t0),
            StringComparison.Ordinal);
        Assert.Contains(
            "Wait a moment",
            LiveWorldSizeProbe.RefreshTitle(true, false, t0.AddMinutes(2).AddSeconds(-30), t0),
            StringComparison.Ordinal);
        Assert.DoesNotContain("friend", LiveWorldSizeProbe.EnabledHint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("friend", LiveWorldSizeProbe.VmStoppedHint, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("friend", LiveWorldSizeProbe.DisplayTitle, StringComparison.OrdinalIgnoreCase);
    }
}
