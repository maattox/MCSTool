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
        Assert.Equal("The server is already this size.",
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
        Assert.Contains("more hours of uptime", down);
        Assert.Contains("700.0 hours", down);
        Assert.Contains("350.0 hours", down);
        Assert.DoesNotContain("wall-clock", down, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("OCPU-h", down, StringComparison.Ordinal);

        var up = Vm1ShapeScaleUx.PreviewBody(2, 12, 4, 24, 1400, 0);
        Assert.Contains("fewer hours of uptime", up);
    }

    [Fact]
    public void Confirm_message_warns_about_burn_rate_and_stopped_requirement()
    {
        var text = Vm1ShapeScaleUx.ConfirmMessage(4, 24, 2, 12, 1400, 200);
        Assert.Contains("how fast Always Free hours are used", text);
        Assert.Contains("must stay Stopped", text);
        Assert.Contains("not offered", text);
        Assert.DoesNotContain("8 OCPU", text);
        Assert.DoesNotContain("heap", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Xmx", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Xms", text, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("10G")]
    [InlineData("12G")]
    public void Confirm_and_preview_say_server_memory_becomes_8G_when_shrinking_from_24(string heap)
    {
        var confirm = Vm1ShapeScaleUx.ConfirmMessage(4, 24, 2, 12, 1400, 0, heap);
        Assert.Contains("Server memory will be set to 8G so it fits this size.", confirm);
        Assert.Contains("server memory", confirm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("heap", confirm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Xmx", confirm, StringComparison.OrdinalIgnoreCase);

        var preview = Vm1ShapeScaleUx.PreviewBody(4, 24, 2, 12, 1400, 0, heap);
        Assert.Contains("Server memory will be set to 8G so it fits this size.", preview);
    }

    [Fact]
    public void Downsize_with_8G_or_less_does_not_rewrite_server_memory_copy()
    {
        foreach (var heap in new[] { "4G", "6G", "8G", null, "" })
        {
            var text = Vm1ShapeScaleUx.ConfirmMessage(4, 24, 2, 12, 1400, 0, heap);
            Assert.DoesNotContain("Server memory will be set to", text);
        }
    }

    [Fact]
    public void Upsize_does_not_raise_or_mention_server_memory_clamp()
    {
        foreach (var heap in new[] { "4G", "8G", "10G", "12G" })
        {
            var text = Vm1ShapeScaleUx.ConfirmMessage(2, 12, 4, 24, 1400, 0, heap);
            Assert.DoesNotContain("Server memory will be set to", text);
            Assert.Equal(heap, Vm1ShapeScaleUx.ServerMemoryAfterResize(heap, 24));
            Assert.False(Vm1ShapeScaleUx.ServerMemoryWillClamp(heap, 24));
        }
    }

    [Fact]
    public void Server_memory_after_resize_clamps_only_when_the_host_is_tight()
    {
        Assert.Equal("8G", Vm1ShapeScaleUx.ServerMemoryAfterResize("12G", 12));
        Assert.Equal("8G", Vm1ShapeScaleUx.ServerMemoryAfterResize("10G", 12));
        Assert.Equal("8G", Vm1ShapeScaleUx.ServerMemoryAfterResize("8G", 12));
        Assert.Equal("6G", Vm1ShapeScaleUx.ServerMemoryAfterResize("6G", 12));
        Assert.Equal("10G", Vm1ShapeScaleUx.ServerMemoryAfterResize("10G", 24));
        Assert.True(Vm1ShapeScaleUx.ServerMemoryWillClamp("12G", 12));
        Assert.False(Vm1ShapeScaleUx.ServerMemoryWillClamp("8G", 12));
        Assert.Equal("Server memory will be set to 8G so it fits this size.",
            Vm1ShapeScaleUx.ServerMemoryClampSentence(12, "10G"));
        Assert.Null(Vm1ShapeScaleUx.ServerMemoryClampSentence(24, "12G"));
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
        Assert.Contains("Stop the server from the sidebar first", reason);
        Assert.Contains("RUNNING", reason);
    }
}
