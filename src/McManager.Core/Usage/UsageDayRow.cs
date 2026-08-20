namespace McManager.Core.Usage;

/// <summary>One UTC day in the current-month usage breakdown (wall-clock hours).</summary>
public sealed class UsageDayRow
{
    public DateOnly Day { get; init; }

    /// <summary>Wall-clock hours the server was on this UTC day.</summary>
    public double UptimeHours { get; init; }

    /// <summary>True when an open ledger interval overlaps this day (no <c>stopped_at</c>).</summary>
    public bool StillRunning { get; init; }
}
