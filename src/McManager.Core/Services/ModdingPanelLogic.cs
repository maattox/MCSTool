namespace McManager.Core.Services;

/// <summary>
/// Server tab Mods / Plugins panes: Modded vs Vanilla/Paper, Change pack help, and download-pack copy.
/// Inspect-only in v1 — never treat a zip of VM1 <c>mods/</c> as the client pack.
/// </summary>
public static class ModdingPanelLogic
{
    public const string VanillaEmptyState =
        "This is not a modded server. There is no imported pack to download.";

    public const string PaperEmptyState =
        "Paper plugins only. Upload a .jar to plugins/ on the game VM. Minecraft restarts after upload or delete — do not use /reload.";

    public const string PaperHelpTitle =
        "Paper plugins on the game VM (plugins/). Upload and delete restart Minecraft. "
        + "Do not use /reload or a plugin manager. This is not a Hangar or Modrinth catalog.";

    public const string MissingArchiveMessage =
        "The original pack file is not on this PC. Manager cannot rebuild a client pack "
        + "from the mods on the server. Use the file you imported during Setup.";

    public const string VmStoppedHint =
        "Start the game VM to list the mods currently on the server.";

    public const string PaperVmStoppedHint =
        "Start the game VM to list Paper plugins.";

    public const string HelpTitle =
        "Change pack reinstalls Minecraft from a new .mrpack or server-pack zip; the world is "
        + "kept unless you also wipe. On a modded server, this tab also lists server-side mods "
        + "on the game VM. Download pack copies the confirmed pack file saved on this PC (with "
        + "manifest added for jar-root zips when you corrected versions). That is not a zip of "
        + "the server mods folder — Setup strips client-only files, so a server-side zip would "
        + "not work for players. If a crash blamed exactly one mod, it is listed here so you "
        + "can keep it excluded or put it back. Advanced can add or delete a single .jar without "
        + "pack review; that bypasses automatic pack checks and restarts Minecraft.";

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
