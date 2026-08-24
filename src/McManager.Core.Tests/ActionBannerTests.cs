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
    [InlineData("Starting the game server…", ActionBannerSeverity.Progress, true)]
    [InlineData("Wipe failed.", ActionBannerSeverity.Error, true)]
    [InlineData("VM1 is STOPPED — Wipe world requires RUNNING.", ActionBannerSeverity.Warning, true)]
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
            "VM1 is 'STOPPED' — Wipe world requires RUNNING. "
            + "Start the game VM first, then wipe. Cloud backups stay until you delete them separately.";
        Assert.Equal(ActionBannerSeverity.Warning, ActionBanner.InferSeverity(msg));
        Assert.True(ActionBanner.ShouldPersist(msg, ActionBannerSeverity.Warning));
    }

    [Theory]
    [InlineData("Wiping live world via SSH…", ActionBannerSeverity.Progress)]
    [InlineData("Wipe cancelled.", ActionBannerSeverity.Success)]
    [InlineData("List failed.", ActionBannerSeverity.Error)]
    [InlineData("Local config is missing.", ActionBannerSeverity.Error)]
    [InlineData("Copied play IP.", ActionBannerSeverity.Success)]
    [InlineData("Shared backup storage isn't configured.", ActionBannerSeverity.Warning)]
    public void InferSeverity_common_copy(string message, ActionBannerSeverity expected) =>
        Assert.Equal(expected, ActionBanner.InferSeverity(message));

    [Fact]
    public void ShowInferred_uses_infer()
    {
        var banner = new ActionBanner();
        banner.ShowInferred("Wiping live world via SSH…");
        Assert.Equal(ActionBannerSeverity.Progress, banner.Severity);
        Assert.True(banner.IsVisible);
    }
}
