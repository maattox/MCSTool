namespace McManager.Core.Services;

/// <summary>Clear errors for Object Storage If-Match / missing-ETag failures.</summary>
public static class ObjectStorageConflict
{
    public static string Message(string objectName) =>
        $"{objectName} was changed by another writer. Refresh and try again.";

    public static string MissingEtag(string objectName) =>
        $"{objectName} GetObject did not return an ETag; refusing to overwrite.";

    public static bool IsConflictMessage(string? error) =>
        !string.IsNullOrWhiteSpace(error)
        && (error.Contains("changed by another writer", StringComparison.OrdinalIgnoreCase)
            || error.Contains("If-Match", StringComparison.OrdinalIgnoreCase)
            || error.Contains("Precondition Failed", StringComparison.OrdinalIgnoreCase)
            || error.Contains("412", StringComparison.Ordinal));
}
