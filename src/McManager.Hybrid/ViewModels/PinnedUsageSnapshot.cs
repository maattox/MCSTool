using McManager.Core.Usage;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Novice-facing pinned usage cards. Values are wall-clock hours, not OCPU-hours.
/// Rollover = unused hours from earlier days this month (<see cref="UsageMath"/> leftover),
/// not hours remaining in the month.
/// </summary>
public sealed class PinnedUsageSnapshot
{
    public string TodayValue { get; init; } = "—";
    public string TodayHint { get; init; } = "";
    public double TodayFraction { get; init; }

    public string AvgValue { get; init; } = "—";
    public string AvgHint { get; init; } = "";
    public double AvgFraction { get; init; }

    public string MonthValue { get; init; } = "—";
    public string MonthHint { get; init; } = "";
    public double MonthFraction { get; init; }

    public string RolloverValue { get; init; } = "—";
    public string RolloverHint { get; init; } = "";
    public bool RolloverPositive { get; init; }

    public string RemainingLabel { get; init; } = AlwaysOnCapableCopy.RemainingHoursLabel(false);
    public string RemainingValue { get; init; } = "—";
    public string RemainingHint { get; init; } = "";
    public double RemainingFraction { get; init; }

    public string IdleValue { get; init; } = "—";
    public string IdleHint { get; init; } = "";

    public string TodayHelp { get; init; } = AlwaysOnCapableCopy.PinTodayHelp(false);
    public string MonthHelp { get; init; } = AlwaysOnCapableCopy.PinMonthHelp(false);
    public string AvgHelp { get; init; } = AlwaysOnCapableCopy.PinAvgHelp(false);
    public string RolloverHelp { get; init; } = AlwaysOnCapableCopy.PinRolloverHelp(false);
    public string RemainingHelp { get; init; } = AlwaysOnCapableCopy.PinRemainingHelp(false);
    public string IdleHelp { get; init; } = AlwaysOnCapableCopy.PinIdleHelp(false);

    public static PinnedUsageSnapshot FromReport(
        BudgetReport report,
        double shapeOcpus,
        int idleTimeoutMinutes)
    {
        var shape = shapeOcpus > 0 ? shapeOcpus : 4;
        var alwaysOn = AlwaysOnCapableCopy.ForShape(shape);
        var dailyHours = report.DailyOcpuAllowance / shape;
        var todayHours = report.TodayUptimeHours;
        var avgHours = report.AvgHoursPerDay;
        var monthPct = report.MonthlyOcpuTarget > 0
            ? Math.Clamp(report.MonthOcpu / report.MonthlyOcpuTarget, 0, 1)
            : 0;
        // Closed-day unused allowance only — not (monthly target − used).
        var rolloverHours = report.LeftoverOcpu / shape;
        var remainingHours = Math.Max(0, report.MonthlyOcpuTarget - report.MonthOcpu) / shape;
        var idleMinutes = idleTimeoutMinutes > 0 ? idleTimeoutMinutes : 15;

        return new PinnedUsageSnapshot
        {
            TodayValue = $"{todayHours:F1}h",
            TodayHint = AlwaysOnCapableCopy.PinTodayHint(dailyHours, alwaysOn),
            TodayFraction = dailyHours > 0 ? Math.Clamp(todayHours / dailyHours, 0, 1) : 0,
            AvgValue = $"{avgHours:F1}h",
            AvgHint = AlwaysOnCapableCopy.PinAvgHint(dailyHours, alwaysOn),
            AvgFraction = dailyHours > 0 ? Math.Clamp(avgHours / dailyHours, 0, 1) : 0,
            MonthValue = $"{monthPct * 100:F0}%",
            MonthHint = AlwaysOnCapableCopy.PinMonthHint(alwaysOn),
            MonthFraction = monthPct,
            RolloverValue = $"{(rolloverHours >= 0 ? "+" : "")}{rolloverHours:F1}h",
            RolloverHint = "unused hours from earlier days",
            RolloverPositive = rolloverHours > 0.05,
            RemainingLabel = AlwaysOnCapableCopy.RemainingHoursLabel(alwaysOn),
            RemainingValue = $"{remainingHours:F1}h",
            RemainingHint = AlwaysOnCapableCopy.PinRemainingHint(alwaysOn),
            RemainingFraction = 1 - monthPct,
            IdleValue = $"{idleMinutes} min",
            IdleHint = AlwaysOnCapableCopy.PinIdleHint(alwaysOn),
            TodayHelp = AlwaysOnCapableCopy.PinTodayHelp(alwaysOn),
            MonthHelp = AlwaysOnCapableCopy.PinMonthHelp(alwaysOn),
            AvgHelp = AlwaysOnCapableCopy.PinAvgHelp(alwaysOn),
            RolloverHelp = AlwaysOnCapableCopy.PinRolloverHelp(alwaysOn),
            RemainingHelp = AlwaysOnCapableCopy.PinRemainingHelp(alwaysOn),
            IdleHelp = AlwaysOnCapableCopy.PinIdleHelp(alwaysOn),
        };
    }
}
