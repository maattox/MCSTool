using McManager.Core.Notifications;
using Xunit;

namespace McManager.Core.Tests;

public sealed class NotificationCenterTests
{
    [Fact]
    public void Post_adds_newest_first_and_counts_unread()
    {
        var clock = new SteppingTimeProvider();
        var center = new NotificationCenter(clock);

        var first = center.Post("Older", "a");
        var second = center.Post("Newer", "b");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(2, center.Count);
        Assert.Equal(2, center.UnreadCount);

        var snap = center.Snapshot();
        Assert.Equal("Newer", snap[0].Title);
        Assert.Equal("Older", snap[1].Title);
        Assert.True(snap[0].CreatedAt > snap[1].CreatedAt);
    }

    [Fact]
    public void Empty_title_is_ignored()
    {
        var center = new NotificationCenter();
        Assert.Null(center.Post("  ", "body"));
        Assert.Equal(0, center.Count);
    }

    [Fact]
    public void MarkAllRead_clears_unread_and_keeps_items()
    {
        var center = new NotificationCenter();
        center.Post("One", "x");
        center.Post("Two", "y");
        center.MarkAllRead();

        Assert.Equal(2, center.Count);
        Assert.Equal(0, center.UnreadCount);
        Assert.All(center.Snapshot(), n => Assert.True(n.IsRead));
    }

    [Fact]
    public void Dismiss_removes_one()
    {
        var center = new NotificationCenter();
        var keep = center.Post("Keep", "")!;
        var drop = center.Post("Drop", "")!;

        Assert.True(center.Dismiss(drop.Id));
        Assert.False(center.Dismiss("missing"));
        Assert.Equal(keep.Id, center.Snapshot()[0].Id);
        Assert.Equal(1, center.Count);
    }

    [Fact]
    public void DismissAll_empties()
    {
        var center = new NotificationCenter();
        center.Post("A", "");
        center.Post("B", "");
        center.DismissAll();
        Assert.Equal(0, center.Count);
        Assert.Empty(center.Snapshot());
    }

    [Fact]
    public void Cap_drops_oldest()
    {
        var center = new NotificationCenter();
        for (var i = 0; i < NotificationCenter.MaxItems + 3; i++)
            center.Post("n" + i, "");

        Assert.Equal(NotificationCenter.MaxItems, center.Count);
        var snap = center.Snapshot();
        Assert.Equal("n" + (NotificationCenter.MaxItems + 2), snap[0].Title);
        Assert.Equal("n3", snap[^1].Title);
    }

    [Fact]
    public void PostOnce_dedupes_by_kind()
    {
        var center = new NotificationCenter();
        var first = center.PostOnce(NotificationKinds.OversizedWorld, "One", "a");
        var second = center.PostOnce(NotificationKinds.OversizedWorld, "Two", "b");
        Assert.NotNull(first);
        Assert.Null(second);
        Assert.Equal(1, center.Count);
        Assert.Equal("One", center.Snapshot()[0].Title);
        Assert.True(center.HasKind(NotificationKinds.OversizedWorld));
    }

    [Fact]
    public void DismissByKind_removes_matching_items()
    {
        var center = new NotificationCenter();
        center.Post("Keep", "", kind: "other");
        center.PostOnce(NotificationKinds.OversizedWorld, "Drop", "x");
        Assert.Equal(1, center.DismissByKind(NotificationKinds.OversizedWorld));
        Assert.Equal(1, center.Count);
        Assert.Equal("Keep", center.Snapshot()[0].Title);
        Assert.False(center.HasKind(NotificationKinds.OversizedWorld));
    }

    [Fact]
    public void Snapshot_is_a_copy()
    {
        var center = new NotificationCenter();
        center.Post("A", "");
        var snap = (AppNotification[])center.Snapshot();
        center.Post("B", "");
        Assert.Single(snap);
        Assert.Equal(2, center.Count);
    }

    private sealed class SteppingTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 18, 16, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            _now = _now.AddMinutes(1);
            return _now;
        }
    }
}
