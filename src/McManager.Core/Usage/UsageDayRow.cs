namespace McManager.Core.Usage;

/// <summary>One UTC day in the current-month usage breakdown (wall-clock hours).</summary>
public sealed class UsageDayRow
{
    public DateOnly Day { get; init; }

    /// <summary>Wall-clock hours the server was on this UTC day.</summary>
    public double UptimeHours { get; init; }

    /// <summary>OCPU-hours charged this UTC day.</summary>
    public double OcpuHours { get; init; }

    /// <summary>GB-hours charged this UTC day.</summary>
    public double GbHours { get; init; }

    /// <summary>Working (or closed planned) allocation in OCPU-hours.</summary>
    public double BudgetOcpuHours { get; init; }

    /// <summary>Budget as wall-clock hours at the current shape.</summary>
    public double BudgetWallClockHours { get; init; }

    public bool IsClosed { get; init; }

    public bool IsZeroed { get; init; }

    public bool IsSculpted { get; init; }

    public bool IsFuture { get; init; }

    /// <summary>True when an open ledger interval overlaps this day (no <c>stopped_at</c>).</summary>
    public bool StillRunning { get; init; }
}
