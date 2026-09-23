using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Server → Settings <c>Change server type</c> copy and gates (no tofu / Deploy).
/// </summary>
public static class ChangeServerTypeUx
{
    public const string ChoicePaper = "optimized";
    public const string ChoiceModded = "modded";

    public const string SectionTitle = "Change server type";

    public const string OpenButton = "Change type…";

    public const string SectionHelp =
        "Reinstalls Minecraft as Vanilla (Paper) or Modded on this game VM. "
        + "The doorbell VM and play IP don't change. "
        + "The world is kept unless you check wipe. Going from Modded back to Vanilla (Paper) "
        + "can lose mod blocks and items.";

    public const string ModalTitle = "Change server type";

    public const string ConfirmTitle = "Reinstall Minecraft?";

    public const string PrimaryAction = "Continue";

    public const string ConfirmButton = "Reinstall";

    public const string LabelPaper = SetupVanillaFlavor.PlanLabelPaper;

    public const string LabelModded = "Modded";

    public const string WipeWorldLabel = PackReplaceUx.WipeWorldLabel;

    public const string MissingPackError =
        "Modded needs a modpack. Drop a .mrpack or .zip file.";

    public const string MissingVersionError = "Choose a Minecraft version.";

    public const string PackNeedsReview =
        "This modpack needs a review on the Mods tab first (unknown mods or versions).";

    public const string AnyToModdedNote =
        "After mods run, going back to Vanilla (Paper) can lose mod blocks and items.";

    public const string ModdedToVanillaStrong =
        "Blocks and items from the old mods will be missing from the world. "
        + "Download a world save first if that world matters.";

    public const string ConfirmKeepWorld =
        "Reinstalls Minecraft on this game VM. The doorbell VM and play IP don't change. "
        + "Nothing else in Oracle Cloud is recreated. The world is kept unless wipe is checked.";

    public const string ConfirmWipeWorld =
        "Reinstalls Minecraft on this game VM. The doorbell VM and play IP don't change. "
        + "Nothing else in Oracle Cloud is recreated. The live world will be deleted, including the Nether, the End, and any other dimensions. "
        + "The player map and its pins are cleared. Cloud backups are kept. "
        + "This can't be undone except by restoring a backup.";

    public static string ConfirmBody(bool wipeWorld) =>
        wipeWorld ? ConfirmWipeWorld : ConfirmKeepWorld;

    public static string NormalizeChoice(string? value)
    {
        var id = (value ?? "").Trim().ToLowerInvariant();
        if (id is ChoiceModded)
            return ChoiceModded;
        return ChoicePaper;
    }

    public static bool IsModdedChoice(string? value) =>
        string.Equals(NormalizeChoice(value), ChoiceModded, StringComparison.Ordinal);

    public static bool IsPaperChoice(string? value) =>
        string.Equals(NormalizeChoice(value), ChoicePaper, StringComparison.Ordinal);

    public static string ChoiceLabel(string? value) =>
        NormalizeChoice(value) switch
        {
            ChoiceModded => LabelModded,
            _ => LabelPaper,
        };

    /// <summary>Novice label for the live <c>game.server_kind</c>.</summary>
    public static string KindLabel(string? serverKind)
    {
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return LabelModded;
        if (string.IsNullOrWhiteSpace(serverKind))
            return "—";
        return LabelPaper;
    }

    public static string ChoiceFromServerKind(string? serverKind)
    {
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return ChoiceModded;
        return ChoicePaper;
    }

    public static string ServerKindForMeta(string? targetChoice, string? packLoader)
    {
        var choice = NormalizeChoice(targetChoice);
        if (choice == ChoiceModded)
            return PackReplaceUx.ServerKindForMeta(packLoader);
        return SetupVanillaFlavor.DistributionPaper;
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
        return null;
    }

    public static string SuccessMessage(ChangeServerTypeResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var kind = KindLabel(result.ServerKind);
        var wipe = result.WipedWorld
            ? string.IsNullOrWhiteSpace(result.SharedMapWarning)
                ? " The world was wiped. The player map was cleared."
                : " The world was wiped. " + result.SharedMapWarning.Trim()
            : " The world was kept.";
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
        if (ModdingPanelLogic.IsModdedServerKind(serverKind))
            return "modded";
        if (string.IsNullOrWhiteSpace(serverKind))
            return "";
        return "paper";
    }

    private static string KindGroupFromChoice(string? targetChoice) =>
        NormalizeChoice(targetChoice) switch
        {
            ChoiceModded => "modded",
            _ => "paper",
        };
}
