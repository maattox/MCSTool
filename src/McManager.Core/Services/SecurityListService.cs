using McManager.Core.Config;
using McManager.Core.Oci;
using Oci.CoreService.Models;
using Oci.CoreService.Requests;

namespace McManager.Core.Services;

public sealed class SecurityListService : ISecurityListService
{
    /// <summary>OCI per-Security-List ingress rule cap.</summary>
    public const int MaxIngressRules = 200;

    private readonly OciSession _session;

    public SecurityListService(OciSession session) => _session = session;

    public async Task<ServiceResult<string>> GetDisplayNameAsync(
        string securityListId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securityListId))
            return ServiceResult<string>.Fail("No Oracle firewall rules are saved for this server.");

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

    public async Task<ServiceResult<SecurityListAllowlistSnapshot>> ReadAllowlistAsync(
        string securityListId,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securityListId))
            return ServiceResult<SecurityListAllowlistSnapshot>.Fail("No Oracle firewall rules are saved for this server.");

        try
        {
            var response = await _session.VirtualNetwork.GetSecurityList(
                new GetSecurityListRequest { SecurityListId = securityListId },
                cancellationToken: cancellationToken);

            var ingress = response.SecurityList.IngressSecurityRules ?? [];
            return ServiceResult<SecurityListAllowlistSnapshot>.Ok(new SecurityListAllowlistSnapshot
            {
                Friends = SecurityListIngressPlanner.ExtractFriends(
                    ingress,
                    minecraftPort,
                    sshPort,
                    doorHttpPort),
                Ingress = ingress,
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<SecurityListAllowlistSnapshot>.Fail(
                ComputeService.FormatOciError("GetSecurityList", ex));
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
            return ServiceResult<SecurityListApplyResult>.Fail("No Oracle firewall rules are saved for this server.");

        try
        {
            var getResponse = await _session.VirtualNetwork.GetSecurityList(
                new GetSecurityListRequest { SecurityListId = securityListId },
                cancellationToken: cancellationToken);

            var plan = SecurityListIngressPlanner.Build(
                getResponse.SecurityList.IngressSecurityRules ?? [],
                friends,
                minecraftPort,
                sshPort,
                doorHttpPort,
                adminName);

            if (plan.Ingress.Count > MaxIngressRules)
            {
                return ServiceResult<SecurityListApplyResult>.Fail(
                    $"Too many Whitelist entries: Oracle allows {MaxIngressRules} firewall rules and this list needs {plan.Ingress.Count}. Remove some entries and try again.");
            }

            await _session.VirtualNetwork.UpdateSecurityList(
                new UpdateSecurityListRequest
                {
                    SecurityListId = securityListId,
                    UpdateSecurityListDetails = new UpdateSecurityListDetails
                    {
                        IngressSecurityRules = plan.Ingress.ToList(),
                    },
                },
                cancellationToken: cancellationToken);

            return ServiceResult<SecurityListApplyResult>.Ok(new SecurityListApplyResult
            {
                PreservedRuleCount = plan.Preserved.Count,
                OwnedRuleCount = plan.Owned.Count,
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<SecurityListApplyResult>.Fail(
                ComputeService.FormatOciError("UpdateSecurityList", ex));
        }
    }
}
