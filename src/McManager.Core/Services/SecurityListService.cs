using McManager.Core.Oci;
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
}
