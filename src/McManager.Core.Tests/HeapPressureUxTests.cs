using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class HeapPressureUxTests
{
    [Fact]
    public void SyncBell_posts_once_when_pressured_and_dismisses_when_clear()
    {
        var notices = new NotificationCenter();
        var pressured = new HeapPressureReadResult
        {
            Present = true,
            Document = HeapPressureDocument.CreatePressure("8G", 24),
        };

        HeapPressureUx.SyncBell(notices, pressured);
        HeapPressureUx.SyncBell(notices, pressured);
        Assert.Equal(1, notices.Count);
        Assert.Equal(HeapPressureUx.NotificationTitle, notices.Snapshot()[0].Title);
        Assert.Equal(NotificationKinds.HeapPressure, notices.Snapshot()[0].Kind);
        Assert.Contains("server memory", notices.Snapshot()[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("heap", notices.Snapshot()[0].Title, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("heap", notices.Snapshot()[0].Body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("10G", notices.Snapshot()[0].Body, StringComparison.Ordinal);
        Assert.Contains("Advanced → Danger Zone", notices.Snapshot()[0].Body, StringComparison.Ordinal);
        Assert.Contains("more server memory", notices.Snapshot()[0].Body, StringComparison.Ordinal);

        HeapPressureUx.SyncBell(notices, new HeapPressureReadResult { Present = false });
        Assert.Equal(0, notices.Count);
    }

    [Fact]
    public void Body_at_12GB_host_max_mentions_24GB_size()
    {
        var doc = HeapPressureDocument.CreatePressure("8G", 12);
        Assert.True(doc.AtHostMax);
        Assert.Null(doc.SuggestedHeap);
        var body = HeapPressureUx.NotificationBody(new HeapPressureReadResult
        {
            Present = true,
            Document = doc,
        });
        Assert.Contains("24 GB", body, StringComparison.Ordinal);
        Assert.DoesNotContain("heap", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("server memory", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Body_at_24GB_host_max_does_not_offer_illegal_size()
    {
        var doc = HeapPressureDocument.CreatePressure("12G", 24);
        Assert.True(doc.AtHostMax);
        var body = HeapPressureUx.NotificationBody(new HeapPressureReadResult
        {
            Present = true,
            Document = doc,
        });
        Assert.DoesNotContain("14G", body, StringComparison.Ordinal);
        Assert.DoesNotContain("16G", body, StringComparison.Ordinal);
        Assert.DoesNotContain("heap", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("maximum", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShouldClearOnApply_needs_suggested_or_larger_than_current()
    {
        var pressured = new HeapPressureReadResult
        {
            Present = true,
            Document = HeapPressureDocument.CreatePressure("8G", 24),
        };

        Assert.False(HeapPressureUx.ShouldClearOnApply(pressured, "8G"));
        Assert.False(HeapPressureUx.ShouldClearOnApply(pressured, "6G"));
        Assert.True(HeapPressureUx.ShouldClearOnApply(pressured, "10G"));
        Assert.True(HeapPressureUx.ShouldClearOnApply(pressured, "12G"));
        Assert.False(HeapPressureUx.ShouldClearOnApply(
            new HeapPressureReadResult { Present = false }, "10G"));
    }

    [Fact]
    public void ShouldClearOnApply_malformed_present_clears_on_legal_apply()
    {
        var malformed = new HeapPressureReadResult { Present = true };
        Assert.True(HeapPressureUx.ShouldClearOnApply(malformed, "6G"));
        Assert.False(HeapPressureUx.ShouldClearOnApply(malformed, "16G"));
    }
}
