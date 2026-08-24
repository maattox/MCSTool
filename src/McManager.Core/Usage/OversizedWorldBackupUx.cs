using McManager.Core.Notifications;
using McManager.Core.Services;

namespace McManager.Core.Usage;

/// <summary>
/// Manager copy and routing when <c>meta/oversized-world-backup.json</c> is set.
/// Download World Save uses SSH (never Object Storage) while the flag is blocked.
/// </summary>
public static class OversizedWorldBackupUx
{
    public const string NotificationKind = NotificationKinds.OversizedWorld;

    public const string NotificationTitle = "World save is too large for cloud backup";

    public const string NotificationBody =
        "Automatic cloud backups have stopped because this world is too large for the free "
        + "cloud storage cap. Use Server → Download latest world save while the "
        + "game VM is running — that copies the live world to this PC over SSH and does not "
        + "upload it to cloud storage.";

    public const string StartVmFirstMessage =
        "The game VM must be running to copy the live world over SSH. Start the server, then "
        + "use Download latest world save again. Cloud backups stay paused until this size "
        + "limit is resolved.";

    public const string HelpTitle =
        "Cloud backups of the world zip. When a single save is too large for the free cap, "
        + "automatic uploads stop and Download latest world save copies the live world over "
        + "SSH instead (game VM must be running). That copy is not uploaded to cloud storage. "
        + "Replace copies a zip onto the live server. Wipe world deletes only the live save.";

    /// <summary>
    /// Observed presence is the block. Transport errors are not treated as blocked
    /// (keep the existing Object Storage download path).
    /// </summary>
    public static bool IsBlocked(OversizedWorldBackupReadResult? read) =>
        read is { Blocked: true };

    public static bool UseSshDownload(OversizedWorldBackupReadResult? read) =>
        IsBlocked(read);

    public static bool Vm1IsRunning(string? lifecycle)
    {
        var life = (lifecycle ?? "").Trim().ToUpperInvariant();
        return life == "RUNNING";
    }

    public static string Banner(OversizedWorldBackupReadResult? read)
    {
        if (!IsBlocked(read))
            return "";

        var sizeBit = FormatSizeVsCap(read!.Document);
        return "Automatic cloud backups are paused because this world is too large for free "
            + "cloud storage."
            + sizeBit
            + " Download latest world save copies the live world over SSH while the game VM "
            + "is running. That file stays on this PC — it is not uploaded to cloud storage.";
    }

    public static string DownloadLatestTitle(bool blocked, bool vm1Running)
    {
        if (!blocked)
            return "Download the newest backup zip from cloud storage to this PC.";
        if (!vm1Running)
            return StartVmFirstMessage;
        return "Copy the live world from the game VM to this PC over SSH. Not stored in cloud backup.";
    }

    public static string DownloadLatestButtonLabel(bool blocked) =>
        blocked ? "Download live world (SSH)" : "Download latest world save";

    public static string SuggestedFileName(DateTimeOffset? nowUtc = null)
    {
        var stamp = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'");
        return "world-" + stamp + ".zip";
    }

    public static string FormatSizeVsCap(OversizedWorldBackupDocument? doc)
    {
        if (doc?.ArchiveSizeBytes is not > 0 || doc.SoftCapBytes is not > 0)
            return "";
        return " The last zip was "
            + FormatGiB(doc.ArchiveSizeBytes.Value)
            + " against a "
            + FormatGiB(doc.SoftCapBytes.Value)
            + " cap.";
    }

    public static string FormatGiB(long bytes)
    {
        var gib = bytes / (1024d * 1024d * 1024d);
        return gib >= 10 ? $"{gib:0} GB" : $"{gib:0.#} GB";
    }

    /// <summary>
    /// Post one warning when blocked; drop that kind when the flag is gone.
    /// Transport-failed reads should not call this with a null "absent" result
    /// unless the caller intends to clear the bell.
    /// </summary>
    public static void SyncBell(NotificationCenter notices, OversizedWorldBackupReadResult? read)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (IsBlocked(read))
        {
            notices.PostOnce(
                NotificationKind,
                NotificationTitle,
                NotificationBody,
                NotificationSeverity.Warning);
            return;
        }

        notices.DismissByKind(NotificationKind);
    }
}
