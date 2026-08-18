using McManager.Core.Config;
using McManager.Core.Oci;
using Oci.CoreService.Models;
using Oci.CoreService.Requests;

namespace McManager.Core.Services;

public sealed class SecurityListService : ISecurityListService
{
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
        string? accessMode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(securityListId))
            return ServiceResult<SecurityListApplyResult>.Fail("network.security_list_id is empty.");

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
                adminName,
                accessMode);

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
                PublicMinecraft = plan.PublicMinecraft,
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<SecurityListApplyResult>.Fail(
                ComputeService.FormatOciError("UpdateSecurityList", ex));
        }
    }
}
