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
        string confirmableSummary)
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

    public int ServerSideCount => ServerRequiredCount + ServerOptionalCount;
}
