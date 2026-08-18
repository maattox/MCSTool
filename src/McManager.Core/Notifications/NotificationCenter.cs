namespace McManager.Core.Notifications;

/// <summary>
/// In-memory notification channel. Later Manager steps call <see cref="Post"/>;
/// the title-row bell is a reader. Not persisted.
/// </summary>
public sealed class NotificationCenter
{
    public const int MaxItems = 50;

    private readonly object _gate = new();
    private readonly List<AppNotification> _items = [];
    private readonly TimeProvider _time;

    public NotificationCenter(TimeProvider? time = null)
    {
        _time = time ?? TimeProvider.System;
    }

    public event EventHandler? Changed;

    public int Count
    {
        get
        {
            lock (_gate)
                return _items.Count;
        }
    }

    public int UnreadCount
    {
        get
        {
            lock (_gate)
            {
                var n = 0;
                foreach (var item in _items)
                {
                    if (!item.IsRead)
                        n++;
                }

                return n;
            }
        }
    }

    /// <summary>Newest first. Snapshot copy.</summary>
    public IReadOnlyList<AppNotification> Snapshot()
    {
        lock (_gate)
            return _items.ToArray();
    }

    /// <summary>
    /// Adds a notice at the front. Empty title is ignored. Caps at <see cref="MaxItems"/>.
    /// </summary>
    public AppNotification? Post(
        string title,
        string body,
        NotificationSeverity severity = NotificationSeverity.Info,
        string kind = "")
    {
        var trimmedTitle = (title ?? "").Trim();
        if (trimmedTitle.Length == 0)
            return null;

        var item = new AppNotification(
            Guid.NewGuid().ToString("N"),
            trimmedTitle,
            (body ?? "").Trim(),
            severity,
            (kind ?? "").Trim(),
            _time.GetUtcNow());

        lock (_gate)
        {
            _items.Insert(0, item);
            while (_items.Count > MaxItems)
                _items.RemoveAt(_items.Count - 1);
        }

        RaiseChanged();
        return item;
    }

    /// <summary>
    /// Posts only when no existing item has the same non-empty <paramref name="kind"/>.
    /// Empty kind always posts (same as <see cref="Post"/>).
    /// </summary>
    public AppNotification? PostOnce(
        string kind,
        string title,
        string body,
        NotificationSeverity severity = NotificationSeverity.Info)
    {
        var trimmedKind = (kind ?? "").Trim();
        if (trimmedKind.Length == 0)
            return Post(title, body, severity);

        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (string.Equals(item.Kind, trimmedKind, StringComparison.Ordinal))
                    return null;
            }
        }

        return Post(title, body, severity, trimmedKind);
    }

    public bool HasKind(string kind)
    {
        var trimmed = (kind ?? "").Trim();
        if (trimmed.Length == 0)
            return false;

        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (string.Equals(item.Kind, trimmed, StringComparison.Ordinal))
                    return true;
            }
        }

        return false;
    }

    public bool Dismiss(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var removed = false;
        lock (_gate)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (!string.Equals(_items[i].Id, id, StringComparison.Ordinal))
                    continue;
                _items.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (removed)
            RaiseChanged();
        return removed;
    }

    public int DismissByKind(string kind)
    {
        var trimmed = (kind ?? "").Trim();
        if (trimmed.Length == 0)
            return 0;

        var removed = 0;
        lock (_gate)
        {
            for (var i = _items.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(_items[i].Kind, trimmed, StringComparison.Ordinal))
                    continue;
                _items.RemoveAt(i);
                removed++;
            }
        }

        if (removed > 0)
            RaiseChanged();
        return removed;
    }

    public void DismissAll()
    {
        lock (_gate)
        {
            if (_items.Count == 0)
                return;
            _items.Clear();
        }

        RaiseChanged();
    }

    public void MarkAllRead()
    {
        var changed = false;
        lock (_gate)
        {
            foreach (var item in _items)
            {
                if (item.IsRead)
                    continue;
                item.IsRead = true;
                changed = true;
            }
        }

        if (changed)
            RaiseChanged();
    }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
