using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class AlwaysOnCapableCopyTests
{
    [Fact]
    public void Two_ocpu_can_stay_up_around_the_clock_four_cannot()
    {
        Assert.True(Vm1ShapeScaleUx.CanStayUpAroundTheClock(2));
        Assert.True(AlwaysOnCapableCopy.ForShape(2));
        Assert.False(Vm1ShapeScaleUx.CanStayUpAroundTheClock(4));
        Assert.False(AlwaysOnCapableCopy.ForShape(4));
        Assert.False(Vm1ShapeScaleUx.CanStayUpAroundTheClock(0));
        Assert.False(Vm1ShapeScaleUx.CanStayUpAroundTheClock(-1));
    }

    [Fact]
    public void Two_twelve_copy_does_not_nag_scarcity()
    {
        Assert.Contains("usually stay on all month", AlwaysOnCapableCopy.UsageLead(true));
        Assert.DoesNotContain("out of hours", AlwaysOnCapableCopy.UsageLead(true));
        Assert.Equal("Hours available this month", AlwaysOnCapableCopy.RemainingHoursLabel(true));
        Assert.Contains("still counted", AlwaysOnCapableCopy.RemainingHoursHint(true));
        Assert.DoesNotContain("cap — not the rollover", AlwaysOnCapableCopy.RemainingHoursHint(true));
        Assert.DoesNotContain("run out of free time", AlwaysOnCapableCopy.PublishConfirmBody(true));
        Assert.Contains("still counted", AlwaysOnCapableCopy.PublishConfirmBody(true));
        Assert.Equal("used this month", AlwaysOnCapableCopy.PinMonthHint(true));
        Assert.DoesNotContain("monthly cap", AlwaysOnCapableCopy.PinMonthHint(true));
        Assert.Contains("today", AlwaysOnCapableCopy.PinTodayHint(22.5, true));
        Assert.DoesNotContain("allowed", AlwaysOnCapableCopy.PinTodayHint(22.5, true));
        Assert.Contains("typical day", AlwaysOnCapableCopy.PinAvgHint(22.5, true));
        Assert.DoesNotContain("budget", AlwaysOnCapableCopy.PinAvgHint(22.5, true));
        Assert.DoesNotContain("daily slice", AlwaysOnCapableCopy.PinTodayHelp(true));
        Assert.DoesNotContain("free compute budget already used", AlwaysOnCapableCopy.PinMonthHelp(true));
        Assert.DoesNotContain("today's allowed hours", AlwaysOnCapableCopy.PinAvgHelp(true));
        Assert.DoesNotContain("hours still left in the month", AlwaysOnCapableCopy.PinRolloverHelp(true));
    }

    [Fact]
    public void Four_twenty_four_copy_keeps_scarce_budget_language()
    {
        Assert.Contains("out of hours", AlwaysOnCapableCopy.UsageLead(false));
        Assert.Equal("Hours left this month", AlwaysOnCapableCopy.RemainingHoursLabel(false));
        Assert.Contains("not the rollover bank", AlwaysOnCapableCopy.RemainingHoursHint(false));
        Assert.Contains("run out of free time", AlwaysOnCapableCopy.PublishConfirmBody(false));
        Assert.Equal("of monthly cap", AlwaysOnCapableCopy.PinMonthHint(false));
        Assert.Contains("allowed", AlwaysOnCapableCopy.PinTodayHint(11.25, false));
        Assert.Contains("budget", AlwaysOnCapableCopy.PinAvgHint(11.25, false));
        Assert.Contains("daily slice", AlwaysOnCapableCopy.PinTodayHelp(false));
        Assert.Contains("free compute budget already used", AlwaysOnCapableCopy.PinMonthHelp(false));
    }
}
