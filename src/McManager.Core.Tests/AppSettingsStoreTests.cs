using McManager.Core.Config;
using Xunit;

namespace McManager.Core.Tests;

public sealed class AppSettingsStoreTests
{
    [Fact]
    public void Missing_file_defaults_update_check_on()
    {
        var path = Path.Combine(NewTempDir(), "missing.json");
        try
        {
            var doc = AppSettingsStore.Load(path);
            Assert.True(doc.CheckForUpdates);
            Assert.Equal(1, doc.Version);
            Assert.False(File.Exists(path));
        }
        finally
        {
            TryDeleteDir(Path.GetDirectoryName(path)!);
        }
    }

    [Fact]
    public void Save_then_load_round_trips_the_toggle()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, AppSettingsStore.FileName);
        try
        {
            var saved = AppSettingsStore.Save(
                new AppSettingsDocument { CheckForUpdates = false },
                path);
            Assert.True(saved.Succeeded, saved.Error);
            Assert.True(File.Exists(path));

            var doc = AppSettingsStore.Load(path);
            Assert.False(doc.CheckForUpdates);
            Assert.Equal(AppSettingsDocument.DocumentVersion, doc.Version);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Malformed_json_returns_defaults()
    {
        var dir = NewTempDir();
        var path = Path.Combine(dir, AppSettingsStore.FileName);
        try
        {
            File.WriteAllText(path, "not-json");
            var doc = AppSettingsStore.Load(path);
            Assert.True(doc.CheckForUpdates);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Default_file_path_is_under_local_app_data()
    {
        var path = AppSettingsStore.DefaultFilePath();
        Assert.EndsWith(
            Path.Combine("McManager", "app-settings.json"),
            path,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-appsettings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
