using McManager.Core.Config;
using Oci.CoreService.Models;

namespace McManager.Core.Services;

/// <summary>
/// Builds the full Security List ingress set for one rewrite.
/// <see cref="Oci.CoreService.Requests.UpdateSecurityListRequest"/> replaces the entire
/// ingress list — preserve ICMP and other non-owned rules; never emit SSH/door/map
/// HTTP from <c>0.0.0.0/0</c>. Minecraft and player map HTTP are private allowlist
/// only (CIDR or <c>/32</c>). Leftover world-open Minecraft, leftover world-open
/// player HTTP, leftover friend Minecraft prefixes (TCP+UDP, <c>/9</c>–<c>/31</c>),
/// and leftover player-map TCP 80 on those prefixes are treated as managed and stripped.
/// Door <c>wait_forge</c> is TCP-only from the subnet CIDR and must stay.
/// </summary>
public static class SecurityListIngressPlanner
{
    public const string ProtocolTcp = "6";
    public const string ProtocolUdp = "17";

    /// <summary>Player map static HTTP. Separate from admin door <c>:8080</c>.</summary>
    public const int PlayerHttpPort = 80;

    public static SecurityListIngressPlan Build(
        IEnumerable<IngressSecurityRule> existing,
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName,
        int playerHttpPort = PlayerHttpPort)
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

            if (IsManagedRule(rule, minecraftPort, sshPort, doorHttpPort, existingList, playerHttpPort))
                continue;

            preserved.Add(rule);
        }

        var owned = BuildOwnedRules(
            friends,
            minecraftPort,
            sshPort,
            doorHttpPort,
            adminName,
            playerHttpPort);

        return new SecurityListIngressPlan
        {
            Preserved = preserved,
            Owned = owned,
        };
    }

    /// <summary>
    /// True when applying <paramref name="friends"/> would change ingress
    /// (membership, names, leftover world-open / prefix rules, or owned ports).
    /// </summary>
    public static bool NeedsRewrite(
        IEnumerable<IngressSecurityRule> existing,
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName,
        int playerHttpPort = PlayerHttpPort)
    {
        var existingList = existing as IReadOnlyList<IngressSecurityRule> ?? existing.ToList();
        var plan = Build(
            existingList,
            friends,
            minecraftPort,
            sshPort,
            doorHttpPort,
            adminName,
            playerHttpPort);
        var existingKeys = existingList.Select(RuleKey).ToHashSet(StringComparer.Ordinal);
        var desiredKeys = plan.Ingress.Select(RuleKey).ToHashSet(StringComparer.Ordinal);
        return !existingKeys.SetEquals(desiredKeys);
    }

    /// <summary>
    /// Reconstruct allowlist rows from live ingress. Skips world-open Minecraft
    /// and door <c>wait_forge</c> (TCP-only prefix with no UDP pair). SSH/door
    /// sources without Minecraft still become admin rows so they are not dropped.
    /// </summary>
    public static IReadOnlyList<FriendEntry> ExtractFriends(
        IEnumerable<IngressSecurityRule> existing,
        int minecraftPort,
        int sshPort,
        int doorHttpPort)
    {
        var existingList = existing as IReadOnlyList<IngressSecurityRule> ?? existing.ToList();
        var bySource = new Dictionary<string, ExtractedFriend>(StringComparer.Ordinal);

        foreach (var rule in existingList)
        {
            if (FriendRules.IsWorldOpenCidr(rule.Source))
                continue;
            if (!FriendRules.TryNormalizeAllowlistSource(rule.Source ?? "", out var source, out _))
                continue;

            var proto = rule.Protocol ?? "";
            var isMc = IsMinecraftPort(rule, proto, minecraftPort);
            if (isMc && IsWaitForgeTcp(rule, proto, minecraftPort, existingList))
                continue;

            var isSsh = proto == ProtocolTcp
                && rule.TcpOptions?.DestinationPortRange?.Min == sshPort;
            var isDoor = proto == ProtocolTcp
                && rule.TcpOptions?.DestinationPortRange?.Min == doorHttpPort;
            if (!isMc && !isSsh && !isDoor)
                continue;

            if (!bySource.TryGetValue(source.Stored, out var acc))
            {
                acc = new ExtractedFriend { Stored = source.Stored };
                bySource[source.Stored] = acc;
            }

            if (isMc)
            {
                var name = DisplayNameFromDescription(rule.Description, source.Stored);
                if (acc.Name.Length == 0 && name.Length > 0)
                    acc.Name = name;
            }

            if (isSsh || isDoor)
            {
                acc.IsAdmin = true;
                var name = DisplayNameFromAdminDescription(rule.Description);
                if (acc.Name.Length == 0 && name.Length > 0)
                    acc.Name = name;
            }
        }

        return bySource.Values
            .Select(a => new FriendEntry
            {
                Id = "",
                Name = a.Name,
                Ip = a.Stored,
                IsAdmin = a.IsAdmin,
            })
            .ToList();
    }

    private static bool IsWaitForgeTcp(
        IngressSecurityRule rule,
        string proto,
        int minecraftPort,
        IReadOnlyList<IngressSecurityRule> existing)
    {
        if (proto != ProtocolTcp || !IsMinecraftPort(rule, proto, minecraftPort))
            return false;
        if (!IsAllowlistPrefixSource(rule.Source))
            return false;
        return !existing.Any(other =>
            other.Protocol == ProtocolUdp
            && string.Equals(other.Source, rule.Source, StringComparison.Ordinal)
            && other.UdpOptions?.DestinationPortRange?.Min == minecraftPort);
    }

    private static string DisplayNameFromDescription(string? description, string stored)
    {
        var desc = (description ?? "").Trim();
        if (desc.StartsWith(FriendRules.McTagPrefix, StringComparison.Ordinal))
            desc = desc[FriendRules.McTagPrefix.Length..].Trim();
        desc = StripAdminSuffix(desc);
        if (desc.Length == 0)
            return "";
        if (string.Equals(desc, stored, StringComparison.OrdinalIgnoreCase))
            return "";
        if (FriendRules.TryNormalizeAllowlistSource(desc, out var parsed, out _)
            && string.Equals(parsed.Stored, stored, StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return desc;
    }

    private static string DisplayNameFromAdminDescription(string? description)
    {
        var desc = StripAdminSuffix((description ?? "").Trim());
        if (desc.Length == 0 || desc == FriendRules.SshTagLegacy)
            return "";
        if (FriendRules.TryNormalizeAllowlistSource(desc, out _, out _))
            return "";
        return desc;
    }

    private static string StripAdminSuffix(string description)
    {
        if (description.EndsWith(FriendRules.SshAccessSuffix, StringComparison.Ordinal))
            return description[..^FriendRules.SshAccessSuffix.Length].Trim();
        if (description.EndsWith(FriendRules.DoorAccessSuffix, StringComparison.Ordinal))
            return description[..^FriendRules.DoorAccessSuffix.Length].Trim();
        if (description.EndsWith(FriendRules.MapAccessSuffix, StringComparison.Ordinal))
            return description[..^FriendRules.MapAccessSuffix.Length].Trim();
        return description;
    }

    private static string RuleKey(IngressSecurityRule rule)
    {
        var proto = rule.Protocol ?? "";
        int? port = null;
        if (proto == ProtocolTcp)
            port = rule.TcpOptions?.DestinationPortRange?.Min;
        else if (proto == ProtocolUdp)
            port = rule.UdpOptions?.DestinationPortRange?.Min;

        return string.Concat(
            proto,
            "|",
            rule.Source ?? "",
            "|",
            port?.ToString() ?? "",
            "|",
            (rule.Description ?? "").Trim());
    }

    private sealed class ExtractedFriend
    {
        public string Stored { get; init; } = "";
        public string Name { get; set; } = "";
        public bool IsAdmin { get; set; }
    }

    internal static bool IsManagedRule(
        IngressSecurityRule rule,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        IReadOnlyList<IngressSecurityRule>? existing = null,
        int playerHttpPort = PlayerHttpPort)
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

        if (IsPlayerHttpPort(rule, proto, playerHttpPort)
            && FriendRules.IsWorldOpenCidr(rule.Source))
        {
            return true;
        }

        if (IsLeftoverMinecraftPrefix(rule, proto, minecraftPort, existing))
            return true;

        if (IsLeftoverPlayerHttpPrefix(rule, proto, playerHttpPort))
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
            if (port == playerHttpPort)
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

    private static bool IsPlayerHttpPort(IngressSecurityRule rule, string proto, int playerHttpPort) =>
        proto == ProtocolTcp && rule.TcpOptions?.DestinationPortRange?.Min == playerHttpPort;

    /// <summary>
    /// Stale player-map TCP 80 on a friend prefix (TCP-only; not wait_forge).
    /// </summary>
    private static bool IsLeftoverPlayerHttpPrefix(
        IngressSecurityRule rule,
        string proto,
        int playerHttpPort) =>
        IsPlayerHttpPort(rule, proto, playerHttpPort) && IsAllowlistPrefixSource(rule.Source);

    private static List<IngressSecurityRule> BuildOwnedRules(
        IReadOnlyList<FriendEntry> friends,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName,
        int playerHttpPort = PlayerHttpPort)
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

            var mapLabel = string.IsNullOrWhiteSpace(friend.Name) ? source.Stored : friend.Name.Trim();
            owned.Add(MakeTcpRule(source.Cidr, playerHttpPort, FriendRules.MapDescription(mapLabel)));

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
