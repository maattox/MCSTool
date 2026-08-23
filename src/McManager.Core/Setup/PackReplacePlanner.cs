using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Day-2 full pack replace (blueprint §28.1 full path). Light swap is parked.
/// Analyze + save-compat warning only — SSH install is <see cref="SetupBootstrapService.ReplacePackAsync"/>.
/// </summary>
public static class PackReplacePlanner
{
    public static ServiceResult<PackReplacePlan> TryCreate(
        string packPath,
        bool wipeWorld,
        string? currentMinecraftVersion,
        string? currentLoaderOrDistribution)
    {
        var analysis = SetupPackImport.AnalyzeFile(packPath);
        if (!analysis.Succeeded)
            return ServiceResult<PackReplacePlan>.Fail(analysis.Error!);

        var preview = analysis.Value!;
        if (!preview.CanContinue)
        {
            return ServiceResult<PackReplacePlan>.Fail(
                preview.BlockReason ?? "This pack cannot be installed.");
        }

        var warning = wipeWorld
            ? null
            : PackReplaceSaveCompatibility.Warn(
                currentMinecraftVersion,
                currentLoaderOrDistribution,
                preview.MinecraftVersion,
                preview.Loader);

        return ServiceResult<PackReplacePlan>.Ok(
            new PackReplacePlan(preview, wipeWorld, warning));
    }

    public static SetupWizardState ToWizardState(SetupPackPreview preview)
    {
        ArgumentNullException.ThrowIfNull(preview);
        return new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            EulaAccepted = true,
            MinecraftVersion = preview.MinecraftVersion,
            PackPath = preview.SourcePath,
            PackKind = preview.Kind,
            PackName = preview.PackName,
            PackVersionId = preview.VersionId ?? "",
            PackLoader = preview.Loader,
            PackLoaderVersion = preview.LoaderVersion,
            PackJavaMajor = preview.JavaMajor,
            PackSummary = preview.ConfirmableSummary,
            PackConfirmed = true,
            ClientPackAcknowledged = true,
        };
    }
}

/// <summary>Confirmed full re-setup of Minecraft from a local pack file.</summary>
public sealed class PackReplacePlan
{
    public PackReplacePlan(SetupPackPreview preview, bool wipeWorld, string? saveCompatibilityWarning)
    {
        Preview = preview;
        WipeWorld = wipeWorld;
        SaveCompatibilityWarning = saveCompatibilityWarning;
    }

    public SetupPackPreview Preview { get; }
    public bool WipeWorld { get; }
    public string? SaveCompatibilityWarning { get; }
}

public sealed class PackReplaceRequest
{
    public PackReplaceRequest(string packPath, bool wipeWorld, string? dataDirectory = null)
    {
        PackPath = packPath;
        WipeWorld = wipeWorld;
        DataDirectory = dataDirectory;
    }

    public string PackPath { get; }
    public bool WipeWorld { get; }
    public string? DataDirectory { get; }
}

public sealed class PackReplaceResult
{
    public PackReplaceResult(
        string packName,
        string minecraftVersion,
        string loader,
        bool wipedWorld,
        string? saveCompatibilityWarning,
        string? quarantineNotice = null)
    {
        PackName = packName;
        MinecraftVersion = minecraftVersion;
        Loader = loader;
        WipedWorld = wipedWorld;
        SaveCompatibilityWarning = saveCompatibilityWarning;
        QuarantineNotice = quarantineNotice;
    }

    public string PackName { get; }
    public string MinecraftVersion { get; }
    public string Loader { get; }
    public bool WipedWorld { get; }
    public string? SaveCompatibilityWarning { get; }
    public string? QuarantineNotice { get; }
}
