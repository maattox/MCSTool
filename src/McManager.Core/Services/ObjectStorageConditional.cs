namespace McManager.Core.Services;

internal static class ObjectStorageConditional
{
    public static ServiceResult RequireEtagIfPresent(string objectName, bool objectExists, string? etag)
    {
        if (!objectExists)
            return ServiceResult.Ok();
        if (string.IsNullOrWhiteSpace(etag))
            return ServiceResult.Fail(ObjectStorageConflict.MissingEtag(objectName));
        return ServiceResult.Ok();
    }
}
