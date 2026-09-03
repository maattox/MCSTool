using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class LocalConfigStoreTests
{
    [Fact]
    public void Installed_layout_saves_under_profiles_not_product_root()
    {
        var installed = NewTempDir("mcmgr-cfg-installed-");
        using (Isolate(configDirEnv: null, installed: installed))
        {
            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Pat" });
            Assert.True(saved.Succeeded, saved.Error);

            var dataDir = LocalConfigStore.TryFindDataDirectory();
            Assert.NotNull(dataDir);
            Assert.Equal(
                Path.Combine(installed, ServerCatalog.ProfilesFolderName),
                Path.GetDirectoryName(dataDir));
            Assert.True(File.Exists(Path.Combine(dataDir!, LocalConfigStore.ConfigFileName)));
            Assert.False(File.Exists(Path.Combine(installed, LocalConfigStore.ConfigFileName)));
            Assert.False(Directory.Exists(Path.Combine(installed, "data")));

            var loaded = LocalConfigStore.Load();
            Assert.True(loaded.Succeeded, loaded.Error);
            Assert.Equal("Pat", loaded.Config!.AdminName);
            Assert.Equal(dataDir, loaded.DataDirectory);
            Assert.True(LocalConfigStore.HasManageConfig());

            var wizard = SetupWizardStore.Save(new SetupWizardState { CurrentStep = 1 });
            Assert.True(wizard.Succeeded, wizard.Error);
            Assert.True(File.Exists(Path.Combine(dataDir!, LocalConfigStore.WizardStateFileName)));

            var friends = LocalConfigStore.SaveFriends(new FriendsLocalFile
            {
                Friends = [new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10", IsAdmin = true }],
            });
            Assert.True(friends.Succeeded, friends.Error);
            Assert.True(File.Exists(Path.Combine(dataDir!, LocalConfigStore.FriendsFileName)));

            Assert.Equal(Path.Combine(dataDir!, "tofu"), TofuWorkspace.TofuRootDirectory());
            Assert.NotEqual(Path.Combine(installed, "tofu"), TofuWorkspace.TofuRootDirectory());

            var settings = AppSettingsStore.Load();
            Assert.NotEmpty(settings.Servers);
            Assert.False(string.IsNullOrWhiteSpace(settings.ActiveServer));
        }
    }

    [Fact]
    public void Env_override_wins_over_profiles_and_is_flat()
    {
        var overrideDir = NewTempDir("mcmgr-cfg-env-");
        var installed = NewTempDir("mcmgr-cfg-inst-");
        using (Isolate(configDirEnv: overrideDir, installed: installed))
        {
            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Env" });
            Assert.True(saved.Succeeded, saved.Error);
            Assert.True(File.Exists(Path.Combine(overrideDir, LocalConfigStore.ConfigFileName)));
            Assert.False(File.Exists(Path.Combine(installed, LocalConfigStore.ConfigFileName)));
            Assert.False(Directory.Exists(Path.Combine(installed, ServerCatalog.ProfilesFolderName)));

            var loaded = LocalConfigStore.Load();
            Assert.True(loaded.Succeeded, loaded.Error);
            Assert.Equal("Env", loaded.Config!.AdminName);
            Assert.Equal(overrideDir, loaded.DataDirectory);

            Assert.Equal(
                Path.Combine(overrideDir, "tofu"),
                TofuWorkspace.TofuRootDirectory());
        }
    }

    [Fact]
    public void Repo_root_markers_are_ignored()
    {
        var repo = NewTempDir("mcmgr-cfg-example-");
        File.WriteAllText(Path.Combine(repo, "config.local.example.json"), "{}");
        Directory.CreateDirectory(Path.Combine(repo, "data"));
        File.WriteAllText(Path.Combine(repo, "data", LocalConfigStore.ConfigFileName), """{"admin_name":"Repo"}""");
        var installed = NewTempDir("mcmgr-cfg-inst-");
        using (Isolate(configDirEnv: null, installed: installed))
        {
            var found = LocalConfigStore.TryFindDataDirectory();
            Assert.NotNull(found);
            Assert.StartsWith(
                Path.Combine(installed, ServerCatalog.ProfilesFolderName),
                found,
                StringComparison.OrdinalIgnoreCase);
            Assert.NotEqual(Path.Combine(repo, "data"), found);

            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Installed" });
            Assert.True(saved.Succeeded, saved.Error);
            Assert.True(File.Exists(Path.Combine(found!, LocalConfigStore.ConfigFileName)));
            Assert.Contains("\"admin_name\":\"Repo\"", File.ReadAllText(Path.Combine(repo, "data", LocalConfigStore.ConfigFileName)));
        }
    }

    [Fact]
    public void Save_failure_does_not_mention_developer_env()
    {
        using (Isolate(configDirEnv: null, installed: ""))
        {
            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig());
            Assert.False(saved.Succeeded);
            Assert.Equal(LocalConfigStore.CannotWriteSettingsMessage, saved.Error);
            Assert.DoesNotContain(
                LocalConfigStore.ConfigDirEnvVar,
                saved.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain("repo", saved.Error, StringComparison.OrdinalIgnoreCase);

            var wizard = SetupWizardStore.Save(new SetupWizardState());
            Assert.False(wizard.Succeeded);
            Assert.Equal(LocalConfigStore.CannotWriteSettingsMessage, wizard.Error);
            Assert.DoesNotContain(
                LocalConfigStore.ConfigDirEnvVar,
                wizard.Error,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Installed_folder_matches_app_settings_parent()
    {
        var path = LocalConfigStore.GetInstalledDataDirectory();
        Assert.Equal(
            Path.GetDirectoryName(AppSettingsStore.DefaultFilePath()),
            path);
        Assert.EndsWith(
            Path.DirectorySeparatorChar + AppSettingsStore.ProductFolderName,
            path.TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static IsolatedFinder Isolate(string? configDirEnv, string? installed) =>
        new(configDirEnv, installed);

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
            // best-effort cleanup
        }
    }

    private sealed class IsolatedFinder : IDisposable
    {
        private readonly string? _previousEnv;
        private readonly List<string> _dirs = [];

        public IsolatedFinder(string? configDirEnv, string? installed)
        {
            if (!string.IsNullOrWhiteSpace(configDirEnv))
                _dirs.Add(configDirEnv);
            if (installed is { Length: > 0 })
                _dirs.Add(installed);
            _previousEnv = LocalConfigStore.ConfigDirEnvOverride;
            LocalConfigStore.ConfigDirEnvOverride = configDirEnv ?? "";
            LocalConfigStore.InstalledDataDirectoryOverride = installed;
        }

        public void Dispose()
        {
            LocalConfigStore.ConfigDirEnvOverride = _previousEnv;
            LocalConfigStore.InstalledDataDirectoryOverride = null;
            foreach (var dir in _dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                TryDeleteDir(dir);
        }
    }
}
