namespace McManager.Hybrid.ViewModels;

/// <summary>One row in the Usage tab “Detailed usage” table.</summary>
public sealed class UsageDayDisplayRow
{
    public string DateLabel { get; init; } = "";

    public string BudgetValue { get; init; } = "";

    public string UsedValue { get; init; } = "";

    public string HoursValue { get; init; } = "";

    public bool IsToday { get; init; }

    public bool IsClosed { get; init; }

    public bool StillRunning { get; init; }
}
