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
        UpdatedAt = now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
        DailyOcpuLimitPhaseA = DeriveDailyOcpuLimit(MonthlyOcpuTarget, now);
    }

    /// <summary>
    /// Door wake gate daily share — lab uses America/Los_Angeles month length.
    /// </summary>
    public static double DeriveDailyOcpuLimit(double monthlyOcpuTarget, DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        var la = ResolveLosAngeles();
        var local = TimeZoneInfo.ConvertTimeFromUtc(now, la);
        var days = DateTime.DaysInMonth(local.Year, local.Month);
        if (days <= 0)
            return 45.0;
        return monthlyOcpuTarget / days;
    }

    private static TimeZoneInfo ResolveLosAngeles()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");
        }
    }
}
