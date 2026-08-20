using System.Text.Json.Serialization;

namespace McManager.Core.Services;

public sealed class DoorStatus
{
    [JsonPropertyName("door")]
    public string Door { get; init; } = "";

    [JsonPropertyName("wake_in_progress")]
    public bool WakeInProgress { get; init; }

    [JsonPropertyName("stop_in_progress")]
    public bool StopInProgress { get; init; }

    [JsonPropertyName("last_error")]
    public string LastError { get; init; } = "";

    [JsonPropertyName("used_ocpu_hours")]
    public double? UsedOcpuHours { get; init; }

    [JsonPropertyName("remaining_ocpu_hours")]
    public double? RemainingOcpuHours { get; init; }

    [JsonPropertyName("daily_limit_ocpu_hours")]
    public double? DailyLimitOcpuHours { get; init; }

    public bool IsStarting =>
        WakeInProgress
        || string.Equals(Door, "STARTING", StringComparison.OrdinalIgnoreCase);

    public bool IsPlayable =>
        string.Equals(Door, "PLAYABLE", StringComparison.OrdinalIgnoreCase);

    public bool IsIdle =>
        string.Equals(Door, "DOOR_IDLE", StringComparison.OrdinalIgnoreCase);

    public bool IsBudgetExhausted =>
        string.Equals(Door, "BUDGET_EXHAUSTED", StringComparison.OrdinalIgnoreCase);

    public bool IsSpendBrake =>
        string.Equals(Door, "SPEND_BRAKE", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Door, "DOOR_SPEND_BRAKE", StringComparison.OrdinalIgnoreCase);

    public bool IsDegraded =>
        string.Equals(Door, "DEGRADED", StringComparison.OrdinalIgnoreCase);

    public string ToDisplayLabel(string? vm1Lifecycle)
    {
        if (IsDegraded)
        {
            return string.IsNullOrWhiteSpace(LastError)
                ? "Degraded"
                : $"Degraded — {LastError}";
        }

        if (IsPlayable)
        {
            var life = (vm1Lifecycle ?? "").ToUpperInvariant();
            if (life is "STOPPED" or "STOPPING")
                return "Degraded / recovering (door PLAYABLE, VM1 down)";
            return "Playable";
        }

        if (IsStarting)
            return "Starting";

        if (IsBudgetExhausted)
            return "Budget exhausted";

        if (IsIdle)
            return "Idle (doorbell)";

        return string.IsNullOrWhiteSpace(Door) ? "Unknown" : Door;
    }
}
