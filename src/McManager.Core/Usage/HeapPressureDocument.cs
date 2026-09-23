using System.Text.Json.Serialization;
using McManager.Core.Setup;

namespace McManager.Core.Usage;

/// <summary>
/// Object Storage <c>meta/heap-pressure.json</c> — durable flag when guest logs show
/// memory pressure. Presence with <see cref="StatusPressure"/> means Manager should
/// warn. Absence means no known pressure. Do not store secrets or live OCIDs.
/// Field names stay internal (<c>current_heap</c>); user-facing copy is server memory.
/// </summary>
public sealed class HeapPressureDocument
{
    public const int DocumentVersion = 1;
    public const string FileName = "heap-pressure.json";
    public const string StatusPressure = "pressure";
    public const string ReasonOom = "oom";
    public const string ReasonGcOverhead = "gc_overhead";
    public const string ReasonRepeatedFullGc = "repeated_full_gc";

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    [JsonPropertyName("status")]
    public string Status { get; set; } = StatusPressure;

    [JsonPropertyName("detected_at")]
    public string DetectedAt { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = ReasonOom;

    [JsonPropertyName("current_heap")]
    public string? CurrentHeap { get; set; }

    [JsonPropertyName("suggested_heap")]
    public string? SuggestedHeap { get; set; }

    [JsonPropertyName("host_memory_gb")]
    public int? HostMemoryGb { get; set; }

    [JsonPropertyName("at_host_max")]
    public bool AtHostMax { get; set; }

    public static HeapPressureDocument CreatePressure(
        string? currentHeap,
        int hostMemoryGb,
        string reason = ReasonOom,
        DateTimeOffset? nowUtc = null)
    {
        var stamp = FormatUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var current = JvmHeapChoice.Normalize(currentHeap);
        var suggested = JvmHeapChoice.NextLarger(current, hostMemoryGb);
        return new HeapPressureDocument
        {
            Version = DocumentVersion,
            Status = StatusPressure,
            DetectedAt = stamp,
            UpdatedAt = stamp,
            Reason = string.IsNullOrWhiteSpace(reason) ? ReasonOom : reason.Trim(),
            CurrentHeap = current,
            SuggestedHeap = suggested,
            HostMemoryGb = JvmHeapChoice.ResolvedHostMemoryGb(hostMemoryGb),
            AtHostMax = suggested is null,
        };
    }

    public static string FormatUtc(DateTimeOffset nowUtc) =>
        nowUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
