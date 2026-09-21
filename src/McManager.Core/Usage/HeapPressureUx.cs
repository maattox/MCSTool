using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Setup;

namespace McManager.Core.Usage;

/// <summary>
/// Manager copy when <c>meta/heap-pressure.json</c> is set. Notices say server memory,
/// never heap. No click-through; Advanced → Danger is the apply UI.
/// </summary>
public static class HeapPressureUx
{
    public const string NotificationKind = NotificationKinds.HeapPressure;

    public const string NotificationTitle = "Server memory is tight";

    public static bool HasPressure(HeapPressureReadResult? read) =>
        read is { Pressured: true };

    public static string NotificationBody(HeapPressureReadResult? read)
    {
        if (!HasPressure(read))
            return "";

        var doc = read!.Document;
        var suggested = doc?.SuggestedHeap;
        if (!string.IsNullOrWhiteSpace(suggested) && JvmHeapChoice.IsAllowed(suggested)
            && doc is not { AtHostMax: true })
        {
            return "The Minecraft server is short on memory. Apply a larger size on Advanced → Danger "
                + "(this restarts Minecraft). Suggested size: "
                + JvmHeapChoice.Format(suggested)
                + ".";
        }

        var hostGb = JvmHeapChoice.ResolvedHostMemoryGb(doc?.HostMemoryGb ?? 0);
        if (hostGb <= 12)
        {
            return "The Minecraft server is short on memory. Server memory is already at the maximum "
                + "for this VM size. The 24 GB size can offer more RAM. Changing size is on "
                + "Advanced → Danger.";
        }

        return "The Minecraft server is short on memory. Server memory is already at the maximum "
            + "for this VM size. Extra RAM does not fix 4-OCPU lag. You can still review "
            + "Advanced → Danger.";
    }

    /// <summary>
    /// True when a successful Danger apply should DELETE the flag: applied size is
    /// at least the suggested preset, or any larger legal size than the flagged current.
    /// </summary>
    public static bool ShouldClearOnApply(HeapPressureReadResult? read, string? appliedToken)
    {
        if (!HasPressure(read))
            return false;

        if (!JvmHeapChoice.IsAllowed(appliedToken))
            return false;

        var appliedGb = JvmHeapChoice.Gigabytes(appliedToken);
        var doc = read!.Document;
        if (doc is null)
            return true;

        var suggested = doc.SuggestedHeap;
        if (!string.IsNullOrWhiteSpace(suggested) && JvmHeapChoice.IsAllowed(suggested)
            && appliedGb >= JvmHeapChoice.Gigabytes(suggested))
        {
            return true;
        }

        var current = doc.CurrentHeap;
        if (!string.IsNullOrWhiteSpace(current) && JvmHeapChoice.IsAllowed(current)
            && appliedGb > JvmHeapChoice.Gigabytes(current))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Post one warning when pressure is present; drop that kind when the flag is gone.
    /// Transport-failed reads should not call this with a null "absent" result
    /// unless the caller intends to clear the bell.
    /// </summary>
    public static void SyncBell(NotificationCenter notices, HeapPressureReadResult? read)
    {
        ArgumentNullException.ThrowIfNull(notices);
        if (HasPressure(read))
        {
            notices.PostOnce(
                NotificationKind,
                NotificationTitle,
                NotificationBody(read),
                NotificationSeverity.Warning);
            return;
        }

        notices.DismissByKind(NotificationKind);
    }
}
