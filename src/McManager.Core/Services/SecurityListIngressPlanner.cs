using McManager.Core.Config;
using Oci.CoreService.Models;

namespace McManager.Core.Services;

/// <summary>
/// Builds the full Security List ingress set for one rewrite.
/// <see cref="Oci.CoreService.Requests.UpdateSecurityListRequest"/> replaces the entire
/// ingress list — preserve ICMP and other non-owned rules; never emit SSH/door from
/// <c>0.0.0.0/0</c>. Minecraft is private allowlist only (CIDR or <c>/32</c>).
/// Leftover world-open Minecraft rules and leftover friend Minecraft prefixes
/// (TCP+UDP, <c>/9</c>–<c>/31</c>) are treated as managed and stripped.
/// Door <c>wait_forge</c> is TCP-only from the subnet CIDR and must stay.
/// </summary>
public static class SecurityListIngressPlanner
{
    public const string ProtocolTcp = "6";
    public const string ProtocolUdp = "17";

    public static SecurityListIngressPlan Build(
        IEnumerable<IngressSecurityRule> existing,
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName)
    {
        var existingList = existing as IReadOnlyList<IngressSecurityRule> ?? existing.ToList();
        var ownedDescriptions = friends
            .SelectMany(f => new[] { f.Name.Trim(), f.Ip.Trim() })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToHashSet(StringComparer.Ordinal);

        var preserved = new List<IngressSecurityRule>();
        foreach (var rule in existingList)
        {
            var desc = rule.Description ?? "";
            if (FriendRules.IsOwnedDescription(desc, ownedDescriptions))
                continue;

            if (IsManagedRule(rule, minecraftPort, sshPort, doorHttpPort, existingList))
                continue;

            preserved.Add(rule);
        }

        var owned = BuildOwnedRules(
            friends,
            minecraftPort,
            sshPort,
            doorHttpPort,
            adminName);

        return new SecurityListIngressPlan
        {
            Preserved = preserved,
            Owned = owned,
        };
    }

    internal static bool IsManagedRule(
        IngressSecurityRule rule,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        IReadOnlyList<IngressSecurityRule>? existing = null)
    {
        var desc = rule.Description ?? "";
        if (FriendRules.IsOwnedDescription(desc))
            return false;

        var proto = rule.Protocol ?? "";
        if (IsMinecraftPort(rule, proto, minecraftPort)
            && FriendRules.IsWorldOpenCidr(rule.Source))
        {
            return true;
        }

        if (IsLeftoverMinecraftPrefix(rule, proto, minecraftPort, existing))
            return true;

        if (!FriendRules.IsSingleHostCidr(rule.Source))
            return false;

        if (proto == ProtocolTcp)
        {
            var port = rule.TcpOptions?.DestinationPortRange?.Min;
            if (port == minecraftPort)
                return true;
            if (port == sshPort && !FriendRules.IsWorldOpenCidr(rule.Source))
                return true;
            if (port == doorHttpPort)
                return true;
        }

        if (proto == ProtocolUdp)
        {
            var port = rule.UdpOptions?.DestinationPortRange?.Min;
            if (port == minecraftPort)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Friend Advanced CIDRs are named (not <c>mc-whitelist:</c>) and are TCP+UDP.
    /// <c>wait_forge</c> is the same prefix width but TCP-only — leave that rule.
    /// </summary>
    private static bool IsLeftoverMinecraftPrefix(
        IngressSecurityRule rule,
        string proto,
        int minecraftPort,
        IReadOnlyList<IngressSecurityRule>? existing)
    {
        if (!IsMinecraftPort(rule, proto, minecraftPort))
            return false;
        if (!IsAllowlistPrefixSource(rule.Source))
            return false;

        // UDP on a friend prefix is never wait_forge (that probe is TCP-only).
        if (proto == ProtocolUdp)
            return true;

        if (proto != ProtocolTcp || existing is null)
            return false;

        return existing.Any(other =>
            other.Protocol == ProtocolUdp
            && string.Equals(other.Source, rule.Source, StringComparison.Ordinal)
            && other.UdpOptions?.DestinationPortRange?.Min == minecraftPort);
    }

    private static bool IsAllowlistPrefixSource(string? source) =>
        FriendRules.TryNormalizeAllowlistSource(source ?? "", out var parsed, out _)
        && !parsed.IsSingleHost;

    private static bool IsMinecraftPort(IngressSecurityRule rule, string proto, int minecraftPort)
    {
        if (proto == ProtocolTcp)
            return rule.TcpOptions?.DestinationPortRange?.Min == minecraftPort;
        if (proto == ProtocolUdp)
            return rule.UdpOptions?.DestinationPortRange?.Min == minecraftPort;
        return false;
    }

    private static List<IngressSecurityRule> BuildOwnedRules(
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName)
    {
        var owned = new List<IngressSecurityRule>();
        foreach (var friend in friends)
        {
            if (string.IsNullOrWhiteSpace(friend.Ip))
                continue;
            if (!FriendRules.TryNormalizeAllowlistSource(friend.Ip, out var source, out _))
                continue;

            var mcDesc = FriendRules.McDescription(friend.Name, source.Stored);
            owned.Add(MakeTcpRule(source.Cidr, minecraftPort, mcDesc));
            owned.Add(MakeUdpRule(source.Cidr, minecraftPort, mcDesc));

            if (!friend.IsAdmin)
                continue;

            var allowAdminPrefix = FriendRules.IsPrimaryAdmin(friend, adminName, friends);
            var adminCidr = FriendRules.ToAdminCidr(source.Stored, allowAdminPrefix);
            if (adminCidr is null || FriendRules.IsWorldOpenCidr(adminCidr))
                continue;

            var label = string.IsNullOrWhiteSpace(friend.Name) ? source.Stored : friend.Name.Trim();
            owned.Add(MakeTcpRule(adminCidr, sshPort, FriendRules.SshDescription(label)));
            owned.Add(MakeTcpRule(adminCidr, doorHttpPort, FriendRules.DoorDescription(label)));
        }

        return owned;
    }

    public static IngressSecurityRule MakeTcpRule(string sourceCidr, int port, string description) =>
        new()
        {
            Protocol = ProtocolTcp,
            Source = sourceCidr,
            SourceType = IngressSecurityRule.SourceTypeEnum.CidrBlock,
            IsStateless = false,
            Description = description,
            TcpOptions = new TcpOptions
            {
                DestinationPortRange = new PortRange { Min = port, Max = port },
            },
        };

    public static IngressSecurityRule MakeUdpRule(string sourceCidr, int port, string description) =>
        new()
        {
            Protocol = ProtocolUdp,
            Source = sourceCidr,
            SourceType = IngressSecurityRule.SourceTypeEnum.CidrBlock,
            IsStateless = false,
            Description = description,
            UdpOptions = new UdpOptions
            {
                DestinationPortRange = new PortRange { Min = port, Max = port },
            },
        };
}

public sealed class SecurityListIngressPlan
{
    public required IReadOnlyList<IngressSecurityRule> Preserved { get; init; }
    public required IReadOnlyList<IngressSecurityRule> Owned { get; init; }

    public IReadOnlyList<IngressSecurityRule> Ingress =>
        Preserved.Concat(Owned).ToList();
}
