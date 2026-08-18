using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Notifications;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Title-row bell + list shell. Posts go through <see cref="NotificationCenter"/>.
/// </summary>
public sealed partial class NotificationCenterViewModel : ObservableObject, IDisposable
{
    public const string EmptyState =
        "Nothing waiting. Notices about this server show here — for example if a world save is too large for automatic cloud backup.";

    public const string DebugTitle = "DEBUG: sample notice";

    public const string DebugBody =
        "This is a test notice. Later, real notices (for example a world save too large for cloud backup) will show here. Dismiss it from the bell.";

    private readonly NotificationCenter _center;

    public NotificationCenterViewModel(NotificationCenter center)
    {
        _center = center;
        _center.Changed += OnCenterChanged;
        Refresh();
    }

    [ObservableProperty]
    private bool _panelOpen;

    [ObservableProperty]
    private IReadOnlyList<AppNotification> _items = [];

    [ObservableProperty]
    private int _unreadCount;

    public bool HasUnread => UnreadCount > 0;

    public bool IsEmpty => Items.Count == 0;

    public string BellAriaLabel =>
        HasUnread
            ? $"Notifications, {UnreadCount} unread"
            : "Notifications";

    public void Toggle()
    {
        if (PanelOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        _center.MarkAllRead();
        PanelOpen = true;
        Refresh();
    }

    public void Close() => PanelOpen = false;

    public void Dismiss(string id) => _center.Dismiss(id);

    public void DismissAll() => _center.DismissAll();

    public void PostDebugSample()
    {
        _center.Post(
            DebugTitle,
            DebugBody,
            NotificationSeverity.Warning,
            NotificationKinds.Debug);
    }

    public static string FormatStamp(AppNotification item) =>
        item.CreatedAt.ToLocalTime().ToString("MMM d, h:mm tt");

    public static string SeverityIcon(AppNotification item) =>
        item.Severity switch
        {
            NotificationSeverity.Warning => "ti ti-alert-triangle",
            NotificationSeverity.Error => "ti ti-alert-triangle",
            _ => "ti ti-info-circle",
        };

    public static string SeverityClass(AppNotification item) =>
        item.Severity switch
        {
            NotificationSeverity.Warning => "is-warning",
            NotificationSeverity.Error => "is-error",
            _ => "is-info",
        };

    public void Dispose() => _center.Changed -= OnCenterChanged;

    private void OnCenterChanged(object? sender, EventArgs e)
    {
        if (PanelOpen)
            _center.MarkAllRead();
        Refresh();
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(BellAriaLabel));
    }

    private void Refresh()
    {
        Items = _center.Snapshot();
        UnreadCount = _center.UnreadCount;
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(BellAriaLabel));
    }
}
