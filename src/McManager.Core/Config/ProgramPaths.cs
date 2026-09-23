using McManager.Core.Setup;

namespace McManager.Core.Config;

/// <summary>Read-only paths shown in program settings (gear). No secrets.</summary>
public sealed record ProgramPathItem(
    string Id,
    string Label,
    string Hint,
    string Path,
    bool Exists);

public static class ProgramPaths
{
    public const string GitHubUrl = "https://github.com/maattox/MCSTool";

    public static IReadOnlyList<ProgramPathItem> Describe(
        string? dataDirectory,
        string? ociConfigFile = null)
    {
        var rows = new List<ProgramPathItem>(4);

        Add(
            rows,
            "data",
            "MCSTool data folder",
            "Server settings, player list, and imported modpacks on this PC.",
            dataDirectory);

        var config = string.IsNullOrWhiteSpace(dataDirectory)
            ? null
            : Path.Combine(dataDirectory, LocalConfigStore.ConfigFileName);
        Add(
            rows,
            "config",
            "Server settings file",
            "Settings for the server this PC is connected to.",
            config);

        Add(
            rows,
            "tofu",
            "Setup files",
            "Files Setup uses to create and delete this server in Oracle Cloud.",
            string.IsNullOrWhiteSpace(dataDirectory)
                ? TofuWorkspace.TofuRootDirectory()
                : Path.Combine(dataDirectory, "tofu"));

        var oci = ResolveOciConfigPath(ociConfigFile);
        Add(
            rows,
            "oci",
            "Oracle API config",
            "Used by MCSTool to sign in to Oracle. This is not the SSH key.",
            oci);

        return rows;
    }

    public static string ResolveOciConfigPath(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
            return LocalConfigStore.ExpandPath(configured.Trim());

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".oci", "config");
    }

    public static string? ConfigDirOverride => LocalConfigStore.ReadConfigDirEnv();

    private static void Add(
        List<ProgramPathItem> rows,
        string id,
        string label,
        string hint,
        string? path)
    {
        var value = string.IsNullOrWhiteSpace(path) ? "" : path;
        var exists = value.Length > 0 && (File.Exists(value) || Directory.Exists(value));
        rows.Add(new ProgramPathItem(id, label, hint, value, exists));
    }
}
