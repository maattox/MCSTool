namespace McManager.Core.Services;

public interface ISecurityListService
{
    Task<ServiceResult<string>> GetDisplayNameAsync(string securityListId, CancellationToken cancellationToken = default);
}
