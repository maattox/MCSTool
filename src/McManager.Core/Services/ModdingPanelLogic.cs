namespace McManager.Core.Services;

/// <summary>
/// Server Management Modding section: Modded vs Vanilla/Paper, and download-pack copy.
/// Inspect-only in v1 — never treat a zip of VM1 <c>mods/</c> as the client pack.
/// </summary>
public static class ModdingPanelLogic
{
    public const string VanillaEmptyState =
        "This is not a modded server. There is no imported pack to download.";

    public const string MissingArchiveMessage =
        "The original pack file is not on this PC. Manager cannot rebuild a client pack "
        + "from the mods on the server. Use the file you imported during Setup.";

    public const string VmStoppedHint =
        "Start the game VM to list the mods currently on the server.";

    public const string HelpTitle =
        "Lists server-side mods on the game VM. Download pack copies the original file you "
        + "imported in Setup (saved on this PC). That is not a zip of the server mods folder "
        + "— Setup strips client-only files, so a server-side zip would not work for friends. "
        + "Change pack reinstalls Minecraft from a new .mrpack or server-pack zip; "
        + "the world is kept unless you also wipe.";

    public static bool IsModdedServerKind(string? serverKind)
    {
        var id = (serverKind ?? "").Trim().ToLowerInvariant();
        return id is "modded"
            or "fabric"
            or "forge"
            or "neoforge"
            or "quilt";
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
