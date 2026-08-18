namespace McManager.Core.Notifications;

public enum NotificationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>
/// In-app notice. Session-only; later steps post via <see cref="NotificationCenter"/>.
/// </summary>
public sealed class AppNotification
{
    public AppNotification(
        string id,
        string title,
        string body,
        NotificationSeverity severity,
        string kind,
        DateTimeOffset createdAt)
    {
        Id = id;
        Title = title;
        Body = body;
        Severity = severity;
        Kind = kind;
        CreatedAt = createdAt;
    }

    public string Id { get; }

    public string Title { get; }

    public string Body { get; }

    public NotificationSeverity Severity { get; }

    /// <summary>Caller tag (e.g. <c>debug</c>). Empty if unset.</summary>
    public string Kind { get; }

    public DateTimeOffset CreatedAt { get; }

    public bool IsRead { get; internal set; }
}

public static class NotificationKinds
{
    public const string Debug = "debug";
}
