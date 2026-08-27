using McManager.Core.Setup;

namespace McManager.PackTestHarness;

internal static class FailClassifier
{
    public static PackVerdict FromReplaceError(string? error)
    {
        var e = error ?? "";
        if (e.Length == 0)
            return PackVerdict.ProductFail;

        if (e.Contains("RCON list did not succeed in time", StringComparison.OrdinalIgnoreCase)
            || e.Contains(MinecraftReadiness.TimeoutHeadline, StringComparison.OrdinalIgnoreCase))
            return PackVerdict.Timeout;

        if (LooksInfra(e))
            return PackVerdict.InfraFail;

        return PackVerdict.ProductFail;
    }

    public static bool LooksInfra(string error)
    {
        ReadOnlySpan<string> needles =
        [
            "SSH key not found",
            "SSH host is missing",
            "Connection refused",
            "Connection failed",
            "Connection timed out",
            "connection was aborted",
            "connection attempt failed",
            "failed to respond",
            "forcibly closed",
            "Connection reset by peer",
            "WSAECONN",
            "Authentication",
            "Permission denied (publickey",
            "No route to host",
            "Network is unreachable",
            "Could not connect",
            "SocketException",
            "SshConnection",
            "VM1 SSH",
            "VM1 is not RUNNING",
            "STOPPED",
            "STOPPING",
            "GetInstance",
            "Product onbox/mcmgr/ not found",
            "instance_id is empty",
            "OCI ",
        ];
        foreach (var n in needles)
        {
            if (error.Contains(n, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
