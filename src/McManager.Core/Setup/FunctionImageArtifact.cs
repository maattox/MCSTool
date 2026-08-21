namespace McManager.Core.Setup;

/// <summary>
/// Locates a pre-built <c>linux/arm64</c> spend-brake Function image tarball.
/// Docker is not required when this file exists; V1 Step 8.6.1 still owns CI / installer bundling.
/// </summary>
public static class FunctionImageArtifact
{
    public const string FileName = "mcmgr-fn-softstop-linux-arm64.tar";
    public const string PathEnvVar = "MCMANAGER_FUNCTION_IMAGE_TAR";

    public static string? Find() =>
        FirstExisting(ListCandidatePaths(
            Environment.GetEnvironmentVariable(PathEnvVar),
            AppContext.BaseDirectory,
            ProductPaths.FindProductRepoRoot()));

    public static IReadOnlyList<string> ListCandidatePaths(
        string? envPath,
        string? appDirectory,
        string? repoRoot)
    {
        var list = new List<string>();
        Add(list, envPath);
        if (!string.IsNullOrWhiteSpace(appDirectory))
        {
            Add(list, Path.Combine(appDirectory, FileName));
            Add(list, Path.Combine(appDirectory, "artifacts", FileName));
        }

        if (!string.IsNullOrWhiteSpace(repoRoot))
            Add(list, Path.Combine(repoRoot, "artifacts", FileName));

        return list;
    }

    public static string? FirstExisting(IEnumerable<string> candidates)
    {
        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            try
            {
                if (File.Exists(path))
                    return Path.GetFullPath(path);
            }
            catch (IOException)
            {
                // skip unreadable candidates
            }
        }

        return null;
    }

    private static void Add(List<string> list, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;
        var trimmed = path.Trim();
        if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
            list.Add(trimmed);
    }
}
