using McManager.Core.Config;

namespace McManager.Core.Services;

public interface ISecurityListService
{
    Task<ServiceResult<string>> GetDisplayNameAsync(
        string securityListId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET the Security List and reconstruct allowlist rows from owned ingress.
    /// Does not write.
    /// </summary>
    Task<ServiceResult<SecurityListAllowlistSnapshot>> ReadAllowlistAsync(
        string securityListId,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SecurityListApplyResult>> ApplyFriendsAsync(
        IReadOnlyList<FriendEntry> friends,
        string securityListId,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        string? adminName = null,
        CancellationToken cancellationToken = default);
}
