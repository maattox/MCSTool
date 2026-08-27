using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ManageNoviceStatusTests
{
    [Fact]
    public void Running_when_vm1_is_on_even_if_door_is_not_playable()
    {
        Assert.Equal(
            ManageNoviceStatus.Running,
            ManageNoviceStatus.Label("RUNNING", doorPlayable: false, doorStarting: false));
        Assert.True(
            ManageNoviceStatus.IsRunning(
                ManageNoviceStatus.Label("RUNNING", doorPlayable: false, doorStarting: false)));
    }

    [Fact]
    public void Running_when_door_is_playable()
    {
        Assert.Equal(
            ManageNoviceStatus.Running,
            ManageNoviceStatus.Label("STOPPED", doorPlayable: true, doorStarting: false));
        Assert.Equal(
            ManageNoviceStatus.Running,
            ManageNoviceStatus.Label("RUNNING", doorPlayable: true, doorStarting: false));
    }

    [Fact]
    public void Running_when_vm_is_on_even_if_door_is_still_starting()
    {
        Assert.Equal(
            ManageNoviceStatus.Running,
            ManageNoviceStatus.Label("RUNNING", doorPlayable: false, doorStarting: true));
    }

    [Fact]
    public void Starting_when_vm_or_door_is_coming_up_and_vm_is_not_on()
    {
        Assert.Equal(
            ManageNoviceStatus.Starting,
            ManageNoviceStatus.Label("STARTING", doorPlayable: false, doorStarting: false));
        Assert.Equal(
            ManageNoviceStatus.Starting,
            ManageNoviceStatus.Label("PROVISIONING", doorPlayable: false, doorStarting: false));
        Assert.Equal(
            ManageNoviceStatus.Starting,
            ManageNoviceStatus.Label("STOPPED", doorPlayable: false, doorStarting: true));
        Assert.True(
            ManageNoviceStatus.IsBusy(
                ManageNoviceStatus.Label("STARTING", doorPlayable: false, doorStarting: false)));
    }

    [Fact]
    public void Stopping_when_vm1_is_stopping()
    {
        Assert.Equal(
            ManageNoviceStatus.Stopping,
            ManageNoviceStatus.Label("STOPPING", doorPlayable: true, doorStarting: false));
        Assert.True(ManageNoviceStatus.IsBusy(ManageNoviceStatus.Stopping));
    }

    [Fact]
    public void Stopped_when_vm_and_door_are_off()
    {
        Assert.Equal(
            ManageNoviceStatus.Stopped,
            ManageNoviceStatus.Label("STOPPED", doorPlayable: false, doorStarting: false));
        Assert.Equal(
            ManageNoviceStatus.Stopped,
            ManageNoviceStatus.Label(null, doorPlayable: false, doorStarting: false));
        Assert.False(
            ManageNoviceStatus.IsRunning(
                ManageNoviceStatus.Label("STOPPED", doorPlayable: false, doorStarting: false)));
    }

    [Fact]
    public void Playable_name_accepts_door_playable_alias()
    {
        Assert.True(DoorStatus.IsPlayableName("PLAYABLE"));
        Assert.True(DoorStatus.IsPlayableName("DOOR_PLAYABLE"));
        Assert.True(new DoorStatus { Door = "DOOR_PLAYABLE" }.IsPlayable);
        Assert.False(DoorStatus.IsPlayableName("DOOR_IDLE"));
        Assert.False(DoorStatus.IsPlayableName("unreachable"));
    }
}
