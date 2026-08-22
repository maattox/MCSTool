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
    [InlineData("Loaded meta/infra.json: play=1.2.3.4 bucket=mcmgr", false)]
    [InlineData("Loading meta/infra.json…", false)]
    [InlineData("Idle settings loaded from Object Storage budget.", false)]
    [InlineData("budget/config.json missing — seeded from local config.", false)]
    [InlineData("Publish meta failed.", true)]
    [InlineData("Break-glass: START VM1 (no IP move)…", true)]
    [InlineData("Auto-detect: scanning OCI profiles…", true)]
    public void Advanced_status_gate(string message, bool forward) =>
        Assert.Equal(forward, TabStatusBannerPolicy.ShouldForwardAdvancedStatus(message));
}
