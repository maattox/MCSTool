namespace McManager.Core.Notifications;

/// <summary>
/// Shared copy and visibility for the window-locked Setup / Change pack progress dock.
/// Progress lives on the dock; compact toasts stay for outcomes, not the running job.
/// </summary>
public static class ProgressDockUx
{
    public const string ChangePackPickStatus = "Choose a pack file, then install.";

    public const string ChangePackReviewStatus = "Review the pack, then install.";

    public const string ChangePackAnalyzeFallback = "Analyzing modpack…";

    public const string ChangePackInstallFallback = "Reinstalling Minecraft from this pack…";

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var totalSeconds = (int)Math.Floor(elapsed.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"Time elapsed: {hours}:{minutes:D2}:{seconds:D2}"
            : $"Time elapsed: {minutes}:{seconds:D2}";
    }

    public static string OneLineStatus(bool jobActive, string? caption, string? fallback)
    {
        if (jobActive && !string.IsNullOrWhiteSpace(caption))
            return caption.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
    }

    public static bool ShowChangePackDock(bool showChangePackUi) => showChangePackUi;

    public static bool ShowJobProgress(bool analyzing, bool replaceRunning) =>
        analyzing || replaceRunning;

    /// <summary>
    /// Setup stages report a percent. Change pack SSH lines do not — use an indeterminate bar.
    /// </summary>
    public static bool PercentUnknown(bool hasStagePercent) => !hasStagePercent;
}
