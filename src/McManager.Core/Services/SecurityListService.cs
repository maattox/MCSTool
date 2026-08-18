using McManager.Core.Config;
using McManager.Core.Oci;
using Oci.CoreService.Models;
using Oci.CoreService.Requests;

namespace McManager.Core.Services;

public sealed class SecurityListService : ISecurityListService
{
    private const string ProtocolTcp = "6";
    private const string ProtocolUdp = "17";

    private readonly OciSession _session;

    public SecurityListService(OciSession session) => _session = session;

    public async Task<ServiceResult<string>> GetDisplayNameAsync(
        string securityListId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securityListId))
            return ServiceResult<string>.Fail("network.security_list_id is empty.");

        try
        {
            var response = await _session.VirtualNetwork.GetSecurityList(
                new GetSecurityListRequest { SecurityListId = securityListId },
                cancellationToken: cancellationToken);

            var name = response.SecurityList.DisplayName ?? "(unnamed)";
            return ServiceResult<string>.Ok(name);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(ComputeService.FormatOciError("GetSecurityList", ex));
        }
    }

    public async Task<ServiceResult<SecurityListApplyResult>> ApplyFriendsAsync(
        IReadOnlyList<FriendEntry> friends,
        string securityListId,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securityListId))
            return ServiceResult<SecurityListApplyResult>.Fail("network.security_list_id is empty.");

        try
        {
            var getResponse = await _session.VirtualNetwork.GetSecurityList(
                new GetSecurityListRequest { SecurityListId = securityListId },
                cancellationToken: cancellationToken);

            var ownedDescriptions = friends
                .SelectMany(f => new[] { f.Name.Trim(), f.Ip.Trim() })
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToHashSet(StringComparer.Ordinal);

            var preserved = new List<IngressSecurityRule>();
            foreach (var rule in getResponse.SecurityList.IngressSecurityRules ?? [])
            {
                var desc = rule.Description ?? "";
                if (FriendRules.IsOwnedDescription(desc, ownedDescriptions))
                    continue;

                if (IsLegacyManagedRule(rule, minecraftPort, sshPort, doorHttpPort))
                    continue;

                preserved.Add(rule);
            }

            var owned = BuildOwnedRules(friends, minecraftPort, sshPort, doorHttpPort, adminName);
            var newIngress = preserved.Concat(owned).ToList();

            await _session.VirtualNetwork.UpdateSecurityList(
                new UpdateSecurityListRequest
                {
                    SecurityListId = securityListId,
                    UpdateSecurityListDetails = new UpdateSecurityListDetails
                    {
                        IngressSecurityRules = newIngress,
                    },
                },
                cancellationToken: cancellationToken);

            return ServiceResult<SecurityListApplyResult>.Ok(new SecurityListApplyResult
            {
                PreservedRuleCount = preserved.Count,
                OwnedRuleCount = owned.Count,
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<SecurityListApplyResult>.Fail(
                ComputeService.FormatOciError("UpdateSecurityList", ex));
        }
    }

    private static bool IsLegacyManagedRule(
        IngressSecurityRule rule,
        int minecraftPort,
        int sshPort,
        int doorHttpPort)
    {
        var desc = rule.Description ?? "";
        if (FriendRules.IsOwnedDescription(desc))
            return false;

        if (!FriendRules.IsSingleHostCidr(rule.Source))
            return false;

        var proto = rule.Protocol ?? "";
        if (proto == ProtocolTcp)
        {
            var port = rule.TcpOptions?.DestinationPortRange?.Min;
            if (port == minecraftPort)
                return true;
            if (port == sshPort && rule.Source is not ("0.0.0.0/0" or "::/0"))
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
            if (adminCidr is null)
                continue;

            var label = string.IsNullOrWhiteSpace(friend.Name) ? source.Stored : friend.Name.Trim();
            owned.Add(MakeTcpRule(adminCidr, sshPort, FriendRules.SshDescription(label)));
            owned.Add(MakeTcpRule(adminCidr, doorHttpPort, FriendRules.DoorDescription(label)));
        }

        return owned;
    }

    private static IngressSecurityRule MakeTcpRule(string sourceCidr, int port, string description) =>
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

    private static IngressSecurityRule MakeUdpRule(string sourceCidr, int port, string description) =>
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
