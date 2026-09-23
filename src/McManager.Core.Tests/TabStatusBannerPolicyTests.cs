using McManager.Core.Notifications;
using Xunit;

namespace McManager.Core.Tests;

public sealed class TabStatusBannerPolicyTests
{
    [Theory]
    [InlineData("Listed 3 backup(s). Select one to download.", false)]
    [InlineData("No world backups stored yet.", false)]
    [InlineData("Open this tab to list world backups.", false)]
    [InlineData("Listing backups…", false)]
    [InlineData(
        "Automatic cloud backups are paused. Download latest copies the live world over SSH.",
        false)]
    [InlineData("List failed.", true)]
    [InlineData("Upload failed.", true)]
    [InlineData("Wiping live world via SSH…", true)]
    [InlineData(ProgressDockUx.ChangePackAnalyzeFallback, false)]
    [InlineData(ProgressDockUx.ChangePackBuildFallback, false)]
    [InlineData(ProgressDockUx.ChangePackInstallFallback, false)]
    [InlineData(ProgressDockUx.ChangePackPickStatus, false)]
    [InlineData(ProgressDockUx.ChangePackReviewStatus, false)]
    public void ServerManagement_status_gate(string message, bool forward) =>
        Assert.Equal(forward, TabStatusBannerPolicy.ShouldForwardServerManagementStatus(message));

    [Theory]
    [InlineData("No saved identity yet — defaults are shown. Save to create the shared file.", false)]
    [InlineData("Could not load server identity.", false)]
    [InlineData("Save failed.", true)]
    [InlineData("Icon selected. Save to store it.", true)]
    public void ServerManagement_identity_gate(string message, bool forward) =>
        Assert.Equal(forward, TabStatusBannerPolicy.ShouldForwardServerManagementIdentityStatus(message));

    [Theory]
    [InlineData("Emergency power doesn't move the play IP. Use Start and Stop in the sidebar for normal use.", false)]
    [InlineData("Loaded server details: play=1.2.3.4 bucket=mcmgr", false)]
    [InlineData("Loading server details…", false)]
    [InlineData("Loading idle settings from cloud storage…", false)]
    [InlineData("Idle settings loaded from cloud storage.", false)]
    [InlineData("Idle settings missing from cloud storage — showing this PC's settings.", false)]
    [InlineData("Cloud storage unavailable — showing this PC's idle settings.", false)]
    [InlineData("Saving server details failed.", true)]
    [InlineData("Starting the game VM (play IP not moved)…", true)]
    [InlineData("Looking for an existing server…", true)]
    [InlineData("Selected private key for the game VM. Save to use it.", false)]
    [InlineData("Doorbell VM will use the game VM private key after Save.", false)]
    [InlineData("Game VM will use the doorbell VM private key after Save.", false)]
    [InlineData("Saved SSH key paths on this PC. Both VMs use the same private key file.", true)]
    public void Advanced_status_gate(string message, bool forward) =>
        Assert.Equal(forward, TabStatusBannerPolicy.ShouldForwardAdvancedStatus(message));
}
