namespace McManager.Core.Services;

/// <summary>Object body plus the OCI ETag from GetObject (for If-Match writes).</summary>
public sealed class ObjectStorageGetResult
{
    public required byte[] Content { get; init; }

    /// <summary>Entity tag from the GetObject response, when the SDK provides one.</summary>
    public string? Etag { get; init; }
}
