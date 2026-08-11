using System.Text.Json.Serialization;

namespace McManager.Core.Usage;

/// <summary>Object Storage <c>ledger/usage.json</c> (v2 intervals with ocpus/memory_gb).</summary>
public sealed class UsageLedgerDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 2;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("intervals")]
    public List<UsageInterval> Intervals { get; set; } = [];

    [JsonPropertyName("daily_overrides")]
    public Dictionary<string, DailyOverride> DailyOverrides { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("idle_since")]
    public string? IdleSince { get; set; }

    [JsonPropertyName("last_budget_warn_at")]
    public string? LastBudgetWarnAt { get; set; }

    public static UsageLedgerDocument Empty() => new();
}

public sealed class UsageInterval
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }

    [JsonPropertyName("stopped_at")]
    public string? StoppedAt { get; set; }

    [JsonPropertyName("ocpus")]
    public double Ocpus { get; set; }

    [JsonPropertyName("memory_gb")]
    public double MemoryGb { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("stop_source")]
    public string? StopSource { get; set; }
}

public sealed class DailyOverride
{
    [JsonPropertyName("uptime_hours")]
    public double UptimeHours { get; set; }

    [JsonPropertyName("ocpu_hours")]
    public double OcpuHours { get; set; }

    [JsonPropertyName("gb_hours")]
    public double GbHours { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }
}
