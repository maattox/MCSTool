using McManager.Core.Services;
using McManager.Core.Setup;

namespace McManager.Core.Config;

/// <summary>
/// Per-server folders under <c>%LOCALAPPDATA%\MCSTool\profiles\&lt;slug&gt;</c>
/// and the index in <c>app-settings.json</c>. UI word is server, not profile.
/// </summary>
public static class ServerCatalog
{
    public const string ProfilesFolderName = "profiles";
    public const string DefaultDisplayName = "New server";
    public const string EnvOverrideLabel = "Env override";

    public static bool HasEnvOverride =>
        !string.IsNullOrWhiteSpace(LocalConfigStore.ReadConfigDirEnv());

    public static string GetProfilesRoot()
    {
        var installed = LocalConfigStore.GetInstalledDataDirectory();
        return string.IsNullOrWhiteSpace(installed)
            ? ""
            : Path.Combine(installed, ProfilesFolderName);
    }

    public static string GetProfileDirectory(string slug)
    {
        var root = GetProfilesRoot();
        return string.IsNullOrWhiteSpace(root)
            ? ""
            : Path.Combine(root, slug);
    }

    public static IReadOnlyList<ServerIndexEntry> List()
    {
        var doc = AppSettingsStore.Load();
        return doc.Servers ?? [];
    }

    public static string? ActiveSlug()
    {
        var doc = AppSettingsStore.Load();
        return string.IsNullOrWhiteSpace(doc.ActiveServer) ? null : doc.ActiveServer.Trim();
    }

    public static ServerIndexEntry? ActiveEntry()
    {
        var slug = ActiveSlug();
        if (string.IsNullOrWhiteSpace(slug))
            return null;
        return List().FirstOrDefault(s =>
            string.Equals(s.Id, slug, StringComparison.OrdinalIgnoreCase));
    }

    public static string? ActiveDisplayName()
    {
        var name = ActiveEntry()?.DisplayName;
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

    /// <summary>Caption-bar label: display name, else play IP, else <see cref="DefaultDisplayName"/>.</summary>
    public static string CaptionLabel(string? playIpFallback)
    {
        if (HasEnvOverride)
            return EnvOverrideLabel;

        var name = ActiveDisplayName();
        if (!string.IsNullOrWhiteSpace(name))
            return name;

        var ip = (playIpFallback ?? "").Trim();
        if (ip.Length > 0 && ip != "—" && !ip.StartsWith("ocid1.", StringComparison.OrdinalIgnoreCase))
            return ip;

        return DefaultDisplayName;
    }

    public static string SuggestDisplayName(string? playIp)
    {
        var ip = (playIp ?? "").Trim();
        if (ip.Length > 0 && ip != "—" && !ip.StartsWith("ocid1.", StringComparison.OrdinalIgnoreCase))
            return ip;
        return DefaultDisplayName;
    }

    public static string AllocateSlug(string displayName, IReadOnlyCollection<string>? existingIds = null)
    {
        existingIds ??= List().Select(s => s.Id).ToList();
        var set = new HashSet<string>(existingIds, StringComparer.OrdinalIgnoreCase);
        var source = string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName.Trim();
        var baseSlug = TofuWorkspace.Sanitize(source);
        var slug = baseSlug;
        var n = 2;
        while (set.Contains(slug))
        {
            slug = baseSlug + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            n++;
        }

        return slug;
    }

    /// <summary>
    /// Creates the first <c>New server</c> folder when the index is empty.
    /// No-op when <c>MCMANAGER_CONFIG_DIR</c> is set.
    /// </summary>
    public static ServiceResult EnsureDefaultServer()
    {
        if (HasEnvOverride)
            return ServiceResult.Ok();

        var installed = LocalConfigStore.GetInstalledDataDirectory();
        if (string.IsNullOrWhiteSpace(installed))
            return ServiceResult.Fail(LocalConfigStore.CannotWriteSettingsMessage);

        var doc = AppSettingsStore.Load();
        doc.Servers ??= [];
        if (doc.Servers.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(doc.ActiveServer)
                || !doc.Servers.Any(s =>
                    string.Equals(s.Id, doc.ActiveServer, StringComparison.OrdinalIgnoreCase)))
            {
                doc.ActiveServer = doc.Servers[0].Id;
                var saved = AppSettingsStore.Save(doc);
                if (!saved.Succeeded)
                    return saved;
            }

            Directory.CreateDirectory(GetProfileDirectory(doc.ActiveServer!));
            return ServiceResult.Ok();
        }

        var slug = AllocateSlug(DefaultDisplayName, []);
        doc.Servers.Add(new ServerIndexEntry { Id = slug, DisplayName = DefaultDisplayName });
        doc.ActiveServer = slug;
        Directory.CreateDirectory(GetProfileDirectory(slug));
        return AppSettingsStore.Save(doc);
    }

    /// <summary>Profile folder for the active server, creating the default first server if needed.</summary>
    public static string? TryResolveProfileDirectory()
    {
        if (HasEnvOverride)
            return null;

        var ensure = EnsureDefaultServer();
        if (!ensure.Succeeded)
            return null;

        var slug = ActiveSlug();
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var dir = GetProfileDirectory(slug);
        if (string.IsNullOrWhiteSpace(dir))
            return null;

        Directory.CreateDirectory(dir);
        return dir;
    }

    public static ServiceResult AddServer(string displayName)
    {
        if (HasEnvOverride)
            return ServiceResult.Fail("Server switching is off while MCMANAGER_CONFIG_DIR is set.");

        var name = string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName.Trim();
        var doc = AppSettingsStore.Load();
        doc.Servers ??= [];
        var slug = AllocateSlug(name, doc.Servers.Select(s => s.Id).ToList());
        doc.Servers.Add(new ServerIndexEntry { Id = slug, DisplayName = name });
        doc.ActiveServer = slug;
        Directory.CreateDirectory(GetProfileDirectory(slug));
        return AppSettingsStore.Save(doc);
    }

    public static ServiceResult Rename(string slug, string displayName)
    {
        if (HasEnvOverride)
            return ServiceResult.Fail("Server switching is off while MCMANAGER_CONFIG_DIR is set.");

        var name = string.IsNullOrWhiteSpace(displayName) ? DefaultDisplayName : displayName.Trim();
        var doc = AppSettingsStore.Load();
        var entry = doc.Servers?.FirstOrDefault(s =>
            string.Equals(s.Id, slug, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
            return ServiceResult.Fail("That server is not on this PC.");

        entry.DisplayName = name;
        return AppSettingsStore.Save(doc);
    }

    public static ServiceResult SetActive(string slug)
    {
        if (HasEnvOverride)
            return ServiceResult.Fail("Server switching is off while MCMANAGER_CONFIG_DIR is set.");

        var doc = AppSettingsStore.Load();
        if (doc.Servers is null
            || !doc.Servers.Any(s => string.Equals(s.Id, slug, StringComparison.OrdinalIgnoreCase)))
        {
            return ServiceResult.Fail("That server is not on this PC.");
        }

        doc.ActiveServer = slug;
        Directory.CreateDirectory(GetProfileDirectory(slug));
        return AppSettingsStore.Save(doc);
    }

    /// <summary>After destroy: another indexed server that still has <c>config.local.json</c>.</summary>
    public static string? TryFindOtherServerWithManageConfig(string? exceptSlug)
    {
        foreach (var entry in List())
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
                continue;
            if (!string.IsNullOrWhiteSpace(exceptSlug)
                && string.Equals(entry.Id, exceptSlug, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var dir = GetProfileDirectory(entry.Id);
            if (!string.IsNullOrWhiteSpace(dir) && LocalConfigStore.ConfigFileExists(dir))
                return entry.Id;
        }

        return null;
    }

    /// <summary>
    /// True when this is an extra empty server (Add from Advanced), not the only first-run folder.
    /// </summary>
    public static bool CanDiscardCurrentEmptyServer()
    {
        if (HasEnvOverride)
            return false;
        var slug = ActiveSlug();
        if (string.IsNullOrWhiteSpace(slug))
            return false;
        if (List().Count < 2)
            return false;
        var dir = GetProfileDirectory(slug);
        return !string.IsNullOrWhiteSpace(dir) && !LocalConfigStore.ConfigFileExists(dir);
    }

    /// <summary>
    /// Removes the active empty server from the index, deletes its folder, and switches to another.
    /// Refuses if <c>config.local.json</c> is present.
    /// </summary>
    public static ServiceResult DiscardCurrentEmptyServer()
    {
        if (HasEnvOverride)
            return ServiceResult.Fail("Server switching is off while MCMANAGER_CONFIG_DIR is set.");

        var slug = ActiveSlug();
        if (string.IsNullOrWhiteSpace(slug))
            return ServiceResult.Fail("There is no server to cancel.");
        if (List().Count < 2)
            return ServiceResult.Fail("Cancel is only for an extra new server, not the first folder on this PC.");

        var dir = GetProfileDirectory(slug);
        var root = GetProfilesRoot();
        if (string.IsNullOrWhiteSpace(dir) || string.IsNullOrWhiteSpace(root))
            return ServiceResult.Fail(LocalConfigStore.CannotWriteSettingsMessage);

        var fullDir = Path.GetFullPath(dir);
        var fullRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        if (!fullDir.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            return ServiceResult.Fail("Could not cancel: the server folder is not under this PC's profiles directory.");

        if (LocalConfigStore.ConfigFileExists(dir))
            return ServiceResult.Fail("This server already has manage config. Cancel only removes a new empty folder.");

        var returnTo = TryFindOtherServerWithManageConfig(slug)
            ?? List().FirstOrDefault(s =>
                !string.Equals(s.Id, slug, StringComparison.OrdinalIgnoreCase))?.Id;

        var doc = AppSettingsStore.Load();
        doc.Servers ??= [];
        doc.Servers.RemoveAll(s => string.Equals(s.Id, slug, StringComparison.OrdinalIgnoreCase));
        doc.ActiveServer = returnTo;
        var saved = AppSettingsStore.Save(doc);
        if (!saved.Succeeded)
            return saved;

        try
        {
            if (Directory.Exists(fullDir))
                Directory.Delete(fullDir, recursive: true);
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"Removed the server from the list, but could not delete its folder: {ex.Message}");
        }

        return ServiceResult.Ok();
    }
}
