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

    public const string ClientPackCopy =
        "Friends must install the same exported pack you uploaded. Keep that file "
        + "(Manager saves a copy so you can re-download it later). This product cannot "
        + "rebuild a client pack from mods on the game computer.";

    public static ServiceResult<SetupPackPreview> AnalyzeFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return ServiceResult<SetupPackPreview>.Fail("No pack file was provided.");
        if (!File.Exists(path))
            return ServiceResult<SetupPackPreview>.Fail($"File not found: {path}");

        var ext = Path.GetExtension(path);
        if (ext.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
        {
            var mr = MrpackAnalyzer.AnalyzeFile(path);
            if (!mr.Succeeded)
                return ServiceResult<SetupPackPreview>.Fail(mr.Error!);
            return ServiceResult<SetupPackPreview>.Ok(FromMrpack(mr.Value!, path));
        }

        var manual = ManualServerPackAnalyzer.AnalyzeFile(path);
        if (!manual.Succeeded)
            return ServiceResult<SetupPackPreview>.Fail(manual.Error!);

        var analysis = manual.Value!;
        if (analysis.Kind == ManualServerPackKind.Mrpack)
        {
            var mr = MrpackAnalyzer.AnalyzeFile(path);
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
            analysis.ConfirmableSummary,
            analysis.Warnings,
            canContinue: block is null,
            blockReason: block);
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
            analysis.ConfirmableSummary,
            analysis.Warnings,
            canContinue: block is null,
            blockReason: block);
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
        string? blockReason)
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
}
