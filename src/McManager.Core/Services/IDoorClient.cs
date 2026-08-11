namespace McManager.Core.Services;

public interface IDoorClient
{
    Task<ServiceResult<string>> GetStatusAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> WakeAsync(CancellationToken cancellationToken = default);

    Task<ServiceResult> IdleEmptyAsync(CancellationToken cancellationToken = default);
}
