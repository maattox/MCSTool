using McManager.Core.Notifications;
using McManager.Core.Setup;
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
    [InlineData(true, "Creating cloud resources…", "Deploying…", "Creating cloud resources…")]
    [InlineData(true, "  ", "Deploying…", "Deploying…")]
    [InlineData(false, "Creating cloud resources…", "Ready to deploy.", "Ready to deploy.")]
    public void OneLineStatus_prefers_caption_only_while_busy(
        bool jobActive,
        string caption,
        string fallback,
        string expected) =>
        Assert.Equal(expected, ProgressDockUx.OneLineStatus(jobActive, caption, fallback));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(false, true, true, false)]
    public void Change_pack_dock_is_tab_scoped(
        bool sessionOpen,
        bool onServerTab,
        bool onChangePackPane,
        bool expected) =>
        Assert.Equal(
            expected,
            ProgressDockUx.ShowChangePackDock(sessionOpen, onServerTab, onChangePackPane));

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

    [Fact]
    public void DisplayName_is_novice_english()
    {
        Assert.Equal("Creating cloud resources…", SetupApplyStage.DisplayName(SetupApplyStage.TofuApplied));
        Assert.Equal("Waiting for the servers to start…", SetupApplyStage.DisplayName(SetupApplyStage.CloudInit));
        Assert.Equal("Installing doorbell software…", SetupApplyStage.DisplayName(SetupApplyStage.Door));
        Assert.DoesNotContain("VM", SetupApplyStage.DisplayName(SetupApplyStage.CloudInit), StringComparison.Ordinal);
        Assert.DoesNotContain("stack", SetupApplyStage.DisplayName(SetupApplyStage.Door), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("> rm -rf /tmp/mcmgr-onbox && mkdir -p /tmp/mcmgr-onbox", "Preparing files on the server…")]
    [InlineData("$ tofu apply -auto-approve", "Creating cloud resources…")]
    [InlineData("waiting for /etc/mcmgr/cloud-init-done on 10.0.0.2…", "Waiting for the servers to start…")]
    [InlineData("Door src: C:\\repo\\door_vm", "Installing doorbell software…")]
    [InlineData("onbox src: /opt DISTRIBUTION=fabric MINECRAFT_VERSION=1.21.1", "Installing Minecraft…")]
    [InlineData("uploaded pack files (12 files, skipped 0 eula/properties/world) → /opt/mcmgr", "Installing pack files…")]
    [InlineData("RCON list succeeded.", "Minecraft is ready.")]
    [InlineData("Minecraft crash detected during health check; stopping the unit.", "Minecraft crashed while starting.")]
    [InlineData("The loader blamed one mod for the crash; moving it aside and retrying once…", "Moving the blamed mod aside…")]
    [InlineData("Retrying Minecraft without the blamed mod…", "Retrying Minecraft without that mod…")]
    public void TryHumanizeLogLine_maps_noisy_bootstrap(string raw, string expected) =>
        Assert.Equal(expected, ProgressDockUx.TryHumanizeLogLine(raw));

    [Fact]
    public void TryHumanizeLogLine_ignores_unmapped_shell() =>
        Assert.Null(ProgressDockUx.TryHumanizeLogLine("> chmod 755 /opt/mystery && echo done"));

    [Fact]
    public void HumanizeOrFallback_never_returns_raw_rm()
    {
        var caption = ProgressDockUx.HumanizeOrFallback(
            "> rm -rf /tmp/mcmgr-onbox && mkdir -p /tmp/mcmgr-onbox",
            "Installing Minecraft…");
        Assert.Equal("Preparing files on the server…", caption);
        Assert.DoesNotContain("rm -rf", caption, StringComparison.Ordinal);
    }

    [Fact]
    public void HumanizeOrFallback_keeps_fallback_for_unmapped_shell() =>
        Assert.Equal(
            "Reinstalling Minecraft from this pack…",
            ProgressDockUx.HumanizeOrFallback(
                "> chmod 755 /opt/mystery",
                ProgressDockUx.ChangePackInstallFallback));
}
