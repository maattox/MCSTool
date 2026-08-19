namespace McManager.Core.Services;

public interface IObjectStorageService
{
    Task<ServiceResult<byte[]>> GetBytesAsync(
        string objectName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// GET object bytes plus ETag. Default wraps <see cref="GetBytesAsync"/> with a null ETag
    /// so existing test fakes keep compiling.
    /// </summary>
    Task<ServiceResult<ObjectStorageGetResult>> GetObjectAsync(
        string objectName,
        CancellationToken cancellationToken = default)
    {
        return GetObjectWithoutEtagAsync(objectName, cancellationToken);
    }

    Task<ServiceResult> PutBytesAsync(
        string objectName,
        byte[] content,
        string contentType = "application/octet-stream",
        CancellationToken cancellationToken = default);

    /// <summary>
    /// PUT with optional If-Match. Default ignores <paramref name="ifMatch"/> (test fakes).
    /// Production <see cref="ObjectStorageService"/> sends the header and maps 412 to a conflict error.
    /// </summary>
    Task<ServiceResult> PutBytesAsync(
        string objectName,
        byte[] content,
        string contentType,
        string? ifMatch,
        CancellationToken cancellationToken)
        => PutBytesAsync(objectName, content, contentType, cancellationToken);

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

    private async Task<ServiceResult<ObjectStorageGetResult>> GetObjectWithoutEtagAsync(
        string objectName,
        CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync(objectName, cancellationToken).ConfigureAwait(false);
        if (!bytes.Succeeded || bytes.Value is null)
            return ServiceResult<ObjectStorageGetResult>.Fail(bytes.Error ?? $"GetObject {objectName} failed.");

        return ServiceResult<ObjectStorageGetResult>.Ok(new ObjectStorageGetResult
        {
            Content = bytes.Value,
            Etag = null,
        });
    }
}
