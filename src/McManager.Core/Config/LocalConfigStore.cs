using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Config;

/// <summary>
/// Loads gitignored operator seeds (<c>config.local.json</c>, <c>friends.local.json</c>).
/// From-source uses repo <c>data/</c>; an installed Manager uses
/// <c>%LOCALAPPDATA%\McManager</c>; <c>MCMANAGER_CONFIG_DIR</c> still wins.
/// </summary>
public static class LocalConfigStore
{
    public const string ConfigFileName = "config.local.json";
    public const string FriendsFileName = "friends.local.json";
    public const string WizardStateFileName = "setup-wizard.local.json";
    public const string ConfigDirEnvVar = "MCMANAGER_CONFIG_DIR";

    /// <summary>User-facing save failure when no writable settings folder can be resolved.</summary>
    public const string CannotWriteSettingsMessage =
        "Could not save Manager settings on this PC. Check that files can be written under Local App Data.";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string? TryFindDataDirectory()
    {
        var overrideDir = Environment.GetEnvironmentVariable(ConfigDirEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideDir))
        {
            var dataUnderOverride = Path.Combine(overrideDir, "data");
            if (Directory.Exists(dataUnderOverride))
                return dataUnderOverride;
            if (Directory.Exists(overrideDir))
                return overrideDir;
        }

        // Prefer an existing data/config.local.json while walking up from the binary / cwd.
        // McManager.slnx lives under src/ — do not treat that as the repo root or we create
        // src/data/ and miss the canonical repo-root data/ folder.
        string? solutionDataFallback = null;

        foreach (var start in CandidateStarts())
        {
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                var data = Path.Combine(dir.FullName, "data");
                var configPath = Path.Combine(data, ConfigFileName);
                if (File.Exists(configPath))
                    return data;

                // Product repo root (example configs). Prefer over src/ with only the .slnx.
                if (File.Exists(Path.Combine(dir.FullName, "config.local.example.json")))
                {
                    Directory.CreateDirectory(data);
                    return data;
                }

                if (solutionDataFallback is null
                    && File.Exists(Path.Combine(dir.FullName, "McManager.slnx")))
                {
                    solutionDataFallback = data;
                }

                dir = dir.Parent;
            }
        }

        if (solutionDataFallback is not null)
        {
            Directory.CreateDirectory(solutionDataFallback);
            return solutionDataFallback;
        }

        var installed = GetInstalledDataDirectory();
        if (string.IsNullOrWhiteSpace(installed))
            return null;

        Directory.CreateDirectory(installed);
        return installed;
    }

    /// <summary>
    /// Installed Manager settings folder (same directory as <c>app-settings.json</c>).
    /// Not a <c>data/</c> subfolder under the install dir.
    /// </summary>
    public static string GetInstalledDataDirectory()
    {
        if (InstalledDataDirectoryOverride is not null)
            return InstalledDataDirectoryOverride;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return string.IsNullOrWhiteSpace(local)
            ? ""
            : Path.Combine(local, AppSettingsStore.ProductFolderName);
    }

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = null,
    };

    public static string? GetFriendsFilePath(string? dataDirectory = null)
    {
        dataDirectory ??= TryFindDataDirectory();
        return dataDirectory is null
            ? null
            : Path.Combine(dataDirectory, FriendsFileName);
    }

    public static ServiceResult SaveFriends(FriendsLocalFile friends, string? dataDirectory = null)
    {
        dataDirectory ??= TryFindDataDirectory();
        if (dataDirectory is null)
            return ServiceResult.Fail(CannotWriteSettingsMessage);

        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, FriendsFileName);
            var json = JsonSerializer.Serialize(friends, JsonWriteOptions);
            File.WriteAllText(path, json);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Could not write Manager settings: {ex.Message}");
        }
    }

    public static ServiceResult SaveConfig(ManagerLocalConfig config, string? dataDirectory = null)
    {
        dataDirectory ??= TryFindDataDirectory();
        if (dataDirectory is null)
            return ServiceResult.Fail(CannotWriteSettingsMessage);

        try
        {
            Directory.CreateDirectory(dataDirectory);
            var path = Path.Combine(dataDirectory, ConfigFileName);
            var json = JsonSerializer.Serialize(config, JsonWriteOptions);
            File.WriteAllText(path, json);
            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Could not write Manager settings: {ex.Message}");
        }
    }

    /// <summary>
    /// True when <c>data/config.local.json</c> exists and parses. Used to skip the first-run
    /// Setup chooser so an existing manage stack is not hijacked on every launch.
    /// </summary>
    public static bool HasManageConfig()
    {
        var loaded = Load();
        return loaded.Succeeded && loaded.Config is not null;
    }

    /// <summary>Path to <c>config.local.json</c> if a data directory can be resolved.</summary>
    public static string? GetConfigFilePath(string? dataDirectory = null)
    {
        dataDirectory ??= TryFindDataDirectory();
        return dataDirectory is null
            ? null
            : Path.Combine(dataDirectory, ConfigFileName);
    }

    public static bool ConfigFileExists(string? dataDirectory = null)
    {
        var path = GetConfigFilePath(dataDirectory);
        return path is not null && File.Exists(path);
    }

    public static LocalConfigLoadResult Load()
    {
        var dataDir = TryFindDataDirectory();
        if (dataDir is null)
        {
            return LocalConfigLoadResult.Missing(
                "Could not find Manager settings on this PC.");
        }

        var configPath = Path.Combine(dataDir, ConfigFileName);
        if (!File.Exists(configPath))
        {
            return LocalConfigLoadResult.Missing($"Missing {configPath}.");
        }

        try
        {
            var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<ManagerLocalConfig>(json, JsonOptions)
                ?? throw new InvalidOperationException("Config JSON deserialized to null.");

            FriendsLocalFile? friends = null;
            var friendsPath = Path.Combine(dataDir, FriendsFileName);
            if (File.Exists(friendsPath))
            {
                var friendsJson = File.ReadAllText(friendsPath);
                friends = JsonSerializer.Deserialize<FriendsLocalFile>(friendsJson, JsonOptions);
            }

            var warnings = Validate(config);
            return LocalConfigLoadResult.Ok(config, friends, dataDir, warnings);
        }
        catch (Exception ex)
        {
            return LocalConfigLoadResult.Failed($"Failed to load {configPath}: {ex.Message}");
        }
    }

    /// <summary>Test hook: replace binary/cwd walk. Thread-local so parallel tests stay isolated.</summary>
    [ThreadStatic]
    internal static Func<IEnumerable<string>>? CandidateStartsOverride;

    /// <summary>
    /// Test hook: replace <c>%LOCALAPPDATA%\McManager</c>. Empty string disables the installed fallback.
    /// Thread-local so parallel tests stay isolated.
    /// </summary>
    [ThreadStatic]
    internal static string? InstalledDataDirectoryOverride;

    private static IEnumerable<string> CandidateStarts()
    {
        if (CandidateStartsOverride is not null)
        {
            foreach (var start in CandidateStartsOverride())
                yield return start;
            yield break;
        }

        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
    }

    public static string ExpandPath(string path)    {
        if (string.IsNullOrWhiteSpace(path))
            return path;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path
            .Replace("%USERPROFILE%", home, StringComparison.OrdinalIgnoreCase)
            .Replace("~", home, StringComparison.Ordinal);
    }

    public static IReadOnlyList<string> Validate(ManagerLocalConfig config)
    {
        var warnings = new List<string>();
        void Need(string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("REPLACE_ME", StringComparison.Ordinal))
                warnings.Add($"Missing or placeholder: {label}");
        }

        Need("oci.region", config.Oci.Region);
        Need("oci.compartment_id", config.Oci.CompartmentId);
        Need("oci.config_file", config.Oci.ConfigFile);
        Need("network.security_list_id", config.Network.SecurityListId);
        Need("vm1.instance_id", config.Vm1.InstanceId);
        Need("vm1.ssh_host", config.Vm1.SshHost);
        Need("vm1.ssh_key_path", config.Vm1.SshKeyPath);
        Need("door.instance_id", config.Door.InstanceId);
        Need("door.ssh_host", config.Door.SshHost);
        Need("play.reserved_public_ip", config.Play.ReservedPublicIp);
        Need("object_storage.namespace", config.ObjectStorage.Namespace);
        Need("object_storage.bucket", config.ObjectStorage.Bucket);

        var ociConfig = ExpandPath(config.Oci.ConfigFile);
        if (!string.IsNullOrWhiteSpace(ociConfig) && !File.Exists(ociConfig))
            warnings.Add($"OCI config file not found: {ociConfig}");

        var vm1Key = ExpandPath(config.Vm1.SshKeyPath);
        if (!string.IsNullOrWhiteSpace(vm1Key) && !File.Exists(vm1Key))
            warnings.Add($"VM1 SSH key not found: {vm1Key}");

        var doorKey = ExpandPath(config.Door.SshKeyPath);
        if (!string.IsNullOrWhiteSpace(doorKey) && !File.Exists(doorKey))
            warnings.Add($"Door SSH key not found: {doorKey}");

        return warnings;
    }

    /// <summary>
    /// Removes stack-local Manager files after a successful tofu destroy.
    /// Does not touch <c>friends.local.json</c>, <c>~/.oci</c>, SSH keys, or Credential Manager.
    /// </summary>
    public static ServiceResult DeleteManageConfigAndWizard(string? dataDirectory = null)
    {
        dataDirectory ??= TryFindDataDirectory();
        if (dataDirectory is null)
            return ServiceResult.Ok();

        var errors = new List<string>();
        TryDeleteFile(Path.Combine(dataDirectory, ConfigFileName), errors);
        TryDeleteFile(Path.Combine(dataDirectory, WizardStateFileName), errors);
        return errors.Count == 0
            ? ServiceResult.Ok()
            : ServiceResult.Fail(string.Join("; ", errors));
    }

    private static void TryDeleteFile(string path, List<string> errors)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
        }
    }
}

public sealed class LocalConfigLoadResult
{
    public bool Succeeded { get; private init; }
    public string? Error { get; private init; }
    public ManagerLocalConfig? Config { get; private init; }
    public FriendsLocalFile? Friends { get; private init; }
    public string? DataDirectory { get; private init; }
    public IReadOnlyList<string> Warnings { get; private init; } = [];

    public static LocalConfigLoadResult Ok(
        ManagerLocalConfig config,
        FriendsLocalFile? friends,
        string dataDirectory,
        IReadOnlyList<string> warnings) =>
        new()
        {
            Succeeded = true,
            Config = config,
            Friends = friends,
            DataDirectory = dataDirectory,
            Warnings = warnings,
        };

    public static LocalConfigLoadResult Missing(string message) =>
        new() { Succeeded = false, Error = message };

    public static LocalConfigLoadResult Failed(string message) =>
        new() { Succeeded = false, Error = message };
}
