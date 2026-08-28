using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class LocalConfigStoreTests
{
    [Fact]
    public void Installed_layout_saves_and_loads_without_repo_markers()
    {
        var walk = NewTempDir("mcmgr-cfg-walk-");
        var installed = NewTempDir("mcmgr-cfg-installed-");
        using (Isolate(configDirEnv: null, candidateStarts: [walk], installed: installed))
        {
            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Pat" });
            Assert.True(saved.Succeeded, saved.Error);

            var configPath = Path.Combine(installed, LocalConfigStore.ConfigFileName);
            Assert.True(File.Exists(configPath));
            Assert.False(Directory.Exists(Path.Combine(installed, "data")));
            Assert.False(File.Exists(Path.Combine(walk, "data", LocalConfigStore.ConfigFileName)));

            var loaded = LocalConfigStore.Load();
            Assert.True(loaded.Succeeded, loaded.Error);
            Assert.Equal("Pat", loaded.Config!.AdminName);
            Assert.Equal(installed, loaded.DataDirectory);
            Assert.True(LocalConfigStore.HasManageConfig());

            var wizard = SetupWizardStore.Save(new SetupWizardState { CurrentStep = 1 });
            Assert.True(wizard.Succeeded, wizard.Error);
            Assert.True(File.Exists(Path.Combine(installed, LocalConfigStore.WizardStateFileName)));

            var friends = LocalConfigStore.SaveFriends(new FriendsLocalFile
            {
                Friends = [new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10", IsAdmin = true }],
            });
            Assert.True(friends.Succeeded, friends.Error);
            Assert.True(File.Exists(Path.Combine(installed, LocalConfigStore.FriendsFileName)));
        }
    }

    [Fact]
    public void Env_override_wins_over_repo_and_installed()
    {
        var overrideDir = NewTempDir("mcmgr-cfg-env-");
        var repo = NewTempDir("mcmgr-cfg-repo-");
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "x");
        var installed = NewTempDir("mcmgr-cfg-inst-");
        using (Isolate(configDirEnv: overrideDir, candidateStarts: [repo], installed: installed))
        {
            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Env" });
            Assert.True(saved.Succeeded, saved.Error);
            Assert.True(File.Exists(Path.Combine(overrideDir, LocalConfigStore.ConfigFileName)));
            Assert.False(File.Exists(Path.Combine(installed, LocalConfigStore.ConfigFileName)));
            Assert.False(File.Exists(Path.Combine(repo, "data", LocalConfigStore.ConfigFileName)));

            var loaded = LocalConfigStore.Load();
            Assert.True(loaded.Succeeded, loaded.Error);
            Assert.Equal("Env", loaded.Config!.AdminName);
            Assert.Equal(overrideDir, loaded.DataDirectory);
        }
    }

    [Fact]
    public void Repo_root_markers_use_data_subdirectory()
    {
        var repo = NewTempDir("mcmgr-cfg-agents-");
        File.WriteAllText(Path.Combine(repo, "AGENTS.md"), "x");
        var installed = NewTempDir("mcmgr-cfg-inst-");
        using (Isolate(configDirEnv: null, candidateStarts: [repo], installed: installed))
        {
            var found = LocalConfigStore.TryFindDataDirectory();
            var expected = Path.Combine(repo, "data");
            Assert.Equal(expected, found);
            Assert.True(Directory.Exists(expected));

            var saved = LocalConfigStore.SaveConfig(new ManagerLocalConfig { AdminName = "Repo" });
            Assert.True(saved.Succeeded, saved.Error);
            Assert.True(File.Exists(Path.Combine(expected, LocalConfigStore.ConfigFileName)));
            Assert.False(File.Exists(Path.Combine(installed, LocalConfigStore.ConfigFileName)));

            var loaded = LocalConfigStore.Load();
            Assert.True(loaded.Succeeded, loaded.Error);
            Assert.Equal("Repo", loaded.Config!.AdminName);
        }
    }

    [Fact]
    public void Save_failure_does_not_mention_developer_env()
    {
        var walk = NewTempDir("mcmgr-cfg-none-");
        using (Isolate(configDirEnv: null, candidateStarts: [walk], installed: ""))
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

    private static IsolatedFinder Isolate(
        string? configDirEnv,
        IEnumerable<string> candidateStarts,
        string? installed) =>
        new(configDirEnv, candidateStarts, installed);

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
        private readonly List<string> _dirs;

        public IsolatedFinder(
            string? configDirEnv,
            IEnumerable<string> candidateStarts,
            string? installed)
        {
            _dirs = candidateStarts.ToList();
            if (!string.IsNullOrWhiteSpace(configDirEnv))
                _dirs.Add(configDirEnv);
            if (installed is { Length: > 0 })
                _dirs.Add(installed);
            _previousEnv = Environment.GetEnvironmentVariable(LocalConfigStore.ConfigDirEnvVar);
            Environment.SetEnvironmentVariable(LocalConfigStore.ConfigDirEnvVar, configDirEnv);
            var starts = candidateStarts.ToArray();
            LocalConfigStore.CandidateStartsOverride = () => starts;
            LocalConfigStore.InstalledDataDirectoryOverride = installed;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(LocalConfigStore.ConfigDirEnvVar, _previousEnv);
            LocalConfigStore.CandidateStartsOverride = null;
            LocalConfigStore.InstalledDataDirectoryOverride = null;
            foreach (var dir in _dirs.Distinct(StringComparer.OrdinalIgnoreCase))
                TryDeleteDir(dir);
        }
    }
}
