using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>
/// Persist operator Skip / Unskip (Layer 2 per-archive) and reclassify in memory.
/// Does not change freeze rules.
/// </summary>
public static class PackAssistedReviewActions
{
    public readonly record struct SkipApplyResult(SetupPackPreview Preview, bool NeedsReanalyze);

    public readonly record struct OperatorSkipChange(string Path, bool Skip);

    public static HashSet<string> LoadPersistedSkipTerms(string? packPath, string? dataDirectory = null)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dataDir = dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
        var hash = Layer2LocalOverlay.TryHashFile(packPath);
        if (string.IsNullOrWhiteSpace(dataDir) || string.IsNullOrWhiteSpace(hash))
            return terms;

        var lists = Layer2LocalOverlay.Load(dataDir);
        if (!lists.TryGetPack(Layer2LocalOverlay.IdentityKey(hash), out var pack))
            return terms;

        foreach (var raw in pack.Excludes)
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
    /// Writes or removes a Layer 2 exclude, then re-runs freeze. Unskip of a skip that was
    /// already baked into <see cref="PackJarRecord.AutomaticSkipReason"/> needs a full re-analyze.
    /// </summary>
    public static SkipApplyResult ApplySkip(
        SetupPackPreview preview,
        ISet<string> skipTerms,
        string relativePath,
        bool skip,
        string? dataDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(skipTerms);

        var term = Layer2LocalOverlay.TermForRelativePath(relativePath);
        if (string.IsNullOrWhiteSpace(term))
            return new SkipApplyResult(preview, NeedsReanalyze: false);

        var bakedOverride = preview.JarRecords.Any(j =>
            PackDependencyFreeze.MatchesOperator(j, [term])
            && j.AutomaticSkipReason == PackFileSkipReason.OverrideList);

        if (skip)
            skipTerms.Add(term);
        else
            skipTerms.Remove(term);

        var dataDir = dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
        var hash = Layer2LocalOverlay.TryHashFile(preview.SourcePath);
        if (!string.IsNullOrWhiteSpace(dataDir) && !string.IsNullOrWhiteSpace(hash))
        {
            if (skip)
                Layer2LocalOverlay.PromoteExclude(dataDir, hash, term);
            else
                Layer2LocalOverlay.RemoveExclude(dataDir, hash, term);
        }

        var next = preview.ApplyOperatorSkips(skipTerms.ToArray());
        return new SkipApplyResult(next, NeedsReanalyze: !skip && bakedOverride);
    }
}
