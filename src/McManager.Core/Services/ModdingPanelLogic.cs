namespace McManager.Core.Services;

/// <summary>
/// Server tab Mods / Plugins panes: Modded vs Vanilla (Paper), Change pack help, and download-pack copy.
/// Inspect-only in v1 — never treat a zip of VM1 <c>mods/</c> as the client pack.
/// </summary>
public static class ModdingPanelLogic
{
    public const string VanillaEmptyState =
        "This is not a modded server. There is no modpack to download.";

    public const string PaperEmptyState =
        "Paper plugins only. Upload a plugin .jar. Minecraft restarts after upload or delete — do not use /reload.";

    public const string PaperHelpTitle =
        "Paper plugins on the server. Upload and delete restart Minecraft. "
        + "Do not use /reload or a plugin manager.";

    public const string MissingArchiveMessage =
        "The original modpack file is not on this PC. MCSTool cannot rebuild a client pack "
        + "from the mods on the server. Use the file you imported during Setup.";

    public const string VmStoppedHint =
        "Start the server to list its mods.";

    public const string PaperVmStoppedHint =
        "Start the server to list Paper plugins.";

    public const string HelpTitle =
        "Change pack reinstalls Minecraft from a new modpack; the world is kept unless you also wipe it. "
        + "Download pack saves the modpack file from this PC, not a zip of the server's mods. "
        + "If a crash blamed one mod, it is listed here so you can keep it excluded or put it back.";

    public const string AdvancedJarWarning =
        "Adding or deleting a .jar here bypasses automatic checks. "
        + "Minecraft restarts after add or delete.";

    public const string PaneModdingId = "modding";
    public const string PanePluginsId = "plugins";
    public const string PaneChangePackId = "pack";

    public static bool ShowPluginsTab(bool isPaperServer) => isPaperServer;

    /// <summary>
    /// Maps the retired Change pack pane onto Mods, and Plugins onto Mods when the server
    /// cannot load Paper plugins.
    /// </summary>
    public static string NormalizeServerPane(string? pane, bool isPaperServer)
    {
        var id = (pane ?? "").Trim();
        if (id.Length == 0)
            return "";
        if (string.Equals(id, PaneChangePackId, StringComparison.Ordinal))
            return PaneModdingId;
        if (string.Equals(id, PanePluginsId, StringComparison.Ordinal) && !isPaperServer)
            return PaneModdingId;
        return id;
    }

    public static bool IsModdedServerKind(string? serverKind)
    {
        var id = (serverKind ?? "").Trim().ToLowerInvariant();
        return id is "modded"
            or "fabric"
            or "forge"
            or "neoforge"
            or "quilt";
    }

    public static bool IsPaperServerKind(string? serverKind)
    {
        var id = (serverKind ?? "").Trim().ToLowerInvariant();
        return id is "paper";
    }

    public static bool CanDownloadPack(bool isModded, bool hasLocalArchive) =>
        isModded && hasLocalArchive;

    public static string DownloadDisabledReason(bool isModded, bool hasLocalArchive)
    {
        if (!isModded)
            return VanillaEmptyState;
        if (!hasLocalArchive)
            return MissingArchiveMessage;
        return "";
    }
}
