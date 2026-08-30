using McManager.Core.Notifications;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupIdentityApplyTests
{
    [Fact]
    public void OsMeta_is_after_the_first_minecraft_start()
    {
        Assert.True(
            SetupApplyStage.IndexOf(SetupApplyStage.OsMeta)
            > SetupApplyStage.IndexOf(SetupApplyStage.Vm1));
    }

    [Fact]
    public void Restart_log_is_mapped_for_the_setup_dock()
    {
        Assert.Equal(
            "Applying the server list name and icon…",
            ProgressDockUx.TryHumanizeLogLine(SetupIdentityApply.RestartLog));
    }
}
