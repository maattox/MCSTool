namespace McManager.Core.Setup;

/// <summary>
/// Confirmable summary of a local <c>.mrpack</c> (blueprint §22.1). Analyze-only:
/// no HTTP, no install, no catalog lookup.
/// </summary>
public sealed class MrpackAnalysis
{
    public MrpackAnalysis(
        string packName,
        string? versionId,
        string? summary,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        int? javaMajor,
        int fileCount,
        int serverRequiredCount,
        int serverOptionalCount,
        int clientOnlyCount,
        int unclearSideCount,
        IReadOnlyList<string> serverSidePaths,
        IReadOnlyList<string> clientOnlyPaths,
        IReadOnlyList<string> unclearSidePaths,
        bool hasOverrides,
        bool hasServerOverrides,
        bool hasClientOverrides,
        IReadOnlyList<string> warnings,
        string confirmableSummary,
        int packDeclaredSkipCount,
        int overrideListSkipCount,
        IReadOnlyList<string> packDeclaredSkipPaths,
        IReadOnlyList<string> overrideListSkipPaths,
        IReadOnlyList<string> forceIncludedPaths,
        int inJarMetadataSkipCount = 0,
        IReadOnlyList<string>? inJarMetadataSkipPaths = null,
        PackAssistedReview? assistedReview = null,
        IReadOnlyList<PackJarRecord>? jarRecords = null,
        string? freezeBlockReason = null)
    {
        PackName = packName;
        VersionId = versionId;
        Summary = summary;
        MinecraftVersion = minecraftVersion;
        Loader = loader;
        LoaderVersion = loaderVersion;
        JavaMajor = javaMajor;
        FileCount = fileCount;
        ServerRequiredCount = serverRequiredCount;
        ServerOptionalCount = serverOptionalCount;
        ClientOnlyCount = clientOnlyCount;
        UnclearSideCount = unclearSideCount;
        ServerSidePaths = serverSidePaths;
        ClientOnlyPaths = clientOnlyPaths;
        UnclearSidePaths = unclearSidePaths;
        HasOverrides = hasOverrides;
        HasServerOverrides = hasServerOverrides;
        HasClientOverrides = hasClientOverrides;
        Warnings = warnings;
        ConfirmableSummary = confirmableSummary;
        PackDeclaredSkipCount = packDeclaredSkipCount;
        OverrideListSkipCount = overrideListSkipCount;
        PackDeclaredSkipPaths = packDeclaredSkipPaths;
        OverrideListSkipPaths = overrideListSkipPaths;
        ForceIncludedPaths = forceIncludedPaths;
        InJarMetadataSkipCount = inJarMetadataSkipCount;
        InJarMetadataSkipPaths = inJarMetadataSkipPaths ?? [];
        AssistedReview = assistedReview ?? PackAssistedReview.Empty;
        JarRecords = jarRecords ?? [];
        FreezeBlockReason = freezeBlockReason ?? AssistedReview.FreezeBlockReason;
    }

    public string PackName { get; }
    public string? VersionId { get; }
    public string? Summary { get; }
    public string MinecraftVersion { get; }

    /// <summary>Detected loader id: <c>fabric</c>, <c>quilt</c>, <c>forge</c>, or <c>neoforge</c>.</summary>
    public string Loader { get; }

    public string LoaderVersion { get; }
    public int? JavaMajor { get; }
    public int FileCount { get; }
    public int ServerRequiredCount { get; }
    public int ServerOptionalCount { get; }
    public int ClientOnlyCount { get; }
    public int UnclearSideCount { get; }
    public IReadOnlyList<string> ServerSidePaths { get; }
    public IReadOnlyList<string> ClientOnlyPaths { get; }
    public IReadOnlyList<string> UnclearSidePaths { get; }
    public bool HasOverrides { get; }
    public bool HasServerOverrides { get; }
    public bool HasClientOverrides { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string ConfirmableSummary { get; }

    /// <summary>Skipped because the pack marked <c>env.server == unsupported</c>.</summary>
    public int PackDeclaredSkipCount { get; }

    /// <summary>Skipped because the itzg/product list excluded a server-side or unclear file.</summary>
    public int OverrideListSkipCount { get; }

    public IReadOnlyList<string> PackDeclaredSkipPaths { get; }
    public IReadOnlyList<string> OverrideListSkipPaths { get; }

    /// <summary>Skipped because leftover in-jar Fabric/Forge metadata is client-only (P3).</summary>
    public int InJarMetadataSkipCount { get; }

    public IReadOnlyList<string> InJarMetadataSkipPaths { get; }

    /// <summary>Kept despite pack <c>unsupported</c> because a force-include matched.</summary>
    public IReadOnlyList<string> ForceIncludedPaths { get; }

    public PackAssistedReview AssistedReview { get; }

    public IReadOnlyList<PackJarRecord> JarRecords { get; }

    public string? FreezeBlockReason { get; }

    public bool NeedsAssistedReview => AssistedReview.NeedsAssistedReview;

    public int ServerSideCount => ServerRequiredCount + ServerOptionalCount;

    /// <summary>Re-run freeze after in-session Skip ticks without re-reading the archive.</summary>
    public MrpackAnalysis ApplyOperatorSkips(IReadOnlyCollection<string> skipTerms)
    {
        var classified = PackDependencyFreeze.Classify(JarRecords, skipTerms);
        return new MrpackAnalysis(
            PackName,
            VersionId,
            Summary,
            MinecraftVersion,
            Loader,
            LoaderVersion,
            JavaMajor,
            FileCount,
            classified.ServerSidePaths.Count,
            0,
            classified.ClientOnlyPaths.Count,
            classified.UnclearSidePaths.Count,
            classified.ServerSidePaths,
            classified.ClientOnlyPaths,
            classified.UnclearSidePaths,
            HasOverrides,
            HasServerOverrides,
            HasClientOverrides,
            Warnings,
            ConfirmableSummary,
            classified.PackDeclaredSkipPaths.Count,
            classified.OverrideListSkipPaths.Count,
            classified.PackDeclaredSkipPaths,
            classified.OverrideListSkipPaths,
            ForceIncludedPaths,
            classified.InJarMetadataSkipPaths.Count,
            classified.InJarMetadataSkipPaths,
            classified.Review,
            JarRecords,
            classified.FreezeBlockReason);
    }
}
