namespace McManager.Core.Services;

public interface IComputeService
{
    Task<ServiceResult<string>> GetLifecycleStateAsync(string instanceId, CancellationToken cancellationToken = default);
}
