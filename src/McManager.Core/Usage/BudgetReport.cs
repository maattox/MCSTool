namespace McManager.Core.Usage;

public sealed class BudgetReport
{
    public int Year { get; init; }
    public int Month { get; init; }
    public int DaysInMonth { get; init; }
    public double DailyOcpuAllowance { get; init; }
    public double DailyGbAllowance { get; init; }
    public double MonthlyOcpuTarget { get; init; }
    public double MonthlyGbTarget { get; init; }
    public double SoftOcpuCap { get; init; }
    public double SoftGbCap { get; init; }
    public double MonthOcpu { get; init; }
    public double MonthGb { get; init; }
    public double MonthUptime { get; init; }
    public double TodayUptimeHours { get; init; }
    public double TodayOcpu { get; init; }
    public double TodayGb { get; init; }
    public double LeftoverOcpu { get; init; }
    public double LeftoverGb { get; init; }
    public double ReservedOcpu { get; init; }
    public double UsedClosedOcpu { get; init; }
    public double UnbudgetedOcpu { get; init; }
    public double RolloverOcpu { get; init; }
    public double ClosedUnusedOcpu { get; init; }
    public bool EnvelopeFits { get; init; }
    public bool OcpuOverDaily { get; init; }
    public bool GbOverDaily { get; init; }
    public bool HitSoftCap { get; init; }
    public int DayOfMonth { get; init; }
    public double AvgHoursPerDay { get; init; }

    /// <summary>UTC days from the 1st through today in the report month.</summary>
    public IReadOnlyList<UsageDayRow> Days { get; init; } = [];

    /// <summary>Every UTC day in the report month (including future) for the sculpt calendar.</summary>
    public IReadOnlyList<UsageDayRow> CalendarDays { get; init; } = [];

    public string FormatTodayBar() =>
        $"{TodayOcpu:F1}/{DailyOcpuAllowance:F1} OCPU-h";
}
