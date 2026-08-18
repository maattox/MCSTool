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
            var dir = new DirectoryInfo(start);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir.FullName, "config.local.example.json"))
                    || Directory.Exists(Path.Combine(dir.FullName, "infra")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
            }
        }

        return null;
    }

    public static string? FindInfraDirectory()
    {
        var root = FindProductRepoRoot();
        if (root is null)
            return null;
        var infra = Path.Combine(root, "infra");
        return File.Exists(Path.Combine(infra, "main.tf")) ? infra : null;
    }

    public static string? FindOnboxDirectory()
    {
        var root = FindProductRepoRoot();
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

    public static string? FindDoorVmDirectory()
    {
        var root = FindProductRepoRoot();
        if (root is null)
            return null;
        var door = Path.Combine(root, "door_vm");
        return File.Exists(Path.Combine(door, "Makefile")) ? door : null;
    }

    public static string? FindVmAgentDirectory()
    {
        var root = FindProductRepoRoot();
        if (root is null)
            return null;
        var agent = Path.Combine(root, "vm_agent");
        return File.Exists(Path.Combine(agent, "install.sh")) ? agent : null;
    }

    public static string? FindFunctionDirectory()
    {
        var root = FindProductRepoRoot();
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
