namespace McManager.Core.Setup;

/// <summary>
/// Server Management <c>Change pack</c> copy and button gates (full re-setup; light swap parked).
/// </summary>
public static class PackReplaceUx
{
    public const long PackDropMaxBytes = 512L * 1024 * 1024;

    public const string StartFirstMessage =
        "Start the server first, then change the pack. Change pack reinstalls Minecraft over SSH.";

    public const string ConfirmTitle = "Reinstall Minecraft from this pack?";

    public const string PackConfirmLabel =
        "Use this pack on the server (server-side mods only; client-only files are skipped).";

    public const string WipeWorldLabel =
        "Also wipe the world (a new world will generate). Leave unchecked to keep the current world.";

    public const string IdleForceEnableNote =
        "Minecraft start turns the idle timer back on.";

    public const string ConfirmKeepWorld =
        "This reinstalls Minecraft on the server from the file you chose. "
        + "The world is kept unless you also wipe. Friends need the new exported pack on their PCs.";

    public const string ConfirmWipeWorld =
        "This reinstalls Minecraft on the server from the file you chose. "
        + "The live world will be deleted. Cloud backups stay. This cannot be undone except by restoring a backup.";

    public static string ConfirmBody(bool wipeWorld) =>
        wipeWorld ? ConfirmWipeWorld : ConfirmKeepWorld;

    public static bool CanPick(bool vm1Running, bool busy) =>
        vm1Running && !busy;

    public static bool FreezeAllowsContinue(string? freezeBlockReason) =>
        string.IsNullOrWhiteSpace(freezeBlockReason);

    public static bool CanInstall(
        bool vm1Running,
        bool busy,
        bool canContinue,
        bool packConfirmed,
        bool clientPackAcknowledged,
        bool identityComplete = true,
        string? freezeBlockReason = null) =>
        vm1Running
        && !busy
        && canContinue
        && packConfirmed
        && clientPackAcknowledged
        && identityComplete
        && FreezeAllowsContinue(freezeBlockReason);

    public static string PickDisabledReason(bool vm1Running, bool busy)
    {
        if (busy)
            return "Wait until the current action finishes.";
        if (!vm1Running)
            return StartFirstMessage;
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
        if (busy)
            return "Wait until the current action finishes.";
        if (!vm1Running)
            return StartFirstMessage;
        if (!canContinue)
            return "Choose a pack that can be installed first.";
        if (!FreezeAllowsContinue(freezeBlockReason))
            return freezeBlockReason!.Trim();
        if (!identityComplete)
            return DerivedPackIdentity.IdentityIncompleteReason;
        if (!packConfirmed || !clientPackAcknowledged)
            return "Confirm the pack and that friends will get the same file.";
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
