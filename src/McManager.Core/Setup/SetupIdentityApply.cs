namespace McManager.Core.Setup;

/// <summary>
/// Minecraft loads <c>server-icon.png</c> and MOTD at process start
/// (<c>record_boot</c> Before=<c>minecraft</c>). Setup seeds Object Storage at
/// OsMeta, which is after that first start for Paper, vanilla, and modded.
/// </summary>
public static class SetupIdentityApply
{
    public const string RestartLog =
        "Restarting Minecraft so the list name and icon apply…";
}
