using System.Text.Json.Serialization;

namespace McManager.Core.Usage;

/// <summary>
/// Object Storage <c>meta/spend-brake-triggered.json</c> — durable $1 spend-brake lock (v1).
/// Presence of the object is the lock; absence is unlocked. Do not store secrets or live OCIDs.
/// </summary>
public sealed class SpendBrakeLockDocument
{
    public const int DocumentVersion = 1;
    public const string FileName = "spend-brake-triggered.json";
    public const string SourceBudgetFunction = "budget_function";
    public const string ReasonCompartmentBudgetThreshold = "compartment_budget_threshold";

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    /// <summary>UTC ISO 8601 when the Function first wrote this lock (or last replaced it).</summary>
    [JsonPropertyName("triggered_at")]
    public string TriggeredAt { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    /// <summary>Writer identity. Function must use <see cref="SourceBudgetFunction"/>.</summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = SourceBudgetFunction;

    /// <summary>
    /// Optional copy of Events <c>triggeredAlertType</c> (e.g. <c>ACTUAL</c>).
    /// Must not be <c>RESET</c> — the Function must not write this object on RESET.
    /// </summary>
    [JsonPropertyName("alert_type")]
    public string? AlertType { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = ReasonCompartmentBudgetThreshold;

    public static SpendBrakeLockDocument Create(
        DateTimeOffset? nowUtc = null,
        string? alertType = null)
    {
        var stamp = FormatUtc(nowUtc ?? DateTimeOffset.UtcNow);
        return new SpendBrakeLockDocument
        {
            Version = DocumentVersion,
            TriggeredAt = stamp,
            UpdatedAt = stamp,
            Source = SourceBudgetFunction,
            AlertType = string.IsNullOrWhiteSpace(alertType) ? null : alertType.Trim(),
            Reason = ReasonCompartmentBudgetThreshold,
        };
    }

    public static string FormatUtc(DateTimeOffset nowUtc) =>
        nowUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
