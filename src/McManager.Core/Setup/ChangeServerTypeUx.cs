using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Server → Settings <c>Change server type</c> copy and gates (no tofu / Deploy).
/// </summary>
public static class ChangeServerTypeUx
{
    public const string ChoiceDefaultVanilla = "default";
    public const string ChoicePaper = "optimized";
    public const string ChoiceModded = "modded";

    public const string SectionTitle = "Change server type";

    public const string OpenButton = "Change type…";

    public const string SectionHelp =
        "Reinstalls Minecraft as Default Vanilla, Paper, or Modded on this existing VM. "
        + "The cloud stack, doorbell, and play IP stay. This is not a cloud Redeploy. "
        + "The world is kept unless you check wipe. Going from Modded back to Vanilla or Paper "
        + "can lose mod blocks and items.";

    public const string ModalTitle = "Change server type";

    public const string ConfirmTitle = "Reinstall Minecraft on this VM?";

    public const string PrimaryAction = "Continue";

    public const string ConfirmButton = "Reinstall";

    public const string LabelDefaultVanilla = "Default Vanilla";

    public const string LabelPaper = "Optimized Vanilla (Paper)";

    public const string LabelModded = "Modded";

    public const string WipeWorldLabel = PackReplaceUx.WipeWorldLabel;

    public const string MissingPackError =
        "Modded needs a pack file. Drop a .mrpack or server-pack zip.";

    public const string MissingVersionError = "Choose a Minecraft version.";

    public const string PackNeedsReview =
        "This pack needs review on Mods → Change pack (unknown jars or version fields).";

    public const string VanillaPaperMild =
        "Switching between Default Vanilla and Paper usually keeps the world. "
        + "Some Paper-only settings may reset.";

    public const string AnyToModdedNote =
        "Players will need this pack to join. After mods run, going back to Vanilla or Paper "
        + "can lose mod blocks and items.";

    public const string ModdedToVanillaStrong =
        "Blocks and items from the old mods will be missing from the world. "
        + "Download a world save first if that world matters.";

    public const string ConfirmKeepWorld =
        "Reinstalls Minecraft on the existing game VM. The OCI stack, doorbell, and play IP stay the same. "
        + "This is not a cloud Redeploy. The world is kept unless wipe is checked.";

    public const string ConfirmWipeWorld =
        "Reinstalls Minecraft on the existing game VM. The OCI stack, doorbell, and play IP stay the same. "
        + "This is not a cloud Redeploy. The live world will be deleted. Cloud backups stay. "
        + "Irreversible except by restoring a backup.";

    public static string ConfirmBody(bool wipeWorld) =>
        wipeWorld ? ConfirmWipeWorld : ConfirmKeepWorld;

    public static string NormalizeChoice(string? value)
    {
        var id = (value ?? "").Trim().ToLowerInvariant();
        if (id is ChoicePaper or "paper")
            return ChoicePaper;
        if (id is ChoiceModded)
            return ChoiceModded;
        return ChoiceDefaultVanilla;
    }

    public static bool IsModdedChoice(string? value) =>
        string.Equals(NormalizeChoice(value), ChoiceModded, StringComparison.Ordinal);

    public static bool IsPaperChoice(string? value) =>
        string.Equals(NormalizeChoice(value), ChoicePaper, StringComparison.Ordinal);

    public static string ChoiceLabel(string? value) =>
        NormalizeChoice(value) switch
        {
            ChoicePaper => LabelPaper,
            ChoiceModded => LabelModded,
            _ => LabelDefaultVanilla,
        };

    /// <summary>Novice label for the live <c>game.server_kind</c>.</summary>
    public static string KindLabel(string? serverKind)
    {
        if (ModdingPanelLogic.IsPaperServerKind(serverKind))
            return LabelPaper;
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return LabelModded;
        if (string.IsNullOrWhiteSpace(serverKind))
            return "—";
        return LabelDefaultVanilla;
    }

    public static string ChoiceFromServerKind(string? serverKind)
    {
        if (ModdingPanelLogic.IsPaperServerKind(serverKind))
            return ChoicePaper;
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return ChoiceModded;
        return ChoiceDefaultVanilla;
    }

    public static string ServerKindForMeta(string? targetChoice, string? packLoader)
    {
        var choice = NormalizeChoice(targetChoice);
        if (choice == ChoiceModded)
            return PackReplaceUx.ServerKindForMeta(packLoader);
        return SetupVanillaFlavor.ToDistribution(
            choice == ChoicePaper ? SetupVanillaFlavor.Optimized : SetupVanillaFlavor.Default);
    }

    /// <summary>
    /// Direction copy for the modal. Independent of wipe (wipe has its own checkbox).
    /// </summary>
    public static string? DirectionWarning(string? currentKind, string? targetChoice)
    {
        var from = KindGroup(currentKind);
        var to = KindGroupFromChoice(targetChoice);
        if (from.Length == 0 || from == to)
            return null;
        if (to == "modded")
            return AnyToModdedNote;
        if (from == "modded")
            return ModdedToVanillaStrong;
        return VanillaPaperMild;
    }

    public static string SuccessMessage(ChangeServerTypeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var kind = KindLabel(result.ServerKind);
        var wipe = result.WipedWorld ? " The world was wiped." : " The world was kept.";
        var warn = string.IsNullOrWhiteSpace(result.SaveCompatibilityWarning)
            ? ""
            : " " + result.SaveCompatibilityWarning.Trim();
        var q = string.IsNullOrWhiteSpace(result.QuarantineNotice)
            ? ""
            : " " + result.QuarantineNotice.Trim();
        return $"Now {kind}, Minecraft {result.MinecraftVersion}.{wipe} "
            + $"The play IP was not changed.{warn}{q} {PackReplaceUx.IdleForceEnableNote}";
    }

    private static string KindGroup(string? serverKind)
    {
        if (ModdingPanelLogic.IsPaperServerKind(serverKind))
            return "paper";
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return "modded";
        var id = (serverKind ?? "").Trim().ToLowerInvariant();
        if (id.Length == 0)
            return "";
        return "vanilla";
    }

    private static string KindGroupFromChoice(string? targetChoice) =>
        NormalizeChoice(targetChoice) switch
        {
            ChoicePaper => "paper",
            ChoiceModded => "modded",
            _ => "vanilla",
        };
}
