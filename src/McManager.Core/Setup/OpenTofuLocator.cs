namespace McManager.Core.Setup;

/// <summary>Finds a local OpenTofu binary. Does not download (installer / Phase 7).</summary>
public static class OpenTofuLocator
{
    public static string? Find()
    {
        var fromPath = FindOnPath("tofu.exe") ?? FindOnPath("tofu");
        if (fromPath is not null)
            return fromPath;

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var winget = Path.Combine(local, "Microsoft", "WinGet", "Links", "tofu.exe");
        if (File.Exists(winget))
            return winget;

        var bundled = Path.Combine(local, "McManager", "tofu", "tofu.exe");
        if (File.Exists(bundled))
            return bundled;

        return null;
    }

    public static string MissingMessage() =>
        "OpenTofu (tofu.exe) was not found on PATH or in %LOCALAPPDATA%\\Microsoft\\WinGet\\Links. "
        + "Install with winget: winget install --id OpenTofu.Tofu. The Manager does not download tofu in Step 3.3.";

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // skip invalid PATH entries
            }
        }

        return null;
    }
}
