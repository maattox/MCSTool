namespace McManager.Core.Services;

public interface IObjectStorageService
{
    Task<ServiceResult<byte[]>> GetBytesAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> PutBytesAsync(
        string objectName,
        byte[] content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default);

    Task<ServiceResult<IReadOnlyList<string>>> ListAsync(
        string prefix,
        CancellationToken cancellationToken = default);
}
