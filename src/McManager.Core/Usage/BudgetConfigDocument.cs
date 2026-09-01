using System.Globalization;
using System.Text.Json.Serialization;
using McManager.Core.Config;

namespace McManager.Core.Usage;

/// <summary>Object Storage <c>budget/config.json</c>.</summary>
public sealed class BudgetConfigDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("shape_ocpus")]
    public double ShapeOcpus { get; set; } = 4;

    [JsonPropertyName("shape_memory_gb")]
    public double ShapeMemoryGb { get; set; } = 24;

    [JsonPropertyName("monthly_ocpu_target")]
    public double MonthlyOcpuTarget { get; set; } = 1400;

    [JsonPropertyName("monthly_gb_target")]
    public double MonthlyGbTarget { get; set; } = 8800;

    [JsonPropertyName("soft_ocpu_cap")]
    public double SoftOcpuCap { get; set; } = 1375;

    [JsonPropertyName("soft_gb_cap")]
    public double SoftGbCap { get; set; } = 8600;

    [JsonPropertyName("idle_timeout_minutes")]
    public int IdleTimeoutMinutes { get; set; } = 15;

    [JsonPropertyName("budget_warn_minutes")]
    public int BudgetWarnMinutes { get; set; } = 5;

    [JsonPropertyName("idle_agent_enabled")]
    public bool IdleAgentEnabled { get; set; } = true;

    [JsonPropertyName("daily_ocpu_limit_phase_a")]
    public double DailyOcpuLimitPhaseA { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "always_free";

    /// <summary>
    /// Working UTC-day allocation in OCPU-hours. Missing key = even-split default;
    /// <c>0</c> = zeroed (door will not wake).
    /// </summary>
    [JsonPropertyName("daily_ocpu")]
    public Dictionary<string, double> DailyOcpu { get; set; } = NewMap();

    /// <summary>
    /// Allocation that was in effect when a UTC day closed. Display-only; written once.
    /// </summary>
    [JsonPropertyName("daily_ocpu_planned")]
    public Dictionary<string, double> DailyOcpuPlanned { get; set; } = NewMap();

    public static BudgetConfigDocument FromLocal(
        BudgetSettings budget,
        Vm1Settings vm1,
        DateTimeOffset? nowUtc = null)
    {
        var doc = new BudgetConfigDocument
        {
            MonthlyOcpuTarget = budget.MonthlyOcpuTarget,
            MonthlyGbTarget = budget.MonthlyGbTarget,
            SoftOcpuCap = budget.SoftOcpuCap,
            SoftGbCap = budget.SoftGbCap,
            IdleTimeoutMinutes = budget.IdleTimeoutMinutes,
            BudgetWarnMinutes = budget.BudgetWarnMinutes,
            IdleAgentEnabled = budget.IdleAgentEnabled,
            ShapeOcpus = vm1.ShapeOcpus > 0 ? vm1.ShapeOcpus : 4,
            ShapeMemoryGb = vm1.ShapeMemoryGb > 0 ? vm1.ShapeMemoryGb : 24,
        };
        doc.StampUpdated(nowUtc);
        return doc;
    }

    public void StampUpdated(DateTimeOffset? nowUtc = null)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        UpdatedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        DailyOcpuLimitPhaseA = DeriveDailyOcpuLimit(MonthlyOcpuTarget, now);
    }

    /// <summary>Door wake-gate fallback: monthly target ÷ UTC days in the current month.</summary>
    public static double DeriveDailyOcpuLimit(double monthlyOcpuTarget, DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        var days = DateTime.DaysInMonth(now.Year, now.Month);
        if (days <= 0)
            return 45.0;
        return monthlyOcpuTarget / days;
    }

    public void NormalizeSculptMaps()
    {
        if (DailyOcpu is null || !ReferenceEquals(DailyOcpu.Comparer, StringComparer.Ordinal))
            DailyOcpu = CopyOrdinal(DailyOcpu);
        if (DailyOcpuPlanned is null || !ReferenceEquals(DailyOcpuPlanned.Comparer, StringComparer.Ordinal))
            DailyOcpuPlanned = CopyOrdinal(DailyOcpuPlanned);
    }

    public void CopySculptMapsTo(BudgetConfigDocument dest)
    {
        ArgumentNullException.ThrowIfNull(dest);
        NormalizeSculptMaps();
        dest.DailyOcpu = CopyOrdinal(DailyOcpu);
        dest.DailyOcpuPlanned = CopyOrdinal(DailyOcpuPlanned);
    }

    public string SculptFingerprint()
    {
        NormalizeSculptMaps();
        return MapFingerprint(DailyOcpu) + "#" + MapFingerprint(DailyOcpuPlanned);
    }

    private static Dictionary<string, double> NewMap() =>
        new(StringComparer.Ordinal);

    private static Dictionary<string, double> CopyOrdinal(Dictionary<string, double>? source)
    {
        var copy = NewMap();
        if (source is null)
            return copy;
        foreach (var kv in source)
            copy[kv.Key] = kv.Value;
        return copy;
    }

    private static string MapFingerprint(Dictionary<string, double> map)
    {
        if (map.Count == 0)
            return "";
        var parts = map.OrderBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => kv.Key + "=" + kv.Value.ToString("G17", CultureInfo.InvariantCulture));
        return string.Join(",", parts);
    }
}
