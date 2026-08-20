using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ManagePowerUxTests
{
    private static bool CanStart(
        string? lifecycle,
        bool doorPlayable = false,
        bool doorStarting = false,
        bool doorDegraded = false,
        bool spendBrakeBlocks = false,
        bool doorStatusKnown = true,
        bool hasInitialStatus = true,
        bool powerActionInFlight = false,
        bool spendBrakeUnlockInFlight = false,
        bool configLoaded = true) =>
        ManagePowerUx.CanStart(
            hasInitialStatus,
            powerActionInFlight,
            spendBrakeUnlockInFlight,
            configLoaded,
            lifecycle,
            doorPlayable,
            doorStarting,
            doorDegraded,
            spendBrakeBlocks,
            doorStatusKnown);

    [Fact]
    public void Start_is_enabled_only_when_vm1_is_stopped()
    {
        Assert.True(CanStart("STOPPED"));
        Assert.True(CanStart(" stopped "));
        Assert.False(CanStart("STOPPING"));
        Assert.False(CanStart("STARTING"));
        Assert.False(CanStart("PROVISIONING"));
        Assert.False(CanStart("RUNNING"));
        Assert.False(CanStart(""));
        Assert.False(CanStart("   "));
        Assert.False(CanStart(null));
        Assert.False(CanStart("—"));
        Assert.False(CanStart("UNKNOWN"));
    }

    [Fact]
    public void Start_stays_off_while_already_on_or_coming_up()
    {
        Assert.False(CanStart("STOPPED", doorPlayable: true));
        Assert.False(CanStart("STOPPED", doorStarting: true));
        Assert.False(CanStart("RUNNING"));
        Assert.False(CanStart("STARTING"));
        Assert.False(CanStart("PROVISIONING"));
    }

    [Fact]
    public void Start_stays_off_when_spend_brake_blocks_or_door_unknown()
    {
        Assert.False(CanStart("STOPPED", spendBrakeBlocks: true));
        Assert.False(CanStart("STOPPED", doorStatusKnown: false));
        Assert.False(CanStart("STOPPED", hasInitialStatus: false));
        Assert.False(CanStart("STOPPED", powerActionInFlight: true));
        Assert.False(CanStart("STOPPED", spendBrakeUnlockInFlight: true));
        Assert.False(CanStart("STOPPED", configLoaded: false));
    }

    [Fact]
    public void Stopping_tooltip_asks_to_wait_for_full_stop()
    {
        Assert.True(ManagePowerUx.IsVm1Stopping("STOPPING"));
        Assert.False(ManagePowerUx.LifecycleAllowsStart("STOPPING"));
        Assert.Equal(
            "Wait until the server has fully stopped.",
            ManagePowerUx.WaitUntilFullyStoppedToolTip);
    }
}
