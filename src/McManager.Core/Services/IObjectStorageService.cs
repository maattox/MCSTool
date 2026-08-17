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

    Task<ServiceResult<IReadOnlyList<ObjectStorageObject>>> ListDetailedAsync(
        string prefix,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DownloadToFileAsync(
        string objectName,
        string localPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> UploadFromFileAsync(
        string objectName,
        string localPath,
        string contentType = "application/octet-stream",
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteObjectAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes every object in the configured bucket (paginated). Empty prefix.</summary>
    Task<ServiceResult<int>> DeleteAllObjectsAsync(CancellationToken cancellationToken = default);
}
