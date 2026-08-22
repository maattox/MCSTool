namespace McManager.Core.Setup;

/// <summary>Recognized shapes for a user-supplied zip (blueprint §24.1).</summary>
public enum ManualServerPackKind
{
    /// <summary>Unstructured <c>mods/</c> (+ optional <c>config/</c> / documented layout).</summary>
    UnstructuredServer,

    /// <summary>CurseForge Server Files: <c>manifest.json</c> plus libraries/installer/jars already in the zip.</summary>
    CurseForgeServerFiles,

    /// <summary>Modrinth <c>modrinth.index.json</c> — use <see cref="MrpackInstaller"/>, not this adapter.</summary>
    Mrpack,

    /// <summary>
    /// CurseForge client export, incomplete Server Files, or mixed jars + leftover
    /// manifest file IDs (no CurseForge API). Do not heuristic-strip or fetch jars.
    /// </summary>
    CurseForgeClientExport,

    /// <summary>Launcher/client instance zip. Do not heuristic-strip.</summary>
    ClientPack,

    /// <summary>Zip opened, but no server-pack layout was recognized.</summary>
    Unknown,
}

/// <summary>
/// Confirmable summary of a local generic server-pack zip (blueprint §24).
/// Analyze-only until <see cref="ManualServerPackInstaller"/> copies files.
/// </summary>
public sealed class ManualServerPackAnalysis
{
    public ManualServerPackAnalysis(
        ManualServerPackKind kind,
        bool canInstall,
        string? refusalReason,
        string packName,
        string? versionId,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        int? javaMajor,
        string? wrapperPrefix,
        int fileCount,
        int serverSideCount,
        int clientOnlyCount,
        int unclearSideCount,
        IReadOnlyList<string> serverSidePaths,
        IReadOnlyList<string> clientOnlyPaths,
        IReadOnlyList<string> unclearSidePaths,
        IReadOnlyList<string> warnings,
        string confirmableSummary,
        bool mapRootJarsToMods = false,
        int overrideListSkipCount = 0,
        int inJarMetadataSkipCount = 0,
        IReadOnlyList<string>? overrideListSkipPaths = null,
        IReadOnlyList<string>? inJarMetadataSkipPaths = null,
        IReadOnlyList<string>? forceIncludedPaths = null,
        string? detectedMinecraftVersion = null,
        string? detectedLoader = null,
        bool isDerived = false)
    {
        Kind = kind;
        CanInstall = canInstall;
        RefusalReason = refusalReason;
        PackName = packName;
        VersionId = versionId;
        MinecraftVersion = minecraftVersion;
        Loader = loader;
        LoaderVersion = loaderVersion;
        JavaMajor = javaMajor;
        DetectedMinecraftVersion = detectedMinecraftVersion ?? minecraftVersion;
        DetectedLoader = detectedLoader ?? loader;
        IsDerived = isDerived;
        WrapperPrefix = wrapperPrefix;
        FileCount = fileCount;
        ServerSideCount = serverSideCount;
        ClientOnlyCount = clientOnlyCount;
        UnclearSideCount = unclearSideCount;
        ServerSidePaths = serverSidePaths;
        ClientOnlyPaths = clientOnlyPaths;
        UnclearSidePaths = unclearSidePaths;
        Warnings = warnings;
        ConfirmableSummary = confirmableSummary;
        MapRootJarsToMods = mapRootJarsToMods;
        OverrideListSkipCount = overrideListSkipCount;
        InJarMetadataSkipCount = inJarMetadataSkipCount;
        OverrideListSkipPaths = overrideListSkipPaths ?? [];
        InJarMetadataSkipPaths = inJarMetadataSkipPaths ?? [];
        ForceIncludedPaths = forceIncludedPaths ?? [];
    }

    public ManualServerPackKind Kind { get; }
    public bool CanInstall { get; }
    public string? RefusalReason { get; }
    public string PackName { get; }
    public string? VersionId { get; }
    public string MinecraftVersion { get; }
    public string Loader { get; }
    public string LoaderVersion { get; }
    public int? JavaMajor { get; }

    /// <summary>Single top-level folder stripped from zip paths, or null when entries are already at pack root.</summary>
    public string? WrapperPrefix { get; }

    public int FileCount { get; }
    public int ServerSideCount { get; }
    public int ClientOnlyCount { get; }
    public int UnclearSideCount { get; }
    public IReadOnlyList<string> ServerSidePaths { get; }
    public IReadOnlyList<string> ClientOnlyPaths { get; }
    public IReadOnlyList<string> UnclearSidePaths { get; }
    public IReadOnlyList<string> Warnings { get; }
    public string ConfirmableSummary { get; }

    /// <summary>
    /// Zip has no <c>mods/</c> folder; root <c>*.jar</c> entries install into dest <c>mods/</c>
    /// (MilesPack shape).
    /// </summary>
    public bool MapRootJarsToMods { get; }

    /// <summary>Skipped because the itzg/product CurseForge list excluded the jar.</summary>
    public int OverrideListSkipCount { get; }

    /// <summary>Skipped because in-jar metadata (explicit client side fields, client entrypoints, or exclusively client mixin targets) is client-only.</summary>
    public int InJarMetadataSkipCount { get; }

    public IReadOnlyList<string> OverrideListSkipPaths { get; }
    public IReadOnlyList<string> InJarMetadataSkipPaths { get; }

    /// <summary>Kept despite in-jar <c>client</c> because a force-include matched.</summary>
    public IReadOnlyList<string> ForceIncludedPaths { get; }

    /// <summary>Peeked or layout-detected Minecraft version before user / sidecar override.</summary>
    public string DetectedMinecraftVersion { get; }

    /// <summary>Peeked or layout-detected loader before user / sidecar override.</summary>
    public string DetectedLoader { get; }

    /// <summary>Zip contains product <see cref="DerivedPackIdentity.SidecarEntryName"/>.</summary>
    public bool IsDerived { get; }
}
