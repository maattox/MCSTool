using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ServerCatalogTests
{
    [Fact]
    public void Add_switch_and_rename_keep_folders_separate()
    {
        var installed = NewTempDir("mcmgr-srv-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.EnsureDefaultServer().Succeeded);
            var first = ServerCatalog.ActiveSlug();
            Assert.False(string.IsNullOrWhiteSpace(first));
            var firstDir = ServerCatalog.GetProfileDirectory(first!);
            Assert.True(Directory.Exists(firstDir));

            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "A" }).Succeeded);
            Assert.True(File.Exists(Path.Combine(firstDir, LocalConfigStore.ConfigFileName)));

            var add = ServerCatalog.AddServer("Lab two");
            Assert.True(add.Succeeded, add.Error);
            var second = ServerCatalog.ActiveSlug();
            Assert.NotEqual(first, second);
            var secondDir = ServerCatalog.GetProfileDirectory(second!);
            Assert.NotEqual(firstDir, secondDir);
            Assert.False(File.Exists(Path.Combine(secondDir, LocalConfigStore.ConfigFileName)));

            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "B" }).Succeeded);
            Assert.Equal("B", LocalConfigStore.Load().Config!.AdminName);

            Assert.True(ServerCatalog.SetActive(first!).Succeeded);
            Assert.Equal("A", LocalConfigStore.Load().Config!.AdminName);

            Assert.True(ServerCatalog.Rename(first!, "Home").Succeeded);
            Assert.Equal("Home", ServerCatalog.ActiveDisplayName());
            Assert.Equal("Home", ServerCatalog.CaptionLabel(playIpFallback: "203.0.113.9"));
        }
    }

    [Fact]
    public void Duplicate_names_get_unique_slugs()
    {
        var installed = NewTempDir("mcmgr-slug-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.AddServer("New server").Succeeded);
            var a = ServerCatalog.ActiveSlug();
            Assert.True(ServerCatalog.AddServer("New server").Succeeded);
            var b = ServerCatalog.ActiveSlug();
            Assert.NotEqual(a, b, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(2, ServerCatalog.List().Count);
        }
    }

    [Fact]
    public void Suggest_display_name_prefers_play_ip()
    {
        Assert.Equal("203.0.113.10", ServerCatalog.SuggestDisplayName("203.0.113.10"));
        Assert.Equal(ServerCatalog.DefaultDisplayName, ServerCatalog.SuggestDisplayName(""));
        Assert.Equal(ServerCatalog.DefaultDisplayName, ServerCatalog.SuggestDisplayName("—"));
    }

    [Fact]
    public void Env_override_hides_catalog_writes()
    {
        var overrideDir = NewTempDir("mcmgr-env-");
        var installed = NewTempDir("mcmgr-inst-");
        var previousEnv = LocalConfigStore.ConfigDirEnvOverride;
        var previousInstalled = LocalConfigStore.InstalledDataDirectoryOverride;
        LocalConfigStore.InstalledDataDirectoryOverride = installed;
        LocalConfigStore.ConfigDirEnvOverride = overrideDir;
        try
        {
            Assert.True(ServerCatalog.HasEnvOverride);
            Assert.Equal(ServerCatalog.EnvOverrideLabel, ServerCatalog.CaptionLabel("1.2.3.4"));
            var add = ServerCatalog.AddServer("Nope");
            Assert.False(add.Succeeded);
            Assert.Empty(ServerCatalog.List());
        }
        finally
        {
            LocalConfigStore.ConfigDirEnvOverride = previousEnv;
            LocalConfigStore.InstalledDataDirectoryOverride = previousInstalled;
            TryDeleteDir(overrideDir);
            TryDeleteDir(installed);
        }
    }

    [Fact]
    public void Other_server_with_config_is_found_after_destroy()
    {
        var installed = NewTempDir("mcmgr-other-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.AddServer("One").Succeeded);
            var one = ServerCatalog.ActiveSlug();
            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "One" }).Succeeded);

            Assert.True(ServerCatalog.AddServer("Two").Succeeded);
            var two = ServerCatalog.ActiveSlug();
            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Two" }).Succeeded);

            Assert.Equal(one, ServerCatalog.TryFindOtherServerWithManageConfig(two));
            Assert.Equal(two, ServerCatalog.TryFindOtherServerWithManageConfig(one));
        }
    }

    [Fact]
    public void App_settings_round_trips_server_index()
    {
        var dir = NewTempDir("mcmgr-app-idx-");
        var path = Path.Combine(dir, AppSettingsStore.FileName);
        try
        {
            var saved = AppSettingsStore.Save(
                new AppSettingsDocument
                {
                    CheckForUpdates = false,
                    ActiveServer = "lab",
                    Servers = [new ServerIndexEntry { Id = "lab", DisplayName = "Lab" }],
                },
                path);
            Assert.True(saved.Succeeded, saved.Error);
            var doc = AppSettingsStore.Load(path);
            Assert.False(doc.CheckForUpdates);
            Assert.Equal("lab", doc.ActiveServer);
            Assert.Single(doc.Servers);
            Assert.Equal("Lab", doc.Servers[0].DisplayName);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Discard_empty_server_restores_the_other_folder()
    {
        var installed = NewTempDir("mcmgr-discard-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.AddServer("Keep").Succeeded);
            var keep = ServerCatalog.ActiveSlug();
            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Keep" }).Succeeded);
            Assert.False(ServerCatalog.CanDiscardCurrentEmptyServer());

            Assert.True(ServerCatalog.AddServer("Scratch").Succeeded);
            var scratch = ServerCatalog.ActiveSlug();
            var scratchDir = ServerCatalog.GetProfileDirectory(scratch!);
            Assert.True(Directory.Exists(scratchDir));
            File.WriteAllText(Path.Combine(scratchDir, LocalConfigStore.WizardStateFileName), "{}");
            Assert.True(ServerCatalog.CanDiscardCurrentEmptyServer());

            var discarded = ServerCatalog.DiscardCurrentEmptyServer();
            Assert.True(discarded.Succeeded, discarded.Error);
            Assert.Equal(keep, ServerCatalog.ActiveSlug());
            Assert.False(Directory.Exists(scratchDir));
            Assert.DoesNotContain(ServerCatalog.List(), s => s.Id == scratch);
            Assert.Equal("Keep", LocalConfigStore.Load().Config!.AdminName);
            Assert.False(ServerCatalog.CanDiscardCurrentEmptyServer());
        }
    }

    [Fact]
    public void Discard_refuses_the_only_empty_server()
    {
        var installed = NewTempDir("mcmgr-discard-only-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.EnsureDefaultServer().Succeeded);
            var slug = ServerCatalog.ActiveSlug();
            var dir = ServerCatalog.GetProfileDirectory(slug!);
            File.WriteAllText(Path.Combine(dir, "scratch.txt"), "keep");
            Assert.False(ServerCatalog.CanDiscardCurrentEmptyServer());
            var discarded = ServerCatalog.DiscardCurrentEmptyServer();
            Assert.False(discarded.Succeeded);
            Assert.True(Directory.Exists(dir));
            Assert.True(File.Exists(Path.Combine(dir, "scratch.txt")));
        }
    }

    [Fact]
    public void Discard_refuses_when_manage_config_exists()
    {
        var installed = NewTempDir("mcmgr-discard-cfg-");
        using (Isolate(installed))
        {
            Assert.True(ServerCatalog.AddServer("Keep").Succeeded);
            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Keep" }).Succeeded);
            Assert.True(ServerCatalog.AddServer("Other").Succeeded);
            Assert.True(LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Other" }).Succeeded);
            Assert.False(ServerCatalog.CanDiscardCurrentEmptyServer());
            var slug = ServerCatalog.ActiveSlug();
            var dir = ServerCatalog.GetProfileDirectory(slug!);
            var discarded = ServerCatalog.DiscardCurrentEmptyServer();
            Assert.False(discarded.Succeeded);
            Assert.True(LocalConfigStore.ConfigFileExists(dir));
            Assert.Contains(ServerCatalog.List(), s => s.Id == slug);
        }
    }

    private static IsolatedCatalog Isolate(string installed) => new(installed);

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), prefix + Guid.NewGuid().ToString("N"));
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
            // best-effort
        }
    }

    private sealed class IsolatedCatalog : IDisposable
    {
        private readonly string _installed;
        private readonly string? _previousEnv;

        public IsolatedCatalog(string installed)
        {
            _installed = installed;
            _previousEnv = LocalConfigStore.ConfigDirEnvOverride;
            LocalConfigStore.ConfigDirEnvOverride = "";
            LocalConfigStore.InstalledDataDirectoryOverride = installed;
        }

        public void Dispose()
        {
            LocalConfigStore.ConfigDirEnvOverride = _previousEnv;
            LocalConfigStore.InstalledDataDirectoryOverride = null;
            TryDeleteDir(_installed);
        }
    }
}
