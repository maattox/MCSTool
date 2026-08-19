using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class Vm1ShapeScaleUxTests
{
    [Fact]
    public void Apply_is_disabled_unless_vm1_is_stopped()
    {
        Assert.False(Vm1ShapeScaleUx.CanApply("RUNNING", 4, 24, 2, 12));
        Assert.False(Vm1ShapeScaleUx.CanApply("STOPPING", 4, 24, 2, 12));
        Assert.False(Vm1ShapeScaleUx.CanApply("", 4, 24, 2, 12));
        Assert.True(Vm1ShapeScaleUx.CanApply("STOPPED", 4, 24, 2, 12));
        Assert.True(Vm1ShapeScaleUx.CanApply("stopped", 2, 12, 4, 24));
    }

    [Fact]
    public void Apply_is_disabled_when_target_matches_current()
    {
        Assert.False(Vm1ShapeScaleUx.CanApply("STOPPED", 4, 24, 4, 24));
        Assert.False(Vm1ShapeScaleUx.CanApply("STOPPED", 2.0, 12.0, 2, 12));
        Assert.Equal("The game computer is already this size.",
            Vm1ShapeScaleUx.ApplyBlockedReason("STOPPED", 4, 24, 4, 24));
    }

    [Fact]
    public void Eight_ocpu_is_not_offered()
    {
        Assert.False(Vm1ShapeScaleUx.IsAllowedTarget(8, 48));
        Assert.False(Vm1ShapeScaleUx.CanApply("STOPPED", 4, 24, 8, 48));
        Assert.Contains("not offered", Vm1ShapeScaleUx.ApplyBlockedReason("STOPPED", 4, 24, 8, 48));
    }

    [Fact]
    public void Remaining_playtime_divides_the_ocpu_envelope_by_shape()
    {
        Assert.Equal(1400, Vm1ShapeScaleUx.RemainingOcpuHours(1400, 0));
        Assert.Equal(400, Vm1ShapeScaleUx.RemainingOcpuHours(1400, 1000));
        Assert.Equal(0, Vm1ShapeScaleUx.RemainingOcpuHours(1400, 2000));
        Assert.Equal(350, Vm1ShapeScaleUx.RemainingPlayHours(1400, 4));
        Assert.Equal(700, Vm1ShapeScaleUx.RemainingPlayHours(1400, 2));
    }

    [Fact]
    public void Preview_says_more_hours_when_scaling_down_and_less_when_scaling_up()
    {
        var down = Vm1ShapeScaleUx.PreviewBody(4, 24, 2, 12, 1400, 0);
        Assert.Contains("more wall-clock uptime", down);
        Assert.Contains("700.0 h", down);
        Assert.Contains("350.0 h", down);

        var up = Vm1ShapeScaleUx.PreviewBody(2, 12, 4, 24, 1400, 0);
        Assert.Contains("less wall-clock uptime", up);
    }

    [Fact]
    public void Confirm_message_warns_about_burn_rate_and_stopped_requirement()
    {
        var text = Vm1ShapeScaleUx.ConfirmMessage(4, 24, 2, 12, 1400, 200);
        Assert.Contains("Always Free Ampere hours burn", text);
        Assert.Contains("must stay Stopped", text);
        Assert.Contains("not offered", text);
        Assert.DoesNotContain("8 OCPU", text);
    }

    [Fact]
    public void Format_exact_does_not_normalize_unknown_sizes_to_4_24()
    {
        Assert.Equal("3 OCPU / 18 GB", Vm1ShapeScaleUx.FormatExact(3, 18));
        Assert.Equal("4 OCPU / 24 GB", Vm1ShapeScaleUx.FormatExact(4.0, 24.0));
    }

    [Fact]
    public void Blocked_reason_tells_operator_to_stop_first_when_running()
    {
        var reason = Vm1ShapeScaleUx.ApplyBlockedReason("RUNNING", 4, 24, 2, 12);
        Assert.Contains("Stop the server from the top bar first", reason);
        Assert.Contains("RUNNING", reason);
    }
}
