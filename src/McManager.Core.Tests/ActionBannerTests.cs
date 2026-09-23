using McManager.Core.Notifications;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ActionBannerTests
{
    [Fact]
    public void Show_then_Dismiss_clears()
    {
        var banner = new ActionBanner();
        var n = 0;
        banner.Changed += (_, _) => n++;

        banner.Show("Listed 2 backup(s).", ActionBannerSeverity.Success);

        Assert.True(banner.IsVisible);
        Assert.Equal("Listed 2 backup(s).", banner.Message);
        Assert.Equal(1, n);

        banner.Dismiss();

        Assert.False(banner.IsVisible);
        Assert.Equal("", banner.Message);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Empty_Show_dismisses()
    {
        var banner = new ActionBanner();
        banner.Show("ok", ActionBannerSeverity.Success);
        banner.Show("  ", ActionBannerSeverity.Error);
        Assert.False(banner.IsVisible);
        Assert.Equal("", banner.Message);
    }

    [Theory]
    [InlineData("Copied play IP.", ActionBannerSeverity.Success, false)]
    [InlineData("Starting the server…", ActionBannerSeverity.Progress, true)]
    [InlineData("Wipe failed.", ActionBannerSeverity.Error, true)]
    [InlineData("Wipe world requires the server to be running (game VM is STOPPED).", ActionBannerSeverity.Warning, true)]
    public void ShouldPersist_matches_severity_and_length(
        string message,
        ActionBannerSeverity severity,
        bool persist)
    {
        Assert.Equal(persist, ActionBanner.ShouldPersist(message, severity));
    }

    [Fact]
    public void ShouldPersist_long_success()
    {
        var longCopy = new string('a', ActionBanner.LongCopyChars + 1);
        Assert.True(ActionBanner.ShouldPersist(longCopy, ActionBannerSeverity.Success));
        Assert.True(ActionBanner.ShouldPersist("line one\nline two", ActionBannerSeverity.Success));
    }

    [Fact]
    public void Show_start_success_autoHide_even_when_slightly_long()
    {
        var banner = new ActionBanner();
        var slightlyLong = new string('a', ActionBanner.LongCopyChars + 5);
        banner.Show(slightlyLong, ActionBannerSeverity.Success, autoHide: true);

        Assert.True(banner.IsVisible);
        Assert.True(banner.AutoHide);
        Assert.True(ActionBanner.ShouldPersist(slightlyLong, ActionBannerSeverity.Success));
    }

    [Fact]
    public void Show_error_never_autoHides()
    {
        var banner = new ActionBanner();
        banner.Show("Start failed.", ActionBannerSeverity.Error, autoHide: true);
        Assert.False(banner.AutoHide);
        Assert.True(banner.IsVisible);
    }

    [Fact]
    public void Show_short_success_autoHides_by_default()
    {
        var banner = new ActionBanner();
        banner.Show("Server is running.", ActionBannerSeverity.Success);
        Assert.True(banner.AutoHide);
    }

    [Fact]
    public void InferSeverity_wipe_while_stopped_is_warning()
    {
        var msg =
            "Wipe world requires the server to be running (game VM is STOPPED). "
            + "Start the server first, then wipe. Cloud backups stay until you delete them separately.";
        Assert.Equal(ActionBannerSeverity.Warning, ActionBanner.InferSeverity(msg));
        Assert.True(ActionBanner.ShouldPersist(msg, ActionBannerSeverity.Warning));
    }

    [Theory]
    [InlineData("Wiping live world…", ActionBannerSeverity.Progress)]
    [InlineData("Wipe cancelled.", ActionBannerSeverity.Success)]
    [InlineData("List failed.", ActionBannerSeverity.Error)]
    [InlineData("This server's settings are missing.", ActionBannerSeverity.Error)]
    [InlineData("Copied play IP.", ActionBannerSeverity.Success)]
    [InlineData("Cloud backup storage isn't configured.", ActionBannerSeverity.Warning)]
    [InlineData("Could not connect to Oracle Cloud: timeout", ActionBannerSeverity.Error)]
    [InlineData("Starting the doorbell VM and moving the play IP…", ActionBannerSeverity.Progress)]
    [InlineData("The $1 spending limit blocked Start.", ActionBannerSeverity.Warning)]
    [InlineData("Saving the whitelist to cloud storage failed: 409", ActionBannerSeverity.Error)]
    [InlineData("Replace world requires the server to be running (game VM is STOPPED).", ActionBannerSeverity.Warning)]
    [InlineData("Some MCSTool files were not found. Reinstall MCSTool.", ActionBannerSeverity.Warning)]
    [InlineData("Oracle's free Ampere capacity is unavailable right now.", ActionBannerSeverity.Error)]
    [InlineData("Saved server details. Updated to the current format.", ActionBannerSeverity.Success)]
    [InlineData("Emergency power doesn't move the play IP. Use Start and Stop in the sidebar for normal use.", ActionBannerSeverity.Success)]
    [InlineData("Idle settings missing from cloud storage — showing this PC's settings.", ActionBannerSeverity.Error)]
    public void InferSeverity_common_copy(string message, ActionBannerSeverity expected) =>
        Assert.Equal(expected, ActionBanner.InferSeverity(message));

    public static TheoryData<string, ActionBannerSeverity> CoreConstantCopy => new()
    {
        { ProgressDockUx.ChangePackPickStatus, ActionBannerSeverity.Success },
        { ProgressDockUx.ChangePackReviewStatus, ActionBannerSeverity.Success },
        { ProgressDockUx.ChangePackBuildFallback, ActionBannerSeverity.Progress },
        { ProgressDockUx.ChangePackInstallFallback, ActionBannerSeverity.Progress },
        { McManager.Core.Services.MinecraftConsoleRemote.RconUnreachableHint, ActionBannerSeverity.Error },
        { McManager.Core.Usage.OversizedWorldBackupUx.StartVmFirstMessage, ActionBannerSeverity.Warning },
    };

    [Theory]
    [MemberData(nameof(CoreConstantCopy))]
    public void InferSeverity_core_constants(string message, ActionBannerSeverity expected) =>
        Assert.Equal(expected, ActionBanner.InferSeverity(message));

    [Fact]
    public void ShowInferred_uses_infer()
    {
        var banner = new ActionBanner();
        banner.ShowInferred("Wiping live world…");
        Assert.Equal(ActionBannerSeverity.Progress, banner.Severity);
        Assert.True(banner.IsVisible);
    }
}
