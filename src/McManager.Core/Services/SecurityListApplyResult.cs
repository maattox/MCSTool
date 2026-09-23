using McManager.Core.Config;
using Oci.CoreService.Models;

namespace McManager.Core.Services;

public sealed class SecurityListApplyResult
{
    public int PreservedRuleCount { get; init; }
    public int OwnedRuleCount { get; init; }

    public string Summary => "Oracle firewall rules updated.";
}

/// <summary>One GET of subnet Security List ingress, parsed into allowlist rows.</summary>
public sealed class SecurityListAllowlistSnapshot
{
    public IReadOnlyList<FriendEntry> Friends { get; init; } = [];

    public bool NeedsRewrite(
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName)
        => SecurityListIngressPlanner.NeedsRewrite(
            Ingress,
            friends,
            minecraftPort,
            sshPort,
            doorHttpPort,
            adminName);

    internal IReadOnlyList<IngressSecurityRule> Ingress { get; init; } = [];
}
