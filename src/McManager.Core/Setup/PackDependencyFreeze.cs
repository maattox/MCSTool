namespace McManager.Core.Setup;

/// <summary>
/// Skip-order steps 5–7: never skip a jar that a kept jar declares as required.
/// Re-run after operator Skip marks. Does not grow in-jar heuristics.
/// </summary>
public static class PackDependencyFreeze
{
    public static PackClassification Classify(
        IReadOnlyList<PackJarRecord> records,
        IReadOnlyCollection<string>? operatorSkipTerms = null)
    {
        records ??= [];
        var terms = operatorSkipTerms ?? [];

        var byId = new Dictionary<string, List<PackJarRecord>>(StringComparer.OrdinalIgnoreCase);
        foreach (var jar in records)
        {
            foreach (var id in jar.ProvidedModIds)
            {
                if (string.IsNullOrWhiteSpace(id) || InJarSideDetector.IsPlatformModId(id))
                    continue;
                if (!byId.TryGetValue(id.Trim(), out var list))
                {
                    list = [];
                    byId[id.Trim()] = list;
                }

                list.Add(jar);
            }
        }

        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipReason = new Dictionary<string, PackFileSkipReason>(StringComparer.OrdinalIgnoreCase);
        var skipDetail = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var jar in records)
        {
            if (jar.AutomaticSkipReason != PackFileSkipReason.None)
            {
                skipped.Add(jar.Path);
                skipReason[jar.Path] = jar.AutomaticSkipReason;
                skipDetail[jar.Path] = jar.SkipDetail ?? ReasonLabel(jar.AutomaticSkipReason);
            }

            if (!MatchesOperator(jar, terms))
                continue;

            skipped.Add(jar.Path);
            if (!skipReason.ContainsKey(jar.Path)
                || jar.AutomaticSkipReason == PackFileSkipReason.None)
            {
                skipReason[jar.Path] = PackFileSkipReason.OperatorSkip;
                skipDetail[jar.Path] = "operator skip";
            }
        }

        var requiredBy = new Dictionary<string, PackJarRecord>(StringComparer.OrdinalIgnoreCase);
        bool changed;
        do
        {
            changed = false;
            foreach (var keeper in records)
            {
                if (skipped.Contains(keeper.Path) || keeper.UnclearBlocksInstall)
                    continue;

                foreach (var depId in keeper.RequiredModIds)
                {
                    if (string.IsNullOrWhiteSpace(depId) || !byId.TryGetValue(depId.Trim(), out var deps))
                        continue;

                    foreach (var dep in deps)
                    {
                        if (string.Equals(dep.Path, keeper.Path, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (skipped.Contains(dep.Path))
                        {
                            skipped.Remove(dep.Path);
                            skipReason.Remove(dep.Path);
                            skipDetail.Remove(dep.Path);
                            requiredBy[dep.Path] = keeper;
                            changed = true;
                            continue;
                        }

                        if (dep.UnclearSide && !dep.UnclearBlocksInstall)
                            requiredBy[dep.Path] = keeper;
                    }
                }
            }
        } while (changed);

        string? freezeBlock = null;
        foreach (var jar in records)
        {
            if (!requiredBy.ContainsKey(jar.Path) || !MatchesOperator(jar, terms))
                continue;
            var keeper = requiredBy[jar.Path];
            freezeBlock =
                $"Cannot skip {DisplayName(jar)}: required by {DisplayName(keeper)}.";
            break;
        }

        var willSkip = new List<PackReviewItem>();
        var needsCall = new List<PackReviewItem>();
        var mustKeep = new List<PackReviewItem>();
        var server = new List<string>();
        var client = new List<string>();
        var unclear = new List<string>();
        var inJar = new List<string>();
        var overrideList = new List<string>();
        var packDeclared = new List<string>();
        var operatorSkips = new List<string>();
        var mustKeepPaths = new List<string>();

        foreach (var jar in records)
        {
            var rescued = requiredBy.TryGetValue(jar.Path, out var keeper);
            var stillSkip = skipped.Contains(jar.Path);

            if (stillSkip)
            {
                var reason = skipReason.TryGetValue(jar.Path, out var r) ? r : PackFileSkipReason.None;
                var detail = skipDetail.TryGetValue(jar.Path, out var d) ? d : ReasonLabel(reason);
                willSkip.Add(new PackReviewItem(jar.Path, detail, reason));
                client.Add(jar.Path);
                switch (reason)
                {
                    case PackFileSkipReason.InJarMetadata:
                        inJar.Add(jar.Path);
                        break;
                    case PackFileSkipReason.OverrideList:
                        overrideList.Add(jar.Path);
                        break;
                    case PackFileSkipReason.PackDeclared:
                        packDeclared.Add(jar.Path);
                        break;
                    case PackFileSkipReason.OperatorSkip:
                        operatorSkips.Add(jar.Path);
                        break;
                }

                continue;
            }

            if (rescued)
            {
                var why = "required by " + DisplayName(keeper!);
                mustKeep.Add(new PackReviewItem(
                    jar.Path,
                    why,
                    PackFileSkipReason.None,
                    keeper!.Path,
                    DisplayName(keeper)));
                mustKeepPaths.Add(jar.Path);
                if (!jar.UnclearBlocksInstall)
                    server.Add(jar.Path);
                continue;
            }

            if (jar.UnclearBlocksInstall)
            {
                unclear.Add(jar.Path);
                continue;
            }

            server.Add(jar.Path);
            if (jar.UnclearSide)
            {
                needsCall.Add(new PackReviewItem(jar.Path, "unknown side", PackFileSkipReason.None));
                unclear.Add(jar.Path);
            }
        }

        var review = new PackAssistedReview(willSkip, needsCall, mustKeep, freezeBlock);
        return new PackClassification(
            review,
            server,
            client,
            unclear,
            inJar,
            overrideList,
            packDeclared,
            operatorSkips,
            mustKeepPaths);
    }

    public static bool MatchesOperator(PackJarRecord jar, IReadOnlyCollection<string> terms)
    {
        if (terms.Count == 0)
            return false;
        foreach (var raw in terms)
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;
            var term = raw.Trim();
            if (jar.Path.Equals(term, StringComparison.OrdinalIgnoreCase))
                return true;
            if (Layer2LocalOverlay.TermForRelativePath(jar.Path)
                .Equals(term, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var id in jar.ProvidedModIds)
            {
                if (id.Equals(term, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            var candidate = ExcludeIncludeMatcher.Candidate.From(jar.Path, jar.ProvidedModIds.FirstOrDefault());
            if (ExcludeIncludeMatcher.TermMatches(term, candidate))
                return true;
        }

        return false;
    }

    public static string ReasonLabel(PackFileSkipReason reason) =>
        reason switch
        {
            PackFileSkipReason.OverrideList => "exclude list",
            PackFileSkipReason.PackDeclared => "env.server",
            PackFileSkipReason.InJarMetadata => "in-jar client",
            PackFileSkipReason.OperatorSkip => "operator skip",
            _ => "skipped",
        };

    private static string DisplayName(PackJarRecord jar)
    {
        if (jar.ProvidedModIds.Count > 0 && !string.IsNullOrWhiteSpace(jar.ProvidedModIds[0]))
            return jar.ProvidedModIds[0];
        return Layer2LocalOverlay.TermForRelativePath(jar.Path);
    }
}

/// <summary>Path lists after freeze, ready for analysis / installers.</summary>
public sealed class PackClassification
{
    public PackClassification(
        PackAssistedReview review,
        IReadOnlyList<string> serverSidePaths,
        IReadOnlyList<string> clientOnlyPaths,
        IReadOnlyList<string> unclearSidePaths,
        IReadOnlyList<string> inJarMetadataSkipPaths,
        IReadOnlyList<string> overrideListSkipPaths,
        IReadOnlyList<string> packDeclaredSkipPaths,
        IReadOnlyList<string> operatorSkipPaths,
        IReadOnlyList<string> mustKeepPaths)
    {
        Review = review ?? PackAssistedReview.Empty;
        ServerSidePaths = serverSidePaths ?? [];
        ClientOnlyPaths = clientOnlyPaths ?? [];
        UnclearSidePaths = unclearSidePaths ?? [];
        InJarMetadataSkipPaths = inJarMetadataSkipPaths ?? [];
        OverrideListSkipPaths = overrideListSkipPaths ?? [];
        PackDeclaredSkipPaths = packDeclaredSkipPaths ?? [];
        OperatorSkipPaths = operatorSkipPaths ?? [];
        MustKeepPaths = mustKeepPaths ?? [];
    }

    public PackAssistedReview Review { get; }
    public IReadOnlyList<string> ServerSidePaths { get; }
    public IReadOnlyList<string> ClientOnlyPaths { get; }
    public IReadOnlyList<string> UnclearSidePaths { get; }
    public IReadOnlyList<string> InJarMetadataSkipPaths { get; }
    public IReadOnlyList<string> OverrideListSkipPaths { get; }
    public IReadOnlyList<string> PackDeclaredSkipPaths { get; }
    public IReadOnlyList<string> OperatorSkipPaths { get; }
    public IReadOnlyList<string> MustKeepPaths { get; }
    public string? FreezeBlockReason => Review.FreezeBlockReason;
    public bool NeedsAssistedReview => Review.NeedsAssistedReview;
}
