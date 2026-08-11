namespace McManager.Core.Services;

public sealed class ObjectStorageObject
{
    public required string Name { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? TimeCreated { get; init; }
}
