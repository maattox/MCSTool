using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class BudgetSculptTests
{
    private static readonly DateTime Now = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DeriveDailyOcpuLimit_uses_utc_month_length()
    {
        // 2026-03-01 07:00Z is still February evening in America/Los_Angeles (28 days).
        var marchUtc = new DateTimeOffset(2026, 3, 1, 7, 0, 0, TimeSpan.Zero);
        var daily = BudgetConfigDocument.DeriveDailyOcpuLimit(1400, marchUtc);
        Assert.Equal(1400.0 / 31.0, daily, 6);
    }

    [Fact]
    public void Empty_maps_bank_equals_closed_day_unused_even_split()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var env = BudgetSculpt.ComputeEnvelope(budget, used, Now);

        var daily = 1400.0 / 31.0;
        var closedDays = 9;
        Assert.Equal(daily * closedDays, env.BankOcpu, 6);
        Assert.Equal(0, env.UnbudgetedOcpu, 6);
        Assert.Equal(env.ClosedUnusedOcpu, env.RolloverOcpu, 6);
        Assert.Equal(daily * closedDays, env.RolloverOcpu, 6);
        Assert.Equal(0, env.RolloverSpentOcpu, 6);
        Assert.True(env.FitsMonthly);
    }

    [Fact]
    public void Zero_and_set_future_day_hours()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var zero = new DateOnly(2026, 8, 15);
        var set = new DateOnly(2026, 8, 20);

        Assert.Null(BudgetSculpt.TryZeroDays(budget, [zero], used, Now));
        Assert.True(BudgetSculpt.IsZeroed(budget, zero));
        Assert.Equal(0, BudgetSculpt.AllocationOcpu(budget, zero));

        Assert.Null(BudgetSculpt.TrySetDays(budget, [set], wallClockHours: 6, used, Now));
        Assert.Equal(24.0, BudgetSculpt.AllocationOcpu(budget, set), 6);

        var env = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.True(env.FitsMonthly);
        Assert.True(env.BankOcpu > 0);
    }

    [Fact]
    public void Zero_future_day_raises_unbudgeted_not_rollover()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var before = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        var even = 1400.0 / 31.0;

        Assert.Null(BudgetSculpt.TryZeroDays(budget, [new DateOnly(2026, 8, 15)], used, Now));
        var after = BudgetSculpt.ComputeEnvelope(budget, used, Now);

        Assert.Equal(0, before.UnbudgetedOcpu, 6);
        Assert.Equal(even, after.UnbudgetedOcpu, 6);
        Assert.Equal(before.RolloverOcpu, after.RolloverOcpu, 6);
        Assert.Equal(before.ClosedUnusedOcpu, after.ClosedUnusedOcpu, 6);
        Assert.Equal(0, after.RolloverSpentOcpu, 6);
    }

    [Fact]
    public void Surplus_funded_by_unbudgeted_does_not_spend_rollover()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var before = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        var even = 1400.0 / 31.0;

        Assert.Null(BudgetSculpt.TryZeroDays(budget, [new DateOnly(2026, 8, 15)], used, Now));
        Assert.Null(BudgetSculpt.TrySetDays(
            budget, [new DateOnly(2026, 8, 20)], wallClockHours: (even + 8.0) / 4.0, used, Now));

        var after = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.Equal(even - 8.0, after.UnbudgetedOcpu, 5);
        Assert.Equal(0, after.RolloverSpentOcpu, 6);
        Assert.Equal(before.RolloverOcpu, after.RolloverOcpu, 6);
        Assert.True(after.FitsMonthly);
    }

    [Fact]
    public void Surplus_beyond_unbudgeted_spends_rollover()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var before = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        var even = 1400.0 / 31.0;

        Assert.Null(BudgetSculpt.TrySetDays(
            budget, [new DateOnly(2026, 8, 18)], wallClockHours: (even + 8.0) / 4.0, used, Now));
        var after = BudgetSculpt.ComputeEnvelope(budget, used, Now);

        Assert.Equal(8.0, BudgetSculpt.AllocationOcpu(budget, new DateOnly(2026, 8, 18)) - even, 5);
        Assert.Equal(0, after.UnbudgetedOcpu, 6);
        Assert.Equal(8.0, after.RolloverSpentOcpu, 5);
        Assert.Equal(before.RolloverOcpu - 8.0, after.RolloverOcpu, 5);
        Assert.True(after.BankOcpu < before.BankOcpu);
        Assert.Equal(before.BankOcpu - 8.0, after.BankOcpu, 5);
    }

    [Fact]
    public void FitsMonthly_false_when_reserved_plus_used_closed_exceeds_target()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        foreach (var day in BudgetSculpt.EditableDays(Now))
            Assert.Null(BudgetSculpt.TrySetDays(budget, [day], wallClockHours: 24, used, Now));

        var env = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.False(env.FitsMonthly);
        Assert.True(env.UsedClosedOcpu + env.ReservedOcpu > budget.MonthlyOcpuTarget);
        var gate = BudgetSculpt.EvaluateSave(env, useRolloverHours: true, minBufferWallClockHours: 0, shapeOcpus: 4);
        Assert.False(gate.CanSave);
        Assert.True(gate.ExceedsMonthly);
    }

    [Fact]
    public void Set_applies_even_when_envelope_exceeds_monthly()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var err = BudgetSculpt.TrySetDays(budget, [new DateOnly(2026, 8, 20)], wallClockHours: 24, used, Now);
        Assert.Null(err);
        Assert.Equal(96.0, BudgetSculpt.AllocationOcpu(budget, new DateOnly(2026, 8, 20)), 6);
    }

    [Fact]
    public void Suggested_min_buffer_and_available_rollover()
    {
        // unbudgeted 0, rollover 7h, buffer 5, need 4 extra → suggest buffer 3.
        var rolloverOcpu = 7.0 * 4.0;
        var spentOcpu = 4.0 * 4.0;
        var leftover = rolloverOcpu - spentOcpu;
        Assert.Equal(8.0, BudgetSculpt.AvailableRolloverOcpu(rolloverOcpu, minBufferWallClockHours: 5, shapeOcpus: 4), 6);
        Assert.Equal(8.0, BudgetSculpt.RolloverShortfallOcpu(spentOcpu, 8.0), 6);
        Assert.Equal(3.0, BudgetSculpt.SuggestedMinBufferWallHours(leftover, shapeOcpus: 4), 6);

        var budget = NewBudget();
        var even = 1400.0 / 31.0;
        var closedUnused = 7.0 * 4.0;
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        for (var d = 1; d <= 8; d++)
            used[new DateOnly(2026, 8, d)] = (even, even * 6);
        used[new DateOnly(2026, 8, 9)] = (even - closedUnused, (even - closedUnused) * 6);
        Assert.Null(BudgetSculpt.TrySetDays(
            budget, [new DateOnly(2026, 8, 18)], wallClockHours: (even + 16.0) / 4.0, used, Now));
        var env = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.Equal(16.0, env.RolloverSpentOcpu, 5);
        Assert.Equal(closedUnused, env.ClosedUnusedOcpu, 5);

        var blocked = BudgetSculpt.EvaluateSave(env, useRolloverHours: true, minBufferWallClockHours: 5, shapeOcpus: 4);
        Assert.False(blocked.CanSave);
        Assert.True(blocked.BufferBlocks);
        Assert.Equal(BudgetSculpt.SuggestedMinBufferWallHours(env.RolloverOcpu, 4), blocked.SuggestedMinBufferWallHours, 6);

        var ok = BudgetSculpt.EvaluateSave(env, useRolloverHours: true, minBufferWallClockHours: blocked.SuggestedMinBufferWallHours, shapeOcpus: 4);
        Assert.True(ok.CanSave);

        var disabled = BudgetSculpt.EvaluateSave(env, useRolloverHours: false, minBufferWallClockHours: 0, shapeOcpus: 4);
        Assert.False(disabled.CanSave);
        Assert.Contains("Use rollover hours", disabled.Warning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Closed_day_is_not_editable_and_snapshot_is_stable()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var closed = new DateOnly(2026, 8, 5);

        var err = BudgetSculpt.TrySetDays(budget, [closed], 3, used, Now);
        Assert.NotNull(err);

        Assert.True(BudgetSculpt.SnapshotClosedDays(budget, Now));
        Assert.False(BudgetSculpt.SnapshotClosedDays(budget, Now));
        Assert.True(budget.DailyOcpuPlanned.ContainsKey("2026-08-05"));
        Assert.Equal(1400.0 / 31.0, budget.DailyOcpuPlanned["2026-08-05"], 6);
    }

    [Fact]
    public void Today_cannot_go_below_hours_already_used()
    {
        var budget = NewBudget();
        var today = new DateOnly(2026, 8, 10);
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>
        {
            [today] = (16.0, 96.0),
        };

        var err = BudgetSculpt.TrySetDays(budget, [today], wallClockHours: 2, used, Now);
        Assert.NotNull(err);
        Assert.Contains("already used", err, StringComparison.OrdinalIgnoreCase);

        var zeroErr = BudgetSculpt.TryZeroDays(budget, [today], used, Now);
        Assert.NotNull(zeroErr);
        Assert.Contains("zero out", zeroErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Redistribute_unbudgeted_onto_unspecified_days()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        Assert.Null(BudgetSculpt.TryZeroDays(budget, [new DateOnly(2026, 8, 15)], used, Now));
        var envBefore = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.True(envBefore.UnbudgetedOcpu > 1);

        Assert.Null(BudgetSculpt.TryRedistributePoolOntoUnspecified(budget, used, Now, envBefore.UnbudgetedOcpu));

        Assert.True(BudgetSculpt.TryGetExplicit(budget, new DateOnly(2026, 8, 16), out _));
        Assert.True(BudgetSculpt.IsZeroed(budget, new DateOnly(2026, 8, 15)));
        var env = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.True(env.FitsMonthly);
        Assert.True(env.UnbudgetedOcpu < 1e-3);
        Assert.Equal(envBefore.RolloverOcpu, env.RolloverOcpu, 5);
    }

    [Fact]
    public void Reset_today_and_future_clears_keys_not_closed_planned()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        Assert.True(BudgetSculpt.SnapshotClosedDays(budget, Now));
        Assert.Null(BudgetSculpt.TryZeroDays(budget, [new DateOnly(2026, 8, 15)], used, Now));
        budget.DailyOcpu["2026-08-05"] = 0;

        BudgetSculpt.ResetTodayAndFutureToDefault(budget, Now);

        Assert.False(budget.DailyOcpu.ContainsKey("2026-08-15"));
        Assert.True(budget.DailyOcpu.ContainsKey("2026-08-05"));
        Assert.True(budget.DailyOcpuPlanned.ContainsKey("2026-08-05"));
    }

    [Fact]
    public void Set_rejects_more_than_24_wall_clock_hours()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var err = BudgetSculpt.TrySetDays(
            budget, [new DateOnly(2026, 8, 20)], wallClockHours: 24.1, used, Now);
        Assert.NotNull(err);
        Assert.Contains("24", err, StringComparison.OrdinalIgnoreCase);
        Assert.False(budget.DailyOcpu.ContainsKey("2026-08-20"));
    }

    [Fact]
    public void Redistribute_does_not_push_a_day_past_24_hours()
    {
        var budget = NewBudget();
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        var day = new DateOnly(2026, 8, 20);
        Assert.Null(BudgetSculpt.TrySetDays(budget, [day], wallClockHours: 23, used, Now));

        var pool = BudgetSculpt.OcpuHoursFromWallClock(8, shapeOcpus: 4);
        Assert.Null(BudgetSculpt.TryRedistributePoolOntoSelected(budget, [day], used, Now, pool));
        Assert.Equal(
            BudgetSculpt.OcpuHoursFromWallClock(24, 4),
            BudgetSculpt.AllocationOcpu(budget, day),
            6);
    }

    [Fact]
    public void Unbudgeted_goes_negative_when_over_allocated_without_rollover()
    {
        var budget = NewBudget();
        var even = 1400.0 / 31.0;
        var used = new Dictionary<DateOnly, (double Ocpu, double Gb)>();
        for (var d = 1; d <= 9; d++)
            used[new DateOnly(2026, 8, d)] = (even, even * 6);

        var before = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.Equal(0, before.UnbudgetedOcpu, 6);
        Assert.Equal(0, before.RolloverOcpu, 6);

        Assert.Null(BudgetSculpt.TrySetDays(
            budget, [new DateOnly(2026, 8, 20)], wallClockHours: (even + 16.0) / 4.0, used, Now));
        var after = BudgetSculpt.ComputeEnvelope(budget, used, Now);
        Assert.Equal(-16.0, after.UnbudgetedOcpu, 5);
        Assert.Equal(0, after.UnbudgetedPoolOcpu, 6);
        Assert.Equal(0, after.RolloverOcpu, 6);
    }

    [Fact]
    public void ComputeBudgetReport_leftover_is_bank()
    {
        var budget = NewBudget();
        var report = UsageMath.ComputeBudgetReport(UsageLedgerDocument.Empty(), budget, Now);
        var env = BudgetSculpt.ComputeEnvelope(budget, new Dictionary<DateOnly, (double, double)>(), Now);
        Assert.Equal(env.BankOcpu, report.LeftoverOcpu, 6);
        Assert.Equal(env.UnbudgetedOcpu, report.UnbudgetedOcpu, 6);
        Assert.Equal(env.RolloverOcpu, report.RolloverOcpu, 6);
        Assert.Equal(31, report.CalendarDays.Count);
        Assert.Equal(10, report.Days.Count);
        Assert.Contains(report.CalendarDays, d => d.Day == new DateOnly(2026, 8, 31) && d.IsFuture);
    }

    private static BudgetConfigDocument NewBudget() => new()
    {
        MonthlyOcpuTarget = 1400,
        MonthlyGbTarget = 8800,
        SoftOcpuCap = 1375,
        SoftGbCap = 8600,
        ShapeOcpus = 4,
        ShapeMemoryGb = 24,
    };
}
