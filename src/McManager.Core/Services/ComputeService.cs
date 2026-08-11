using McManager.Core.Oci;
using Oci.CoreService.Requests;

namespace McManager.Core.Services;

public sealed class ComputeService : IComputeService
{
    private readonly OciSession _session;

    public ComputeService(OciSession session) => _session = session;

    public async Task<ServiceResult<string>> GetLifecycleStateAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instanceId))
            return ServiceResult<string>.Fail("vm1.instance_id is empty.");

        try
        {
            var response = await _session.Compute.GetInstance(
                new GetInstanceRequest { InstanceId = instanceId },
                cancellationToken: cancellationToken);

            var state = response.Instance.LifecycleState?.ToString() ?? "UNKNOWN";

            return ServiceResult<string>.Ok(state);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(FormatOciError("GetInstance", ex));
        }
    }

    internal static string FormatOciError(string operation, Exception ex) =>
        $"{operation} failed: {ex.Message}";
}
