using McManager.Core.Config;
using McManager.Core.Services;
using Oci.CoreService.Models;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SecurityListIngressPlanTests
{
    private const int McPort = 25565;
    private const int SshPort = 22;
    private const int DoorPort = 8080;

    private static readonly FriendEntry Alice = new()
    {
        Name = "Alice",
        Ip = "203.0.113.10",
        IsAdmin = false,
    };

    private static readonly FriendEntry Admin = new()
    {
        Name = "Admin",
        Ip = "198.51.100.7",
        IsAdmin = true,
    };

    private static readonly FriendEntry CidrFriend = new()
    {
        Name = "Jordan",
        Ip = "172.56.0.0/16",
        IsAdmin = false,
    };

    [Fact]
    public void Private_writes_allowlist_minecraft_and_admin_ssh_not_world_open()
    {
        var icmp = IcmpRule();
        var plan = SecurityListIngressPlanner.Build(
            [icmp],
            [Alice, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        Assert.Contains(plan.Preserved, SameIcmp);
        Assert.Single(plan.Preserved);

        Assert.True(HasMc(plan.Owned, "203.0.113.10/32", "Alice"));
        Assert.True(HasMc(plan.Owned, "198.51.100.7/32", "Admin"));
        Assert.DoesNotContain(plan.Owned, r => IsMinecraft(r) && FriendRules.IsWorldOpenCidr(r.Source));

        Assert.True(HasTcp(plan.Owned, "198.51.100.7/32", SshPort, FriendRules.SshDescription("Admin")));
        Assert.True(HasTcp(plan.Owned, "198.51.100.7/32", DoorPort, FriendRules.DoorDescription("Admin")));
        Assert.DoesNotContain(plan.Ingress, r => IsSshOrDoor(r) && FriendRules.IsWorldOpenCidr(r.Source));
        Assert.DoesNotContain(plan.Ingress, IsRcon);
    }

    [Fact]
    public void Private_minecraft_uses_friend_cidr_ssh_stays_host()
    {
        var plan = SecurityListIngressPlanner.Build(
            [],
            [CidrFriend, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        Assert.True(HasMc(plan.Owned, "172.56.0.0/16", "Jordan"));
        Assert.False(HasMc(plan.Owned, "172.56.0.0/32", "Jordan"));
        Assert.True(HasTcp(plan.Owned, "198.51.100.7/32", SshPort, FriendRules.SshDescription("Admin")));
        Assert.DoesNotContain(plan.Owned, r => IsSshOrDoor(r) && r.Source == "172.56.0.0/16");
    }

    [Fact]
    public void Private_restores_allowlist_and_strips_world_open_minecraft()
    {
        var existing = new[]
        {
            IcmpRule(),
            SecurityListIngressPlanner.MakeTcpRule(
                "0.0.0.0/0",
                McPort,
                "someone edited this description"),
            SecurityListIngressPlanner.MakeUdpRule(
                "0.0.0.0/0",
                McPort,
                "mc-whitelist:public"),
            SecurityListIngressPlanner.MakeTcpRule(
                "198.51.100.7/32",
                SshPort,
                FriendRules.SshDescription("Admin")),
        };

        var plan = SecurityListIngressPlanner.Build(
            existing,
            [Alice, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        Assert.Contains(plan.Preserved, SameIcmp);
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && FriendRules.IsWorldOpenCidr(r.Source));
        Assert.True(HasMc(plan.Owned, "203.0.113.10/32", "Alice"));
        Assert.True(HasMc(plan.Owned, "198.51.100.7/32", "Admin"));
        Assert.True(HasTcp(plan.Owned, "198.51.100.7/32", SshPort, FriendRules.SshDescription("Admin")));
    }

    [Fact]
    public void Private_strips_leftover_minecraft_prefix_and_keeps_wait_forge_tcp()
    {
        var existing = new[]
        {
            IcmpRule(),
            WaitForgeTcpRule(),
            SecurityListIngressPlanner.MakeTcpRule(
                "192.0.2.0/24",
                McPort,
                "test range"),
            SecurityListIngressPlanner.MakeUdpRule(
                "192.0.2.0/24",
                McPort,
                "test range"),
            SecurityListIngressPlanner.MakeTcpRule(
                "192.0.2.0/32",
                McPort,
                "test host"),
            SecurityListIngressPlanner.MakeUdpRule(
                "192.0.2.0/32",
                McPort,
                "test host"),
            SecurityListIngressPlanner.MakeTcpRule(
                "0.0.0.0/0",
                McPort,
                "someone edited this description"),
            SecurityListIngressPlanner.MakeUdpRule(
                "172.56.0.0/16",
                McPort,
                "gone cidr friend"),
            SecurityListIngressPlanner.MakeTcpRule(
                "172.56.0.0/16",
                McPort,
                "gone cidr friend"),
        };

        var plan = SecurityListIngressPlanner.Build(
            existing,
            [Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        Assert.Contains(plan.Preserved, SameIcmp);
        Assert.Single(plan.Preserved, IsWaitForge);
        Assert.DoesNotContain(plan.Preserved, r => r.Source == "192.0.2.0/24");
        Assert.DoesNotContain(plan.Preserved, r => r.Source == "172.56.0.0/16");
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && r.Source == "192.0.2.0/24");
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && r.Source == "192.0.2.0/32");
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && r.Source == "172.56.0.0/16");
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && FriendRules.IsWorldOpenCidr(r.Source));
        Assert.True(HasMc(plan.Owned, "198.51.100.7/32", "Admin"));
    }

    [Fact]
    public void Private_rewrites_desired_prefix_friend_instead_of_preserving_old_name()
    {
        var existing = new[]
        {
            WaitForgeTcpRule(),
            SecurityListIngressPlanner.MakeTcpRule("172.56.0.0/16", McPort, "old name"),
            SecurityListIngressPlanner.MakeUdpRule("172.56.0.0/16", McPort, "old name"),
        };

        var plan = SecurityListIngressPlanner.Build(
            existing,
            [CidrFriend, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        Assert.Contains(plan.Preserved, IsWaitForge);
        Assert.DoesNotContain(plan.Preserved, r => r.Source == "172.56.0.0/16");
        Assert.True(HasMc(plan.Owned, "172.56.0.0/16", "Jordan"));
        Assert.DoesNotContain(plan.Ingress, r => IsMinecraft(r) && r.Description == "old name");
    }

    [Fact]
    public void Apply_result_summary_is_private_only()
    {
        var result = new SecurityListApplyResult
        {
            PreservedRuleCount = 2,
            OwnedRuleCount = 6,
        };
        Assert.DoesNotContain("0.0.0.0/0", result.Summary, StringComparison.Ordinal);
        Assert.Contains("preserved 2", result.Summary, StringComparison.Ordinal);
        Assert.Contains("wrote 6", result.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public void Extract_round_trips_named_host_and_admin_and_cidr()
    {
        var plan = SecurityListIngressPlanner.Build(
            [IcmpRule(), WaitForgeTcpRule()],
            [Alice, Admin, CidrFriend],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin");

        var extracted = SecurityListIngressPlanner.ExtractFriends(
            plan.Ingress,
            McPort,
            SshPort,
            DoorPort);

        Assert.True(FriendAllowlistMerger.SameAllowlist([Alice, Admin, CidrFriend], extracted));
        Assert.DoesNotContain(extracted, f => f.Ip == "10.0.0.0/24");
    }

    [Fact]
    public void Extract_skips_world_open_minecraft()
    {
        var extracted = SecurityListIngressPlanner.ExtractFriends(
            [
                SecurityListIngressPlanner.MakeTcpRule("0.0.0.0/0", McPort, "public"),
                SecurityListIngressPlanner.MakeUdpRule("0.0.0.0/0", McPort, "public"),
                SecurityListIngressPlanner.MakeTcpRule("203.0.113.10/32", McPort, "Alice"),
                SecurityListIngressPlanner.MakeUdpRule("203.0.113.10/32", McPort, "Alice"),
            ],
            McPort,
            SshPort,
            DoorPort);

        var row = Assert.Single(extracted);
        Assert.Equal("Alice", row.Name);
        Assert.Equal("203.0.113.10", row.Ip);
        Assert.False(row.IsAdmin);
    }

    [Fact]
    public void Extract_orphan_ssh_becomes_admin_row()
    {
        var extracted = SecurityListIngressPlanner.ExtractFriends(
            [
                SecurityListIngressPlanner.MakeTcpRule(
                    "198.51.100.7/32",
                    SshPort,
                    FriendRules.SshDescription("Admin")),
            ],
            McPort,
            SshPort,
            DoorPort);

        var row = Assert.Single(extracted);
        Assert.Equal("Admin", row.Name);
        Assert.Equal("198.51.100.7", row.Ip);
        Assert.True(row.IsAdmin);
    }

    [Fact]
    public void Extract_strips_legacy_mc_whitelist_prefix()
    {
        var extracted = SecurityListIngressPlanner.ExtractFriends(
            [
                SecurityListIngressPlanner.MakeTcpRule(
                    "203.0.113.10/32",
                    McPort,
                    FriendRules.McTagPrefix + "Alice"),
                SecurityListIngressPlanner.MakeUdpRule(
                    "203.0.113.10/32",
                    McPort,
                    FriendRules.McTagPrefix + "Alice"),
            ],
            McPort,
            SshPort,
            DoorPort);

        Assert.Equal("Alice", Assert.Single(extracted).Name);
    }

    [Fact]
    public void NeedsRewrite_is_false_for_matching_owned_set()
    {
        var existing = SecurityListIngressPlanner.Build(
            [IcmpRule(), WaitForgeTcpRule()],
            [Alice, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin").Ingress;

        Assert.False(SecurityListIngressPlanner.NeedsRewrite(
            existing,
            [Alice, Admin],
            McPort,
            SshPort,
            DoorPort,
            adminName: "Admin"));
    }

    [Fact]
    public void NeedsRewrite_true_when_world_open_leftover_even_if_friends_match()
    {
        var matching = SecurityListIngressPlanner.Build(
            [IcmpRule()],
            [Alice],
            McPort,
            SshPort,
            DoorPort,
            adminName: null).Ingress.ToList();
        matching.Add(SecurityListIngressPlanner.MakeTcpRule("0.0.0.0/0", McPort, "public"));

        Assert.True(SecurityListIngressPlanner.NeedsRewrite(
            matching,
            [Alice],
            McPort,
            SshPort,
            DoorPort,
            adminName: null));
    }

    private static IngressSecurityRule IcmpRule() =>
        new()
        {
            Protocol = "1",
            Source = "0.0.0.0/0",
            SourceType = IngressSecurityRule.SourceTypeEnum.CidrBlock,
            IsStateless = false,
            Description = "ICMP",
        };

    private static bool SameIcmp(IngressSecurityRule rule) =>
        rule.Protocol == "1" && rule.Description == "ICMP";

    private static IngressSecurityRule WaitForgeTcpRule() =>
        SecurityListIngressPlanner.MakeTcpRule(
            "10.0.0.0/24",
            McPort,
            "Door wait_forge private poll");

    private static bool IsWaitForge(IngressSecurityRule rule) =>
        rule.Protocol == SecurityListIngressPlanner.ProtocolTcp
        && rule.Source == "10.0.0.0/24"
        && rule.TcpOptions?.DestinationPortRange?.Min == McPort
        && rule.UdpOptions is null;

    private static bool IsMinecraft(IngressSecurityRule rule)
    {
        if (rule.Protocol == SecurityListIngressPlanner.ProtocolTcp)
            return rule.TcpOptions?.DestinationPortRange?.Min == McPort;
        if (rule.Protocol == SecurityListIngressPlanner.ProtocolUdp)
            return rule.UdpOptions?.DestinationPortRange?.Min == McPort;
        return false;
    }

    private static bool IsRcon(IngressSecurityRule rule)
    {
        const int rcon = MinecraftConsoleRemote.RconPort;
        if (rule.Protocol == SecurityListIngressPlanner.ProtocolTcp)
            return rule.TcpOptions?.DestinationPortRange?.Min == rcon;
        if (rule.Protocol == SecurityListIngressPlanner.ProtocolUdp)
            return rule.UdpOptions?.DestinationPortRange?.Min == rcon;
        return false;
    }

    private static bool IsSshOrDoor(IngressSecurityRule rule) =>
        rule.Protocol == SecurityListIngressPlanner.ProtocolTcp
        && (rule.TcpOptions?.DestinationPortRange?.Min == SshPort
            || rule.TcpOptions?.DestinationPortRange?.Min == DoorPort);

    private static bool HasMc(IReadOnlyList<IngressSecurityRule> rules, string source, string description) =>
        HasTcp(rules, source, McPort, description)
        && rules.Any(r =>
            r.Protocol == SecurityListIngressPlanner.ProtocolUdp
            && r.Source == source
            && r.UdpOptions?.DestinationPortRange?.Min == McPort
            && r.Description == description);

    private static bool HasTcp(
        IReadOnlyList<IngressSecurityRule> rules,
        string source,
        int port,
        string description) =>
        rules.Any(r =>
            r.Protocol == SecurityListIngressPlanner.ProtocolTcp
            && r.Source == source
            && r.TcpOptions?.DestinationPortRange?.Min == port
            && r.Description == description);
}
