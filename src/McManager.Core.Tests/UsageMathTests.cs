using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class UsageMathTests
{
    private const double MonthlyOcpu = 744.0;
    private const double MonthlyGb = 4464.0;
    private const double SoftOcpu = 744.0;
    private const double SoftGb = 4464.0;

    [Fact]
    public void Days_sum_matches_month_uptime_and_today_hero()
    {
        var now = new DateTime(2026, 8, 20, 15, 0, 0, DateTimeKind.Utc);
        var ledger = new UsageLedgerDocument
        {
            Intervals =
            [
                new UsageInterval
                {
                    Id = "a",
                    StartedAt = "2026-08-19T22:00:00Z",
                    StoppedAt = "2026-08-20T02:00:00Z",
                    Ocpus = 4,
                    MemoryGb = 24,
                },
                new UsageInterval
                {
                    Id = "b",
                    StartedAt = "2026-08-20T10:00:00Z",
                    StoppedAt = null,
                    Ocpus = 4,
                    MemoryGb = 24,
                },
            ],
        };

        var report = UsageMath.ComputeBudgetReport(
            ledger, MonthlyOcpu, MonthlyGb, SoftOcpu, SoftGb, now);

        Assert.Equal(20, report.Days.Count);
        Assert.Equal(report.MonthUptime, report.Days.Sum(d => d.UptimeHours), 3);
        Assert.Equal(report.TodayUptimeHours, report.Days[^1].UptimeHours, 3);
        Assert.True(report.Days[^1].StillRunning);
        Assert.All(report.Days.Take(report.Days.Count - 1), d => Assert.False(d.StillRunning));
        Assert.Equal(new DateOnly(2026, 8, 20), report.Days[^1].Day);
    }

    [Fact]
    public void Closed_interval_splits_across_midnight_utc()
    {
        var now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);
        var ledger = new UsageLedgerDocument
        {
            Intervals =
            [
                new UsageInterval
                {
                    Id = "cross",
                    StartedAt = "2026-08-19T20:00:00Z",
                    StoppedAt = "2026-08-20T04:00:00Z",
                    Ocpus = 2,
                    MemoryGb = 12,
                },
            ],
        };

        var report = UsageMath.ComputeBudgetReport(
            ledger, MonthlyOcpu, MonthlyGb, SoftOcpu, SoftGb, now);

        var day19 = report.Days.First(d => d.Day == new DateOnly(2026, 8, 19));
        var day20 = report.Days.First(d => d.Day == new DateOnly(2026, 8, 20));

        Assert.Equal(4.0, day19.UptimeHours, 2);
        Assert.Equal(4.0, day20.UptimeHours, 2);
        Assert.False(day19.StillRunning);
        Assert.False(day20.StillRunning);
    }

    [Fact]
    public void Empty_ledger_lists_days_through_today_at_zero_hours()
    {
        var now = new DateTime(2026, 8, 5, 8, 0, 0, DateTimeKind.Utc);

        var report = UsageMath.ComputeBudgetReport(
            UsageLedgerDocument.Empty(), MonthlyOcpu, MonthlyGb, SoftOcpu, SoftGb, now);

        Assert.Equal(5, report.Days.Count);
        Assert.All(report.Days, d => Assert.Equal(0, d.UptimeHours));
        Assert.All(report.Days, d => Assert.False(d.StillRunning));
        Assert.Equal(0, report.MonthUptime);
    }

    [Fact]
    public void Days_do_not_include_future_days_of_the_month()
    {
        var now = new DateTime(2026, 8, 10, 0, 0, 0, DateTimeKind.Utc);

        var report = UsageMath.ComputeBudgetReport(
            UsageLedgerDocument.Empty(), MonthlyOcpu, MonthlyGb, SoftOcpu, SoftGb, now);

        Assert.Equal(10, report.Days.Count);
        Assert.Equal(new DateOnly(2026, 8, 10), report.Days[^1].Day);
        Assert.DoesNotContain(report.Days, d => d.Day.Day > 10);
    }
}
