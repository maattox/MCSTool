namespace McManager.Core.Setup;

/// <summary>
/// Server Management <c>Change pack</c> copy and button gates (full re-setup; light swap parked).
/// </summary>
public static class PackReplaceUx
{
    public const long PackDropMaxBytes = 512L * 1024 * 1024;

    public const string ConfirmTitle = "Reinstall Minecraft from this pack?";

    public const string DropTitle = "Drop a mod pack here";

    public const string DropFormats =
        "Modrinth .mrpack, CurseForge Server Pack .zip, or unstructured .jar zip.";

    public const string DropLargeHint = "Large packs: Choose pack file.";

    public const string SkipWarningBody =
        "Known client-only mods will automatically be skipped. Check the list below and confirm that all client-only mods are correctly marked.";

    /// <summary>
    /// <see cref="SkipWarningBody"/> is for the assisted-review list. A fully handled
    /// <c>.mrpack</c> with override-list skips and no list must not show it.
    /// </summary>
    public static bool ShouldShowSkipListWarning(bool assistedReviewVisible) =>
        assistedReviewVisible;

    public const string ChangePackPickHint =
        "Reinstall Minecraft from a .mrpack or server-pack zip. The world is kept unless wipe is checked.";

    public const string PackConfirmLabel =
        "Use this pack on the server. Client-only mods are skipped.";

    public const string WipeWorldLabel =
        "Also wipe the world (irreversible). Cloud backups stay. Leave unchecked to keep the current world.";

    public const string IdleForceEnableNote =
        "Minecraft start turns the idle timer back on.";

    public const string ConfirmKeepWorld =
        "Reinstalls Minecraft from the chosen file. "
        + "If the game VM is stopped, it is started first. "
        + "The world is kept unless wipe is checked.";

    public const string ConfirmWipeWorld =
        "Reinstalls Minecraft from the chosen file. "
        + "If the game VM is stopped, it is started first. "
        + "The live world will be deleted. Cloud backups stay. Irreversible except by restoring a backup.";

    public static string ConfirmBody(bool wipeWorld) =>
        wipeWorld ? ConfirmWipeWorld : ConfirmKeepWorld;

    /// <param name="vm1Running">Ignored. Pick, drop, analyze, and review work while VM1 is stopped.</param>
    public static bool CanPick(bool vm1Running, bool busy)
    {
        _ = vm1Running;
        return !busy;
    }

    public static bool FreezeAllowsContinue(string? freezeBlockReason) =>
        string.IsNullOrWhiteSpace(freezeBlockReason);

    /// <param name="vm1Running">Ignored. Install starts VM1 when it is stopped.</param>
    public static bool CanInstall(
        bool vm1Running,
        bool busy,
        bool canContinue,
        bool packConfirmed,
        bool clientPackAcknowledged,
        bool identityComplete = true,
        string? freezeBlockReason = null)
    {
        _ = vm1Running;
        _ = clientPackAcknowledged;
        return !busy
            && canContinue
            && packConfirmed
            && identityComplete
            && FreezeAllowsContinue(freezeBlockReason);
    }

    public static string PickDisabledReason(bool vm1Running, bool busy)
    {
        _ = vm1Running;
        if (busy)
            return "Wait until the current action finishes.";
        return "";
    }

    public static string InstallDisabledReason(
        bool vm1Running,
        bool busy,
        bool canContinue,
        bool packConfirmed,
        bool clientPackAcknowledged,
        bool identityComplete = true,
        string? freezeBlockReason = null)
    {
        _ = vm1Running;
        _ = clientPackAcknowledged;
        if (busy)
            return "Wait until the current action finishes.";
        if (!canContinue)
            return "Choose a pack that can be installed first.";
        if (!FreezeAllowsContinue(freezeBlockReason))
            return freezeBlockReason!.Trim();
        if (!identityComplete)
            return DerivedPackIdentity.IdentityIncompleteReason;
        if (!packConfirmed)
            return "Confirm the pack.";
        return "";
    }

    /// <summary>Save-compat warning is hidden when the world will be wiped.</summary>
    public static string? VisibleSaveCompatibilityWarning(bool wipeWorld, string? warning) =>
        wipeWorld || string.IsNullOrWhiteSpace(warning) ? null : warning;

    /// <summary>Object Storage <c>game.server_kind</c> after a successful pack replace.</summary>
    public static string ServerKindForMeta(string? loader) =>
        SetupPackImport.IsInstallableLoader(loader) ? SetupServerType.Modded : (loader ?? "").Trim();

    public static string SuccessMessage(PackReplaceResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var loader = SetupPackImport.DisplayLoader(result.Loader);
        var identity = string.IsNullOrWhiteSpace(loader)
            ? result.PackName
            : $"{result.PackName} ({loader})";
        var wipe = result.WipedWorld ? " The world was wiped." : " The world was kept.";
        var warn = string.IsNullOrWhiteSpace(result.SaveCompatibilityWarning)
            ? ""
            : " " + result.SaveCompatibilityWarning.Trim();
        var q = string.IsNullOrWhiteSpace(result.QuarantineNotice)
            ? ""
            : " " + result.QuarantineNotice.Trim();
        return $"Installed {identity}, Minecraft {result.MinecraftVersion}.{wipe}{warn}{q} {IdleForceEnableNote}";
    }
}
