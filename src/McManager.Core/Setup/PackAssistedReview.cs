namespace McManager.Core.Setup;

/// <summary>
/// One jar/file in the single-list assisted review (Core DTO; no Razor).
/// </summary>
public sealed class PackReviewItem
{
    public PackReviewItem(
        string path,
        string reason,
        PackFileSkipReason skipReason = PackFileSkipReason.None,
        string? requiredByPath = null,
        string? requiredByName = null)
    {
        Path = path ?? "";
        Reason = reason ?? "";
        SkipReason = skipReason;
        RequiredByPath = requiredByPath;
        RequiredByName = requiredByName;
    }

    public string Path { get; }
    public string Reason { get; }
    public PackFileSkipReason SkipReason { get; }
    public string? RequiredByPath { get; }
    public string? RequiredByName { get; }

    public string FileName
    {
        get
        {
            var n = Path.Replace('\\', '/');
            var slash = n.LastIndexOf('/');
            return slash < 0 ? n : n[(slash + 1)..];
        }
    }
}

/// <summary>
/// Row-state buckets after automatic skips and dependency freeze.
/// The review UI is one list; these stay as state, not separate scroll regions.
/// </summary>
public sealed class PackAssistedReview
{
    public static PackAssistedReview Empty { get; } = new([], [], [], freezeBlockReason: null);

    public PackAssistedReview(
        IReadOnlyList<PackReviewItem> willSkip,
        IReadOnlyList<PackReviewItem> needsYourCall,
        IReadOnlyList<PackReviewItem> mustKeep,
        string? freezeBlockReason = null)
    {
        WillSkip = willSkip ?? [];
        NeedsYourCall = needsYourCall ?? [];
        MustKeep = mustKeep ?? [];
        FreezeBlockReason = freezeBlockReason;
    }

    public IReadOnlyList<PackReviewItem> WillSkip { get; }
    public IReadOnlyList<PackReviewItem> NeedsYourCall { get; }
    public IReadOnlyList<PackReviewItem> MustKeep { get; }

    /// <summary>True when the operator still has unknown-side jars to acknowledge (P2 gate).</summary>
    public bool NeedsAssistedReview => NeedsYourCall.Count > 0;

    /// <summary>Set when an operator Skip would drop a required dep of a kept jar. P1 does not flip CanContinue.</summary>
    public string? FreezeBlockReason { get; }

    /// <summary>
    /// Every review jar once, in pack/analyzer order when <paramref name="preferredPathOrder"/>
    /// is supplied. Must-keep wins on path collisions so the freeze note stays on the row.
    /// </summary>
    public IReadOnlyList<PackReviewItem> UnifiedRows(IReadOnlyList<string>? preferredPathOrder = null)
    {
        var byPath = new Dictionary<string, PackReviewItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in NeedsYourCall)
            byPath[item.Path] = item;
        foreach (var item in WillSkip)
            byPath[item.Path] = item;
        foreach (var item in MustKeep)
            byPath[item.Path] = item;

        if (byPath.Count == 0)
            return [];

        var rows = new List<PackReviewItem>(byPath.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (preferredPathOrder is { Count: > 0 })
        {
            foreach (var path in preferredPathOrder)
            {
                if (byPath.TryGetValue(path, out var item) && seen.Add(item.Path))
                    rows.Add(item);
            }
        }

        foreach (var item in NeedsYourCall)
        {
            if (byPath.TryGetValue(item.Path, out var chosen) && seen.Add(chosen.Path))
                rows.Add(chosen);
        }

        foreach (var item in WillSkip)
        {
            if (byPath.TryGetValue(item.Path, out var chosen) && seen.Add(chosen.Path))
                rows.Add(chosen);
        }

        foreach (var item in MustKeep)
        {
            if (byPath.TryGetValue(item.Path, out var chosen) && seen.Add(chosen.Path))
                rows.Add(chosen);
        }

        return rows;
    }
}

/// <summary>
/// Per-jar facts captured during analyze so P2 can reclassify without re-hashing the zip.
/// </summary>
public sealed class PackJarRecord
{
    public PackJarRecord(
        string path,
        IReadOnlyList<string> providedModIds,
        IReadOnlyList<string> requiredModIds,
        bool unclearSide,
        bool forceIncluded,
        PackFileSkipReason automaticSkipReason,
        bool unclearBlocksInstall = false,
        string? skipDetail = null)
    {
        Path = path ?? "";
        ProvidedModIds = providedModIds ?? [];
        RequiredModIds = requiredModIds ?? [];
        UnclearSide = unclearSide;
        ForceIncluded = forceIncluded;
        AutomaticSkipReason = automaticSkipReason;
        UnclearBlocksInstall = unclearBlocksInstall;
        SkipDetail = skipDetail;
    }

    public string Path { get; }
    public IReadOnlyList<string> ProvidedModIds { get; }
    public IReadOnlyList<string> RequiredModIds { get; }
    public bool UnclearSide { get; }
    public bool ForceIncluded { get; }
    public PackFileSkipReason AutomaticSkipReason { get; }

    /// <summary>
    /// .mrpack unclear <c>env.server</c> fails install (do not send to Needs your call).
    /// Manual / jar-root unknowns stay kept and go to review.
    /// </summary>
    public bool UnclearBlocksInstall { get; }

    public string? SkipDetail { get; }
}
