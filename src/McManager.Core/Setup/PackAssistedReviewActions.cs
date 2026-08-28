using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>
/// Persist operator Skip / Unskip (Layer 2 per-archive) and reclassify in memory.
/// Does not change freeze rules. Unskip of an auto-exclude is a Layer 2 force-include,
/// not a full re-analyze of the zip.
/// </summary>
public static class PackAssistedReviewActions
{
    public readonly record struct SkipApplyResult(SetupPackPreview Preview, bool NeedsReanalyze);

    public readonly record struct OperatorSkipChange(string Path, bool Skip);

    public static HashSet<string> LoadPersistedSkipTerms(string? packPath, string? dataDirectory = null) =>
        LoadPersistedPackTerms(packPath, dataDirectory, excludes: true);

    public static HashSet<string> LoadPersistedKeepTerms(string? packPath, string? dataDirectory = null) =>
        LoadPersistedPackTerms(packPath, dataDirectory, excludes: false);

    private static HashSet<string> LoadPersistedPackTerms(string? packPath, string? dataDirectory, bool excludes)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dataDir = dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
        var hash = Layer2LocalOverlay.TryHashFile(packPath);
        if (string.IsNullOrWhiteSpace(dataDir) || string.IsNullOrWhiteSpace(hash))
            return terms;

        var lists = Layer2LocalOverlay.Load(dataDir);
        if (!lists.TryGetPack(Layer2LocalOverlay.IdentityKey(hash), out var pack))
            return terms;

        var source = excludes ? pack.Excludes : pack.ForceIncludes;
        foreach (var raw in source)
        {
            if (!string.IsNullOrWhiteSpace(raw))
                terms.Add(raw.Trim());
        }

        return terms;
    }

    public static bool IsSkipped(IReadOnlyCollection<string> terms, string? relativePath)
    {
        if (terms.Count == 0 || string.IsNullOrWhiteSpace(relativePath))
            return false;
        var term = Layer2LocalOverlay.TermForRelativePath(relativePath);
        foreach (var raw in terms)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var t = raw.Trim();
            if (t.Equals(term, StringComparison.OrdinalIgnoreCase)
                || t.Equals(relativePath.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Writes or removes a Layer 2 exclude / force-include, then re-runs freeze in memory.
    /// Unskip of a baked automatic skip (exclude list, pack env, in-jar) persists as a
    /// force-include so the next analyze and install keep the jar. Never re-reads the zip.
    /// </summary>
    public static SkipApplyResult ApplySkip(
        SetupPackPreview preview,
        ISet<string> skipTerms,
        string relativePath,
        bool skip,
        string? dataDirectory = null,
        ISet<string>? keepTerms = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(skipTerms);
        keepTerms ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var term = Layer2LocalOverlay.TermForRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(term))
            return new SkipApplyResult(preview, NeedsReanalyze: false);

        var bakedAutomatic = preview.JarRecords.Any(j =>
            PackDependencyFreeze.MatchesOperator(j, [term])
            && j.AutomaticSkipReason != PackFileSkipReason.None);

        if (skip)
        {
            skipTerms.Add(term);
            keepTerms.Remove(term);
        }
        else
        {
            skipTerms.Remove(term);
            if (bakedAutomatic)
                keepTerms.Add(term);
        }

        var dataDir = dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
        var hash = Layer2LocalOverlay.TryHashFile(preview.SourcePath);
        if (!string.IsNullOrWhiteSpace(dataDir) && !string.IsNullOrWhiteSpace(hash))
        {
            if (skip)
            {
                Layer2LocalOverlay.PromoteExclude(dataDir, hash, term);
            }
            else
            {
                Layer2LocalOverlay.RemoveExclude(dataDir, hash, term);
                if (bakedAutomatic)
                    Layer2LocalOverlay.PromoteForceInclude(dataDir, hash, term);
                else
                    Layer2LocalOverlay.RemoveForceInclude(dataDir, hash, term);
            }
        }

        var next = preview.ApplyOperatorSkips(skipTerms.ToArray(), keepTerms.ToArray());
        return new SkipApplyResult(next, NeedsReanalyze: false);
    }
}
