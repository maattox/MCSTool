using McManager.Core.Config;

namespace McManager.PackTestHarness;

internal static class ConfigGuard
{
    public const string RequiredDirName = "mcmgr-pack-test";
    public const string InteractiveDirName = "mcmgr-blank-test";
    public const string RequiredProfile = "TESTING";

    public static string? TryAllow(out string dataDirectory, out ManagerLocalConfig? config)
    {
        dataDirectory = "";
        config = null;

        var env = Environment.GetEnvironmentVariable(LocalConfigStore.ConfigDirEnvVar);
        if (string.IsNullOrWhiteSpace(env))
        {
            return $"{LocalConfigStore.ConfigDirEnvVar} must point at {RequiredDirName} "
                + "(TESTING config.local.json copied from mcmgr-blank-test).";
        }

        var overrideDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(env.Trim()));
        if (HasDirName(overrideDir, InteractiveDirName))
        {
            return $"{LocalConfigStore.ConfigDirEnvVar} is {InteractiveDirName}. "
                + $"Copy TESTING config into {RequiredDirName} so Layer 2 stays isolated.";
        }

        if (!HasDirName(overrideDir, RequiredDirName))
        {
            return $"{LocalConfigStore.ConfigDirEnvVar} must resolve a folder named {RequiredDirName} "
                + $"(got {overrideDir}).";
        }

        if (!Directory.Exists(overrideDir))
        {
            return $"{LocalConfigStore.ConfigDirEnvVar} does not exist: {overrideDir}. "
                + $"Create {RequiredDirName} and copy TESTING config.local.json into it.";
        }

        var loaded = LocalConfigStore.Load();
        if (!loaded.Succeeded || loaded.Config is null || string.IsNullOrWhiteSpace(loaded.DataDirectory))
        {
            return loaded.Error
                ?? $"Could not load {LocalConfigStore.ConfigFileName} under {RequiredDirName}.";
        }

        dataDirectory = Path.GetFullPath(loaded.DataDirectory);
        if (!IsUnder(dataDirectory, overrideDir))
        {
            return "Resolved data directory is outside mcmgr-pack-test. "
                + "Refusing to use another config.local.json.";
        }

        if (IsRepoForgeDataDirectory(dataDirectory))
        {
            return "Refusing repo data/config.local.json (Forge / DEFAULT). "
                + $"Use {RequiredDirName} with TESTING.";
        }

        if (HasDirName(dataDirectory, InteractiveDirName))
        {
            return $"Resolved data directory is {InteractiveDirName}. Use {RequiredDirName}.";
        }

        if (!HasDirName(dataDirectory, RequiredDirName) && !HasDirName(overrideDir, RequiredDirName))
        {
            return $"Resolved data directory is not {RequiredDirName}.";
        }

        var profile = (loaded.Config.Oci.Profile ?? "").Trim();
        if (profile.Length == 0
            || string.Equals(profile, "DEFAULT", StringComparison.OrdinalIgnoreCase))
        {
            return "OCI profile is DEFAULT (live Forge lab). "
                + $"{RequiredDirName} must use TESTING.";
        }

        if (!string.Equals(profile, RequiredProfile, StringComparison.OrdinalIgnoreCase))
        {
            return $"OCI profile must be {RequiredProfile} (got {profile}).";
        }

        config = loaded.Config;
        return null;
    }

    internal static bool HasDirName(string path, string name)
    {
        var full = Path.GetFullPath(path);
        foreach (var part in full.Split(
                     new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (string.Equals(part, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    internal static bool IsRepoForgeDataDirectory(string dataDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(dataDirectory));
        if (!string.Equals(dir.Name, "data", StringComparison.OrdinalIgnoreCase))
            return false;
        var parent = dir.Parent;
        if (parent is null)
            return false;
        return File.Exists(Path.Combine(parent.FullName, "AGENTS.md"));
    }

    internal static bool IsUnder(string path, string parent)
    {
        var full = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(parent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            return true;
        var prefix = root + Path.DirectorySeparatorChar;
        return full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
