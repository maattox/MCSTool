using McManager.Core.Config;

namespace McManager.Core.Services;

public interface ISecurityListService
{
    Task<ServiceResult<string>> GetDisplayNameAsync(
        string securityListId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<SecurityListApplyResult>> ApplyFriendsAsync(
        IReadOnlyList<FriendEntry> friends,
        string securityListId,
        int minecraftPort,
        int sshPort,
        int doorHttpPort,
        CancellationToken cancellationToken = default);
}
