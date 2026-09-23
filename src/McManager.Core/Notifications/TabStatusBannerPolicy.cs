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

        if (text.Equals(ProgressDockUx.ChangePackBuildFallback, StringComparison.Ordinal))
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

        if (text.StartsWith("Emergency power doesn't move the play IP.", StringComparison.Ordinal))
            return false;

        if (text.Equals("Loading server details…", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Loaded server details:", StringComparison.Ordinal))
            return false;

        if (text.Equals("Loading idle settings from cloud storage…", StringComparison.Ordinal))
            return false;

        if (text.Equals("Idle settings loaded from cloud storage.", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Idle settings missing from cloud storage", StringComparison.Ordinal))
            return false;

        if (text.Equals(
                "Cloud storage unavailable — showing this PC's idle settings.",
                StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Selected private key for the", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Doorbell VM will use the game VM private key", StringComparison.Ordinal))
            return false;

        if (text.StartsWith("Game VM will use the doorbell VM private key", StringComparison.Ordinal))
            return false;

        return true;
    }
}
