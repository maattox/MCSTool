using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class OversizedWorldBackupUxTests
{
    [Fact]
    public void Fixture_flag_switches_download_to_ssh()
    {
        var blocked = new OversizedWorldBackupReadResult
        {
            Present = true,
            Document = OversizedWorldBackupDocument.CreateBlocked(
                archiveSizeBytes: 12L * 1024 * 1024 * 1024,
                softCapBytes: (long)(9.5 * 1024 * 1024 * 1024)),
        };

        Assert.True(OversizedWorldBackupUx.IsBlocked(blocked));
        Assert.True(OversizedWorldBackupUx.UseSshDownload(blocked));
        Assert.Contains("SSH", OversizedWorldBackupUx.DownloadLatestButtonLabel(true), StringComparison.Ordinal);
        Assert.Contains("SSH", OversizedWorldBackupUx.DownloadLatestTitle(true, vm1Running: true), StringComparison.Ordinal);
        Assert.Equal(
            OversizedWorldBackupUx.StartVmFirstMessage,
            OversizedWorldBackupUx.DownloadLatestTitle(true, vm1Running: false));
        Assert.Contains("paused", OversizedWorldBackupUx.Banner(blocked), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not uploaded", OversizedWorldBackupUx.Banner(blocked), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("12 GB", OversizedWorldBackupUx.Banner(blocked), StringComparison.Ordinal);
    }

    [Fact]
    public void Absent_flag_keeps_object_storage_download()
    {
        var absent = new OversizedWorldBackupReadResult { Present = false };
        Assert.False(OversizedWorldBackupUx.UseSshDownload(absent));
        Assert.False(OversizedWorldBackupUx.UseSshDownload(null));
        Assert.Equal("", OversizedWorldBackupUx.Banner(absent));
        Assert.Contains(
            "cloud storage",
            OversizedWorldBackupUx.DownloadLatestTitle(false, vm1Running: true),
            StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Download latest world save", OversizedWorldBackupUx.DownloadLatestButtonLabel(false));
    }

    [Fact]
    public void Vm1_running_is_lifecycle_RUNNING_only()
    {
        Assert.True(OversizedWorldBackupUx.Vm1IsRunning("RUNNING"));
        Assert.True(OversizedWorldBackupUx.Vm1IsRunning(" running "));
        Assert.False(OversizedWorldBackupUx.Vm1IsRunning("STOPPED"));
        Assert.False(OversizedWorldBackupUx.Vm1IsRunning("STARTING"));
        Assert.False(OversizedWorldBackupUx.Vm1IsRunning(""));
        Assert.False(OversizedWorldBackupUx.Vm1IsRunning(null));
    }

    [Fact]
    public void SyncBell_posts_once_when_blocked_and_dismisses_when_clear()
    {
        var notices = new NotificationCenter();
        var blocked = new OversizedWorldBackupReadResult
        {
            Present = true,
            Document = OversizedWorldBackupDocument.CreateBlocked(),
        };

        OversizedWorldBackupUx.SyncBell(notices, blocked);
        OversizedWorldBackupUx.SyncBell(notices, blocked);
        Assert.Equal(1, notices.Count);
        Assert.Equal(OversizedWorldBackupUx.NotificationTitle, notices.Snapshot()[0].Title);
        Assert.Equal(NotificationKinds.OversizedWorld, notices.Snapshot()[0].Kind);

        OversizedWorldBackupUx.SyncBell(notices, new OversizedWorldBackupReadResult { Present = false });
        Assert.Equal(0, notices.Count);
    }

    [Fact]
    public void Suggested_file_name_matches_world_stamp_zip()
    {
        var name = OversizedWorldBackupUx.SuggestedFileName(
            new DateTimeOffset(2026, 8, 18, 16, 30, 0, TimeSpan.Zero));
        Assert.Equal("world-20260818T163000Z.zip", name);
    }
}
