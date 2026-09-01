namespace McManager.Hybrid.ViewModels;

/// <summary>One cell in the Usage UTC calendar heatmap.</summary>
public sealed class UsageCalendarCell
{
    public DateOnly? Day { get; init; }

    public string DayNum { get; init; } = "";

    public string HoursLabel { get; init; } = "";

    public bool IsPad { get; init; }

    public bool IsToday { get; init; }

    public bool IsClosed { get; init; }

    public bool IsZeroed { get; init; }

    public bool IsSelected { get; init; }

    public bool IsSculpted { get; init; }

    public bool IsFuture { get; init; }

    /// <summary>0–1 heat from allocated wall-clock hours versus 24h.</summary>
    public double Heat { get; init; }

    public string Title { get; init; } = "";

    public string CssClass
    {
        get
        {
            if (IsPad)
                return "mcm-cal-cell is-pad";
            var parts = new List<string> { "mcm-cal-cell" };
            if (IsToday)
                parts.Add("is-today");
            if (IsClosed)
                parts.Add("is-closed");
            if (IsZeroed)
                parts.Add("is-zeroed");
            if (IsSelected)
                parts.Add("is-selected");
            if (IsSculpted && !IsZeroed)
                parts.Add("is-sculpted");
            if (IsFuture)
                parts.Add("is-future");
            return string.Join(" ", parts);
        }
    }

    public string HeatStyle
    {
        get
        {
            if (IsPad || IsZeroed)
                return "";
            var a = Math.Clamp(Heat, 0, 1);
            return $"background: rgba(77, 142, 245, {0.08 + a * 0.42:0.###});";
        }
    }
}
