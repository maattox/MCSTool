using McManager.Core.Notifications;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ProgressDockUxTests
{
    [Fact]
    public void FormatElapsed_under_an_hour_omits_hours() =>
        Assert.Equal("Time elapsed: 3:05", ProgressDockUx.FormatElapsed(TimeSpan.FromSeconds(185)));

    [Fact]
    public void FormatElapsed_includes_hours() =>
        Assert.Equal("Time elapsed: 1:02:03", ProgressDockUx.FormatElapsed(new TimeSpan(1, 2, 3)));

    [Fact]
    public void FormatElapsed_clamps_negative() =>
        Assert.Equal("Time elapsed: 0:00", ProgressDockUx.FormatElapsed(TimeSpan.FromSeconds(-4)));

    [Theory]
    [InlineData(true, "Creating VMs…", "Deploying…", "Creating VMs…")]
    [InlineData(true, "  ", "Deploying…", "Deploying…")]
    [InlineData(false, "Creating VMs…", "Ready to deploy.", "Ready to deploy.")]
    public void OneLineStatus_prefers_caption_only_while_busy(
        bool jobActive,
        string caption,
        string fallback,
        string expected) =>
        Assert.Equal(expected, ProgressDockUx.OneLineStatus(jobActive, caption, fallback));

    [Fact]
    public void Change_pack_dock_follows_the_panel()
    {
        Assert.True(ProgressDockUx.ShowChangePackDock(true));
        Assert.False(ProgressDockUx.ShowChangePackDock(false));
    }

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, false)]
    public void Job_progress_is_analyze_or_replace(bool analyzing, bool replace, bool expected) =>
        Assert.Equal(expected, ProgressDockUx.ShowJobProgress(analyzing, replace));

    [Fact]
    public void Change_pack_percent_is_unknown() =>
        Assert.True(ProgressDockUx.PercentUnknown(hasStagePercent: false));

    [Fact]
    public void Setup_percent_is_known() =>
        Assert.False(ProgressDockUx.PercentUnknown(hasStagePercent: true));
}
