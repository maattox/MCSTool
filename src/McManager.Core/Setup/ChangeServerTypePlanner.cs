using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Day-2 Vanilla / Paper / Modded switch on an existing VM (blueprint §12.3 / §28.1).
/// No tofu. SSH install is <see cref="SetupBootstrapService.ChangeServerTypeAsync"/>.
/// </summary>
public static class ChangeServerTypePlanner
{
    public static ServiceResult<ChangeServerTypePlan> TryCreate(
        string? targetChoice,
        string? minecraftVersion,
        string? packPath,
        bool wipeWorld,
        string? currentMinecraftVersion,
        string? currentLoaderOrDistribution)
    {
        var choice = ChangeServerTypeUx.NormalizeChoice(targetChoice);
        if (choice == ChangeServerTypeUx.ChoiceModded)
        {
            if (string.IsNullOrWhiteSpace(packPath))
                return ServiceResult<ChangeServerTypePlan>.Fail(ChangeServerTypeUx.MissingPackError);

            var packPlan = PackReplacePlanner.TryCreate(
                packPath,
                wipeWorld,
                currentMinecraftVersion,
                currentLoaderOrDistribution);
            if (!packPlan.Succeeded || packPlan.Value is null)
                return ServiceResult<ChangeServerTypePlan>.Fail(
                    packPlan.Error ?? ChangeServerTypeUx.MissingPackError);

            var preview = packPlan.Value.Preview;
            return ServiceResult<ChangeServerTypePlan>.Ok(
                new ChangeServerTypePlan(
                    choice,
                    preview.MinecraftVersion,
                    wipeWorld,
                    preview,
                    packPlan.Value.SaveCompatibilityWarning));
        }

        var version = (minecraftVersion ?? "").Trim();
        if (version.Length == 0)
            return ServiceResult<ChangeServerTypePlan>.Fail(ChangeServerTypeUx.MissingVersionError);

        var newLoader = choice == ChangeServerTypeUx.ChoicePaper
            ? SetupVanillaFlavor.DistributionPaper
            : SetupVanillaFlavor.DistributionVanilla;
        var warning = wipeWorld
            ? null
            : PackReplaceSaveCompatibility.Warn(
                currentMinecraftVersion,
                currentLoaderOrDistribution,
                version,
                newLoader);

        return ServiceResult<ChangeServerTypePlan>.Ok(
            new ChangeServerTypePlan(choice, version, wipeWorld, preview: null, warning));
    }

    public static SetupWizardState ToWizardState(ChangeServerTypePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        if (plan.Preview is not null)
            return PackReplacePlanner.ToWizardState(plan.Preview);

        return new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            VanillaFlavor = plan.TargetChoice == ChangeServerTypeUx.ChoicePaper
                ? SetupVanillaFlavor.Optimized
                : SetupVanillaFlavor.Default,
            EulaAccepted = true,
            MinecraftVersion = plan.MinecraftVersion,
            PackConfirmed = true,
            ClientPackAcknowledged = true,
        };
    }
}

public sealed class ChangeServerTypePlan
{
    public ChangeServerTypePlan(
        string targetChoice,
        string minecraftVersion,
        bool wipeWorld,
        SetupPackPreview? preview,
        string? saveCompatibilityWarning)
    {
        TargetChoice = ChangeServerTypeUx.NormalizeChoice(targetChoice);
        MinecraftVersion = minecraftVersion;
        WipeWorld = wipeWorld;
        Preview = preview;
        SaveCompatibilityWarning = saveCompatibilityWarning;
    }

    public string TargetChoice { get; }
    public string MinecraftVersion { get; }
    public bool WipeWorld { get; }
    public SetupPackPreview? Preview { get; }
    public string? SaveCompatibilityWarning { get; }
}

public sealed class ChangeServerTypeRequest
{
    public ChangeServerTypeRequest(
        string targetChoice,
        string minecraftVersion,
        bool wipeWorld,
        string? packPath = null,
        string? dataDirectory = null)
    {
        TargetChoice = targetChoice;
        MinecraftVersion = minecraftVersion;
        WipeWorld = wipeWorld;
        PackPath = packPath;
        DataDirectory = dataDirectory;
    }

    public string TargetChoice { get; }
    public string MinecraftVersion { get; }
    public bool WipeWorld { get; }
    public string? PackPath { get; }
    public string? DataDirectory { get; }
}

public sealed class ChangeServerTypeResult
{
    public ChangeServerTypeResult(
        string serverKind,
        string minecraftVersion,
        bool wipedWorld,
        string? packName = null,
        string? loader = null,
        string? saveCompatibilityWarning = null,
        string? quarantineNotice = null)
    {
        ServerKind = serverKind;
        MinecraftVersion = minecraftVersion;
        WipedWorld = wipedWorld;
        PackName = packName;
        Loader = loader;
        SaveCompatibilityWarning = saveCompatibilityWarning;
        QuarantineNotice = quarantineNotice;
    }

    public string ServerKind { get; }
    public string MinecraftVersion { get; }
    public bool WipedWorld { get; }
    public string? PackName { get; }
    public string? Loader { get; }
    public string? SaveCompatibilityWarning { get; }
    public string? QuarantineNotice { get; }
}
