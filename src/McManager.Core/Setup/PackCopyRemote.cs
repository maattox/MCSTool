namespace McManager.Core.Setup;

/// <summary>
/// Remote apply of a staged server-pack tree. Replaces <c>mods/</c> so a retry
/// cannot keep client-only jars skipped on a later analyze (SETUP-ISSUE-17).
/// </summary>
public static class PackCopyRemote
{
    public const string ServerDir = "/opt/mcmgr/server";
    public const string ModsDir = ServerDir + "/mods";

    public static string ApplyStagedTreeCommand(string remoteStaging, string onboxStaging)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remoteStaging);
        ArgumentException.ThrowIfNullOrWhiteSpace(onboxStaging);
        return "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + "systemctl stop minecraft || true; "
            + "rm -rf " + ModsDir + "; "
            + "mkdir -p " + ModsDir + "; "
            + "cp -a " + remoteStaging + "/. " + ServerDir + "/; "
            + "bash " + onboxStaging + "/repair-permissions.sh; "
            + "systemctl start minecraft";
    }
}
