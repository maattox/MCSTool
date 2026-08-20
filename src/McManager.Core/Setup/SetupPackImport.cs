using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Routes a user-supplied local pack file to the 4.7–4.9 analyzers (no catalog).
/// Setup can continue only for Fabric / Forge / NeoForge with a confirmable server-side summary.
/// </summary>
public static class SetupPackImport
{
    public const string KindMrpack = "mrpack";
    public const string KindManualZip = "manual_zip";

    public const string QuiltRefusal =
        "This pack uses Quilt. Setup can detect Quilt but cannot install it yet. "
        + "Export a Fabric, Forge, or NeoForge pack instead.";

    public const string UnclearSideRefusal =
        "This pack has file(s) with unclear server/client side. Do not guess. "
        + "Fix the pack metadata or pick a different export.";

    public const string LoaderRefusal =
        "Setup can install Fabric, Forge, or NeoForge packs. This pack's loader is not supported.";

    public const string ClientPackTitle = "Friends need this pack to play";

    /// <summary>Dedicated wizard/Guide copy (blueprint §25). Novice wording; no VM1 jargon.</summary>
    public const string ClientPackCopy =
        "This server is not playable for friends until they install the same exported pack "
        + "on their PCs. Vanilla Minecraft is not enough. Keep the file you upload — Manager "
        + "also saves a copy so you can share it later. This app cannot rebuild a client pack "
        + "from the mods folder on the server.";

    public const string ClientPackAckLabel =
        "I will give friends this same exported pack. They cannot join until they have it.";

    /// <summary>
    /// Confirmable-summary warning when the override list skips mods the pack treated as
    /// server-side or side-unknown (row 12). Not a third required checkbox.
    /// </summary>
    public const string OverrideListMisdeclarationCopy =
        "This pack marks some mods as needed on the server that are known client-only mods. "
        + "Setup will skip those on the game computer. If the server fails to start, check this skipped list first.";

    public const int OverrideListExampleCap = 6;

    /// <summary>Shareable identity from the analyzed pack (file import; no catalog URL).</summary>
    public static string FriendsNeedLine(
        string? packName,
        string? minecraftVersion,
        string? loader,
        string? loaderVersion)
    {
        var name = string.IsNullOrWhiteSpace(packName) ? "this pack" : packName.Trim();
        var mc = string.IsNullOrWhiteSpace(minecraftVersion)
            ? ""
            : "Minecraft " + minecraftVersion.Trim();
        var loaderLabel = DisplayLoader(loader);
        if (!string.IsNullOrWhiteSpace(loaderLabel) && !string.IsNullOrWhiteSpace(loaderVersion))
            loaderLabel += " " + loaderVersion.Trim();
        string identity;
        if (string.IsNullOrWhiteSpace(mc) && string.IsNullOrWhiteSpace(loaderLabel))
            identity = name;
        else if (string.IsNullOrWhiteSpace(loaderLabel))
            identity = $"{name} — {mc}";
        else if (string.IsNullOrWhiteSpace(mc))
            identity = $"{name} — {loaderLabel}";
        else
            identity = $"{name} — {mc} with {loaderLabel}";
        return "Share " + identity
            + ". Give friends the same file you uploaded (not a zip of the server mods folder).";
    }

    public static string DisplayLoader(string? loader)
    {
        var id = (loader ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            MrpackAnalyzer.LoaderFabric => "Fabric",
            MrpackAnalyzer.LoaderForge => "Forge",
            MrpackAnalyzer.LoaderNeoForge => "NeoForge",
            MrpackAnalyzer.LoaderQuilt => "Quilt",
            _ => string.IsNullOrWhiteSpace(loader) ? "" : loader.Trim(),
        };
    }

    public static ServiceResult<SetupPackPreview> AnalyzeFile(
        string path,
        ExcludeIncludeListRefresh? refresh = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ServiceResult<SetupPackPreview>.Fail("No pack file was provided.");
        if (!File.Exists(path))
            return ServiceResult<SetupPackPreview>.Fail($"File not found: {path}");

        var ext = Path.GetExtension(path);
        if (ext.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            var mr = MrpackAnalyzer.AnalyzeFile(path, refresh?.ModrinthMatcher());
            if (!mr.Succeeded)
                return ServiceResult<SetupPackPreview>.Fail(mr.Error!);
            return ServiceResult<SetupPackPreview>.Ok(FromMrpack(mr.Value!, path));
        }

        var manual = ManualServerPackAnalyzer.AnalyzeFile(path, refresh?.CurseForgeMatcher());
        if (!manual.Succeeded)
            return ServiceResult<SetupPackPreview>.Fail(manual.Error!);

        var analysis = manual.Value!;
        if (analysis.Kind == ManualServerPackKind.Mrpack)
        {
            var mr = MrpackAnalyzer.AnalyzeFile(path, refresh?.ModrinthMatcher());
            if (!mr.Succeeded)
                return ServiceResult<SetupPackPreview>.Fail(mr.Error!);
            return ServiceResult<SetupPackPreview>.Ok(FromMrpack(mr.Value!, path));
        }

        return ServiceResult<SetupPackPreview>.Ok(FromManual(analysis, path));
    }

    public static SetupPackPreview FromMrpack(MrpackAnalysis analysis, string path)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        string? block = null;
        if (string.Equals(analysis.Loader, MrpackAnalyzer.LoaderQuilt, StringComparison.OrdinalIgnoreCase))
            block = QuiltRefusal;
        else if (!IsInstallableLoader(analysis.Loader))
            block = LoaderRefusal;
        else if (analysis.UnclearSideCount > 0)
            block = UnclearSideRefusal;
        else if (string.IsNullOrWhiteSpace(analysis.MinecraftVersion))
            block = "This pack does not declare a Minecraft version.";

        var warning = FormatOverrideListWarning(analysis.OverrideListSkipCount, analysis.OverrideListSkipPaths);
        return new SetupPackPreview(
            KindMrpack,
            path,
            analysis.PackName,
            analysis.VersionId,
            analysis.MinecraftVersion,
            analysis.Loader,
            analysis.LoaderVersion,
            analysis.JavaMajor,
            analysis.FileCount,
            analysis.ServerSideCount,
            analysis.ClientOnlyCount,
            analysis.UnclearSideCount,
            PrependWarning(analysis.ConfirmableSummary, warning),
            analysis.Warnings,
            canContinue: block is null,
            blockReason: block,
            analysis.OverrideListSkipCount,
            analysis.OverrideListSkipPaths,
            warning);
    }

    public static SetupPackPreview FromManual(ManualServerPackAnalysis analysis, string path)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        string? block = null;
        if (!analysis.CanInstall)
            block = analysis.RefusalReason ?? ManualServerPackAnalyzer.UnknownRefusal;
        else if (string.Equals(analysis.Loader, MrpackAnalyzer.LoaderQuilt, StringComparison.OrdinalIgnoreCase))
            block = QuiltRefusal;
        else if (!IsInstallableLoader(analysis.Loader))
            block = LoaderRefusal;
        else if (analysis.UnclearSideCount > 0)
            block = UnclearSideRefusal;
        else if (string.IsNullOrWhiteSpace(analysis.MinecraftVersion))
            block = "This pack does not declare a Minecraft version.";

        var warning = FormatOverrideListWarning(analysis.OverrideListSkipCount, analysis.OverrideListSkipPaths);
        return new SetupPackPreview(
            KindManualZip,
            path,
            analysis.PackName,
            analysis.VersionId,
            analysis.MinecraftVersion,
            analysis.Loader,
            analysis.LoaderVersion,
            analysis.JavaMajor,
            analysis.FileCount,
            analysis.ServerSideCount,
            analysis.ClientOnlyCount,
            analysis.UnclearSideCount,
            PrependWarning(analysis.ConfirmableSummary, warning),
            analysis.Warnings,
            canContinue: block is null,
            blockReason: block,
            analysis.OverrideListSkipCount,
            analysis.OverrideListSkipPaths,
            warning);
    }

    /// <summary>
    /// Novice warning plus capped filenames when the override list skipped server-side / unknown-side files.
    /// Returns null when there is nothing to warn about (pack-declared client-only is not this case).
    /// </summary>
    public static string? FormatOverrideListWarning(int skipCount, IReadOnlyList<string>? skipPaths)
    {
        if (skipCount <= 0)
            return null;

        var paths = skipPaths ?? [];
        var examples = paths
            .Select(ExampleFileName)
            .Where(n => n.Length > 0)
            .Take(OverrideListExampleCap)
            .ToList();

        var sb = new System.Text.StringBuilder();
        sb.Append(OverrideListMisdeclarationCopy);
        if (examples.Count > 0)
        {
            sb.Append(" Examples: ").Append(string.Join(", ", examples));
            var remaining = skipCount - examples.Count;
            if (remaining > 0)
                sb.Append(" (and ").Append(remaining).Append(" more)");
            sb.Append('.');
        }

        return sb.ToString();
    }

    private static string ExampleFileName(string relativePath)
    {
        var n = (relativePath ?? "").Replace('\\', '/').Trim();
        if (n.Length == 0)
            return "";
        var slash = n.LastIndexOf('/');
        return slash < 0 ? n : n[(slash + 1)..];
    }

    private static string PrependWarning(string summary, string? warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return summary;
        return warning.TrimEnd() + Environment.NewLine + Environment.NewLine + summary;
    }

    public static bool IsInstallableLoader(string? loader)
    {
        var id = (loader ?? "").Trim().ToLowerInvariant();
        return id is MrpackAnalyzer.LoaderFabric
            or MrpackAnalyzer.LoaderForge
            or MrpackAnalyzer.LoaderNeoForge;
    }

    /// <summary>On-box <c>DISTRIBUTION</c> for the wizard state (vanilla/paper or a loader id).</summary>
    public static string ToDistribution(SetupWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (SetupServerType.IsModded(state.ServerType))
        {
            var loader = (state.PackLoader ?? "").Trim().ToLowerInvariant();
            return IsInstallableLoader(loader) ? loader : "";
        }

        return SetupVanillaFlavor.ToDistribution(state.VanillaFlavor);
    }

    public static bool IsOnboxDistribution(string? distribution)
    {
        var id = (distribution ?? "").Trim().ToLowerInvariant();
        return id is SetupVanillaFlavor.DistributionVanilla
            or SetupVanillaFlavor.DistributionPaper
            or MrpackAnalyzer.LoaderFabric
            or MrpackAnalyzer.LoaderForge
            or MrpackAnalyzer.LoaderNeoForge;
    }

    /// <summary>Optional loader pin env name+value for on-box resolve (empty if unknown).</summary>
    public static (string Name, string Value)? LoaderPin(string? loader, string? loaderVersion)
    {
        if (string.IsNullOrWhiteSpace(loaderVersion))
            return null;
        var id = (loader ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            MrpackAnalyzer.LoaderFabric => ("FABRIC_LOADER_VERSION", loaderVersion.Trim()),
            MrpackAnalyzer.LoaderForge => ("FORGE_VERSION", loaderVersion.Trim()),
            MrpackAnalyzer.LoaderNeoForge => ("NEOFORGE_VERSION", loaderVersion.Trim()),
            _ => null,
        };
    }

    public static string PlanLabel(SetupWizardState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!SetupServerType.IsModded(state.ServerType))
            return SetupVanillaFlavor.PlanLabel(state.VanillaFlavor);

        var name = string.IsNullOrWhiteSpace(state.PackName) ? "(pack not confirmed)" : state.PackName.Trim();
        var loader = string.IsNullOrWhiteSpace(state.PackLoader) ? "unknown loader" : state.PackLoader.Trim();
        var loaderVer = string.IsNullOrWhiteSpace(state.PackLoaderVersion)
            ? ""
            : " " + state.PackLoaderVersion.Trim();
        return $"Modded — {name} ({loader}{loaderVer})";
    }
}

/// <summary>Confirmable Setup preview of a local pack (analyze only until Deploy bootstrap).</summary>
public sealed class SetupPackPreview
{
    public SetupPackPreview(
        string kind,
        string sourcePath,
        string packName,
        string? versionId,
        string minecraftVersion,
        string loader,
        string loaderVersion,
        int? javaMajor,
        int fileCount,
        int serverSideCount,
        int clientOnlyCount,
        int unclearSideCount,
        string confirmableSummary,
        IReadOnlyList<string> warnings,
        bool canContinue,
        string? blockReason,
        int overrideListSkipCount = 0,
        IReadOnlyList<string>? overrideListSkipPaths = null,
        string? overrideListWarning = null)
    {
        Kind = kind;
        SourcePath = sourcePath;
        PackName = packName;
        VersionId = versionId;
        MinecraftVersion = minecraftVersion;
        Loader = loader;
        LoaderVersion = loaderVersion;
        JavaMajor = javaMajor;
        FileCount = fileCount;
        ServerSideCount = serverSideCount;
        ClientOnlyCount = clientOnlyCount;
        UnclearSideCount = unclearSideCount;
        ConfirmableSummary = confirmableSummary;
        Warnings = warnings;
        CanContinue = canContinue;
        BlockReason = blockReason;
        OverrideListSkipCount = overrideListSkipCount;
        OverrideListSkipPaths = overrideListSkipPaths ?? [];
        OverrideListWarning = overrideListWarning;
    }

    public string Kind { get; }
    public string SourcePath { get; }
    public string PackName { get; }
    public string? VersionId { get; }
    public string MinecraftVersion { get; }
    public string Loader { get; }
    public string LoaderVersion { get; }
    public int? JavaMajor { get; }
    public int FileCount { get; }
    public int ServerSideCount { get; }
    public int ClientOnlyCount { get; }
    public int UnclearSideCount { get; }
    public string ConfirmableSummary { get; }
    public IReadOnlyList<string> Warnings { get; }
    public bool CanContinue { get; }
    public string? BlockReason { get; }

    /// <summary>Files skipped because the itzg/product list excluded a server-side or unknown-side jar.</summary>
    public int OverrideListSkipCount { get; }

    public IReadOnlyList<string> OverrideListSkipPaths { get; }

    /// <summary>Novice warning when <see cref="OverrideListSkipCount"/> is positive; otherwise null.</summary>
    public string? OverrideListWarning { get; }
}
