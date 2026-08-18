using System.Text.Json.Serialization;

namespace McManager.Core.Usage;

/// <summary>
/// Object Storage <c>meta/oversized-world-backup.json</c> — durable block when a
/// single world zip exceeds the Always Free backup soft cap.
/// Presence with <see cref="StatusBlocked"/> means automatic OS backups are skipped.
/// Absence means no known oversized-world block. Do not store secrets or live OCIDs.
/// </summary>
public sealed class OversizedWorldBackupDocument
{
    public const int DocumentVersion = 1;
    public const string FileName = "oversized-world-backup.json";
    public const string StatusBlocked = "blocked";
    public const string ReasonArchiveExceedsSoftCap = "archive_exceeds_soft_cap";
    public const string DefaultBackupPrefix = "backups/";

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    [JsonPropertyName("status")]
    public string Status { get; set; } = StatusBlocked;

    [JsonPropertyName("detected_at")]
    public string DetectedAt { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("archive_size_bytes")]
    public long? ArchiveSizeBytes { get; set; }

    [JsonPropertyName("soft_cap_bytes")]
    public long? SoftCapBytes { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = ReasonArchiveExceedsSoftCap;

    [JsonPropertyName("backup_prefix")]
    public string BackupPrefix { get; set; } = DefaultBackupPrefix;

    public static OversizedWorldBackupDocument CreateBlocked(
        DateTimeOffset? nowUtc = null,
        long? archiveSizeBytes = null,
        long? softCapBytes = null)
    {
        var stamp = FormatUtc(nowUtc ?? DateTimeOffset.UtcNow);
        return new OversizedWorldBackupDocument
        {
            Version = DocumentVersion,
            Status = StatusBlocked,
            DetectedAt = stamp,
            UpdatedAt = stamp,
            ArchiveSizeBytes = archiveSizeBytes,
            SoftCapBytes = softCapBytes,
            Reason = ReasonArchiveExceedsSoftCap,
            BackupPrefix = DefaultBackupPrefix,
        };
    }

    public static string FormatUtc(DateTimeOffset nowUtc) =>
        nowUtc.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
}
