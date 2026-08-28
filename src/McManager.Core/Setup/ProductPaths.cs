using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>Resolves product <c>infra/</c>, <c>onbox/</c>, <c>door_vm/</c>, <c>vm_agent/</c>, and <c>functions/shutdown_vm/</c>.</summary>
public static class ProductPaths
{
    public const string TofuDryRunEnvVar = "MCMANAGER_TOFU_DRY_RUN";

    public static bool IsTofuDryRun()
    {
        var v = Environment.GetEnvironmentVariable(TofuDryRunEnvVar);
        return string.Equals(v, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(v, "yes", StringComparison.OrdinalIgnoreCase);
    }

    public static string? FindProductRepoRoot()
    {
        foreach (var start in CandidateStarts())
        {
            var found = FindProductRepoRootFrom(start);
            if (found is not null)
                return found;
        }

        return null;
    }

    /// <summary>
    /// Walks from <paramref name="start"/> looking for a product root
    /// (<c>infra/</c> or <c>config.local.example.json</c>). A published folder
    /// with those trees next to the exe is a root; no git checkout required.
    /// </summary>
    internal static string? FindProductRepoRootFrom(string start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;

        DirectoryInfo? dir;
        try
        {
            dir = new DirectoryInfo(start);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "config.local.example.json"))
                || Directory.Exists(Path.Combine(dir.FullName, "infra")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    public static string? FindInfraDirectory() => InfraDirectoryAt(FindProductRepoRoot());

    internal static string? InfraDirectoryAt(string? root)
    {
        if (root is null)
            return null;
        var infra = Path.Combine(root, "infra");
        return File.Exists(Path.Combine(infra, "main.tf")) ? infra : null;
    }

    public static string? FindOnboxDirectory() => OnboxDirectoryAt(FindProductRepoRoot());

    internal static string? OnboxDirectoryAt(string? root)
    {
        if (root is null)
            return null;
        var onbox = Path.Combine(root, "onbox", "mcmgr");
        return File.Exists(Path.Combine(onbox, "common", "driver.sh")) ? onbox : null;
    }

    public static string? FindLabRepoRoot()
    {
        var product = FindProductRepoRoot();
        if (product is null)
            return null;
        var sibling = Path.GetFullPath(Path.Combine(product, "..", "OCI-mc-server-manager"));
        if (File.Exists(Path.Combine(sibling, "PRODUCT-IDEAS.md")))
            return sibling;
        return null;
    }

    public static string? FindDoorVmDirectory() => DoorVmDirectoryAt(FindProductRepoRoot());

    internal static string? DoorVmDirectoryAt(string? root)
    {
        if (root is null)
            return null;
        var door = Path.Combine(root, "door_vm");
        return File.Exists(Path.Combine(door, "Makefile")) ? door : null;
    }

    public static string? FindVmAgentDirectory() => VmAgentDirectoryAt(FindProductRepoRoot());

    internal static string? VmAgentDirectoryAt(string? root)
    {
        if (root is null)
            return null;
        var agent = Path.Combine(root, "vm_agent");
        return File.Exists(Path.Combine(agent, "install.sh")) ? agent : null;
    }

    public static string? FindFunctionDirectory() => FunctionDirectoryAt(FindProductRepoRoot());

    internal static string? FunctionDirectoryAt(string? root)
    {
        if (root is null)
            return null;
        var fn = Path.Combine(root, "functions", "shutdown_vm");
        return File.Exists(Path.Combine(fn, "func.py")) ? fn : null;
    }

    private static IEnumerable<string> CandidateStarts()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();
        var data = LocalConfigStore.TryFindDataDirectory();
        if (data is not null)
            yield return Path.GetDirectoryName(data) ?? data;
    }
}
