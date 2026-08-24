namespace McManager.Core.Notifications;

public enum ActionBannerSeverity
{
    Success = 0,
    Progress = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// One Manager-window action result. Tabs and chrome post here instead of
/// grey <c>StatusMessage</c> at the bottom of a scrolled tab. Not the bell.
/// </summary>
public sealed class ActionBanner
{
    public const int LongCopyChars = 80;

    public event EventHandler? Changed;

    public string Message { get; private set; } = "";

    public ActionBannerSeverity Severity { get; private set; }

    public bool IsVisible { get; private set; }

    /// <summary>
    /// When true, the UI may auto-hide after a short delay (fade). Errors, warnings,
    /// and progress never set this. Callers can force it for start-success copy that
    /// is slightly over <see cref="LongCopyChars"/>.
    /// </summary>
    public bool AutoHide { get; private set; }

    /// <summary>
    /// Errors, warnings, progress, and long success copy stay until dismiss
    /// (or a newer <see cref="Show"/>). Progress is not timer-auto-hidden — callers
    /// replace it with success/error when the job ends. Short success may auto-hide in the UI.
    /// </summary>
    public static bool ShouldPersist(string message, ActionBannerSeverity severity)
    {
        if (severity != ActionBannerSeverity.Success)
            return true;
        if (string.IsNullOrWhiteSpace(message))
            return false;
        return message.Length > LongCopyChars || message.Contains('\n');
    }

    public static ActionBannerSeverity InferSeverity(string message)
    {
        var text = (message ?? "").Trim();
        if (text.Length == 0)
            return ActionBannerSeverity.Success;

        if (text.EndsWith('…') || text.EndsWith("...", StringComparison.Ordinal))
            return ActionBannerSeverity.Progress;

        if (Contains(text, "cancelled"))
            return ActionBannerSeverity.Success;

        if (Contains(text, "requires")
            || Contains(text, "blocked")
            || Contains(text, "paused")
            || Contains(text, "not found")
            || Contains(text, "incompatible")
            || Contains(text, "isn't configured")
            || Contains(text, "not configured"))
            return ActionBannerSeverity.Warning;

        if (Contains(text, "fail")
            || Contains(text, "could not")
            || Contains(text, "invalid")
            || Contains(text, "unavailable")
            || Contains(text, "missing")
            || Contains(text, "error")
            || Contains(text, "denied"))
            return ActionBannerSeverity.Error;

        return ActionBannerSeverity.Success;
    }

    public void Show(string message, ActionBannerSeverity severity, bool? autoHide = null)
    {
        var trimmed = (message ?? "").Trim();
        if (trimmed.Length == 0)
        {
            Dismiss();
            return;
        }

        Message = trimmed;
        Severity = severity;
        IsVisible = true;
        AutoHide = severity == ActionBannerSeverity.Success
            && (autoHide ?? !ShouldPersist(trimmed, severity));
        RaiseChanged();
    }

    public void ShowInferred(string message)
    {
        var trimmed = (message ?? "").Trim();
        if (trimmed.Length == 0)
            return;
        Show(trimmed, InferSeverity(trimmed));
    }

    public void Dismiss()
    {
        if (!IsVisible && Message.Length == 0)
            return;
        IsVisible = false;
        AutoHide = false;
        Message = "";
        Severity = ActionBannerSeverity.Success;
        RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

    private static bool Contains(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
