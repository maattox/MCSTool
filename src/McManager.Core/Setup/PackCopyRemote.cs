namespace McManager.Core.Setup;

/// <summary>
/// Remote apply of a staged server-pack tree. Replaces <c>mods/</c> so a retry
/// cannot keep client-only jars skipped on a later analyze (SETUP-ISSUE-17).
/// </summary>
public static class PackCopyRemote
{
    public const string ServerDir = "/opt/mcmgr/server";
    public const string ModsDir = ServerDir + "/mods";
    public const string JvmArgsFile = "user_jvm_args.txt";

    /// <remarks>
    /// The bootstrap already wrote server memory into <c>user_jvm_args.txt</c>; a pack copy
    /// (often <c>-Xmx10G</c> or more) must not replace it.
    /// </remarks>
    public static string ApplyStagedTreeCommand(string remoteStaging, string onboxStaging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteStaging);
        ArgumentException.ThrowIfNullOrWhiteSpace(onboxStaging);
        return "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + "systemctl stop minecraft || true; "
            + "if [ -f " + ServerDir + "/" + JvmArgsFile + " ]; then rm -f " + remoteStaging + "/" + JvmArgsFile + "; fi; "
            + "rm -rf " + ModsDir + "; "
            + "mkdir -p " + ModsDir + "; "
            + "cp -a " + remoteStaging + "/. " + ServerDir + "/; "
            + "bash " + onboxStaging + "/repair-permissions.sh; "
            + "systemctl start minecraft";
    }
}
