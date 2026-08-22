namespace McManager.Core.Notifications;

/// <summary>
/// Gates automatic <c>StatusMessage</c> → <see cref="ActionBanner"/> forwarding for
/// tab-open loads. In-page status and summary fields stay; only suppress informational toasts.
/// </summary>
public static class TabStatusBannerPolicy
{
    public static bool ShouldForwardServerManagementStatus(string message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0)
            return false;

        if (text.Equals("Open this tab to list world backups.", StringComparison.Ordinal))
            return false;

        if (text.Equals("Listing backups…", StringComparison.Ordinal))
            return false;

        if (text.Equals("No world backups stored yet.", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Automatic cloud backups are paused.", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Listed ", StringComparison.Ordinal)
            && text.Contains("backup(s)", StringComparison.OrdinalIgnoreCase))
            return false;

        if (text.Equals(ProgressDockUx.ChangePackAnalyzeFallback, StringComparison.Ordinal))
            return false;

        if (text.Equals(ProgressDockUx.ChangePackInstallFallback, StringComparison.Ordinal))
            return false;

        if (text.Equals(ProgressDockUx.ChangePackPickStatus, StringComparison.Ordinal))
            return false;

        if (text.Equals(ProgressDockUx.ChangePackReviewStatus, StringComparison.Ordinal))
            return false;

        return true;
    }

    public static bool ShouldForwardServerManagementIdentityStatus(string message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0)
            return false;

        if (text.StartsWith("No saved identity yet", StringComparison.Ordinal))
            return false;

        if (text.Equals("Could not load server identity.", StringComparison.Ordinal))
            return false;

        return true;
    }

    public static bool ShouldForwardAdvancedStatus(string message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0)
            return false;

        if (text.StartsWith("Break-glass Compute actions do not move the reserved play IP.", StringComparison.Ordinal))
            return false;

        if (text.Equals("Loading meta/infra.json…", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Loaded meta/infra.json:", StringComparison.Ordinal))
            return false;

        if (text.Equals("Loading idle settings from Object Storage…", StringComparison.Ordinal))
            return false;

        if (text.Equals("Idle settings loaded from Object Storage budget.", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("budget/config.json missing", StringComparison.Ordinal))
            return false;

        if (text.Equals(
                "Object Storage unavailable — using local config for idle fields.",
                StringComparison.Ordinal))
            return false;

        return true;
    }
}
