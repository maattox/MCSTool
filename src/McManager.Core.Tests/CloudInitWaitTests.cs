using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class CloudInitWaitTests
{
    [Fact]
    public void Probe_uses_sudo_and_reports_marker_and_status()
    {
        var cmd = SetupBootstrapService.CloudInitProbeCommand("/etc/mcmgr/cloud-init-done");
        Assert.Contains("sudo -n test -f '/etc/mcmgr/cloud-init-done'", cmd, StringComparison.Ordinal);
        Assert.Contains("MARKER_OK", cmd, StringComparison.Ordinal);
        Assert.Contains("MARKER_WAIT", cmd, StringComparison.Ordinal);
        Assert.Contains("cloud-init status", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void Marker_ready_requires_MARKER_OK()
    {
        Assert.True(SetupBootstrapService.MarkerProbeReady("MARKER_OK\nstatus: done"));
        Assert.False(SetupBootstrapService.MarkerProbeReady("MARKER_WAIT\nstatus: done"));
        Assert.False(SetupBootstrapService.MarkerProbeReady("WAIT"));
    }

    [Fact]
    public void Finished_without_marker_when_cloud_init_done()
    {
        Assert.True(SetupBootstrapService.CloudInitFinishedWithoutMarker(
            "MARKER_WAIT\nstatus: done\nextended_status: degraded done"));
        Assert.False(SetupBootstrapService.CloudInitFinishedWithoutMarker(
            "MARKER_OK\nstatus: done"));
        Assert.False(SetupBootstrapService.CloudInitFinishedWithoutMarker(
            "MARKER_WAIT\nstatus: running"));
    }
}
