using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class HeapPressureStoreTests
{
    private static readonly ObjectStoragePrefixes Prefixes = new();

    [Fact]
    public void Object_name_is_the_frozen_meta_key()
    {
        var store = new HeapPressureStore(new MemoryObjectStorage(), Prefixes);
        Assert.Equal("meta/heap-pressure.json", store.ObjectName);
    }

    [Fact]
    public async Task Get_missing_object_is_not_pressure()
    {
        var store = new HeapPressureStore(new MemoryObjectStorage(), Prefixes);
        var result = await store.GetAsync();
        Assert.True(result.Succeeded, result.Error);
        Assert.NotNull(result.Value);
        Assert.False(result.Value.Present);
        Assert.False(result.Value.Pressured);
        Assert.Null(result.Value.Document);
    }

    [Fact]
    public async Task Put_then_get_is_pressure_with_document()
    {
        var store = new HeapPressureStore(new MemoryObjectStorage(), Prefixes);
        var doc = HeapPressureDocument.CreatePressure(
            "8G",
            24,
            HeapPressureDocument.ReasonOom,
            new DateTimeOffset(2026, 9, 21, 0, 0, 0, TimeSpan.Zero));

        var put = await store.PutAsync(doc);
        Assert.True(put.Succeeded, put.Error);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.True(got.Value!.Pressured);
        Assert.Equal(1, got.Value.Document!.Version);
        Assert.Equal("pressure", got.Value.Document.Status);
        Assert.Equal("2026-09-21T00:00:00Z", got.Value.Document.DetectedAt);
        Assert.Equal("8G", got.Value.Document.CurrentHeap);
        Assert.Equal("10G", got.Value.Document.SuggestedHeap);
        Assert.Equal(24, got.Value.Document.HostMemoryGb);
        Assert.False(got.Value.Document.AtHostMax);
        Assert.Equal("oom", got.Value.Document.Reason);
        Assert.Null(got.Value.ParseWarning);
    }

    [Fact]
    public async Task Malformed_json_is_still_pressure()
    {
        var memory = new MemoryObjectStorage();
        memory.Objects["meta/heap-pressure.json"] = "not-json"u8.ToArray();
        var store = new HeapPressureStore(memory, Prefixes);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.True(got.Value!.Pressured);
        Assert.Null(got.Value.Document);
        Assert.False(string.IsNullOrWhiteSpace(got.Value.ParseWarning));
    }

    [Fact]
    public async Task Newer_version_is_still_pressure()
    {
        var memory = new MemoryObjectStorage();
        memory.Objects["meta/heap-pressure.json"] =
            """{"version": 99, "status": "pressure", "detected_at": "2026-09-21T00:00:00Z"}"""u8.ToArray();
        var store = new HeapPressureStore(memory, Prefixes);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.True(got.Value!.Pressured);
        Assert.Null(got.Value.Document);
        Assert.Contains("newer", got.Value.ParseWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Transport_error_is_not_treated_as_pressure()
    {
        var store = new HeapPressureStore(new FailingObjectStorage(), Prefixes);
        var got = await store.GetAsync();
        Assert.False(got.Succeeded);
        Assert.Null(got.Value);
        Assert.Contains("429", got.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clear_deletes_the_object_and_missing_clear_succeeds()
    {
        var memory = new MemoryObjectStorage();
        var store = new HeapPressureStore(memory, Prefixes);
        Assert.True((await store.PutAsync(HeapPressureDocument.CreatePressure("6G", 24))).Succeeded);

        var cleared = await store.ClearAsync();
        Assert.True(cleared.Succeeded, cleared.Error);
        Assert.False(memory.Objects.ContainsKey(store.ObjectName));

        var unlocked = await store.GetAsync();
        Assert.True(unlocked.Succeeded, unlocked.Error);
        Assert.False(unlocked.Value!.Pressured);

        var again = await store.ClearAsync();
        Assert.True(again.Succeeded, again.Error);
    }

    private sealed class MemoryObjectStorage : IObjectStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);

        public Task<ServiceResult<byte[]>> GetBytesAsync(
            string objectName,
            CancellationToken cancellationToken = default)
        {
            if (!Objects.TryGetValue(objectName, out var bytes))
                return Task.FromResult(ServiceResult<byte[]>.Fail("ObjectNotFound: not found in the bucket."));
            return Task.FromResult(ServiceResult<byte[]>.Ok(bytes));
        }

        public Task<ServiceResult> PutBytesAsync(
            string objectName,
            byte[] content,
            string contentType = "application/octet-stream",
            CancellationToken cancellationToken = default)
        {
            Objects[objectName] = content;
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult> DeleteObjectAsync(
            string objectName,
            CancellationToken cancellationToken = default)
        {
            Objects.Remove(objectName);
            return Task.FromResult(ServiceResult.Ok());
        }

        public Task<ServiceResult<IReadOnlyList<string>>> ListAsync(
            string prefix,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult<IReadOnlyList<ObjectStorageObject>>> ListDetailedAsync(
            string prefix,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult> DownloadToFileAsync(
            string objectName,
            string localPath,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult> UploadFromFileAsync(
            string objectName,
            string localPath,
            string contentType = "application/octet-stream",
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult<int>> DeleteAllObjectsAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FailingObjectStorage : IObjectStorageService
    {
        public Task<ServiceResult<byte[]>> GetBytesAsync(
            string objectName,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<byte[]>.Fail("GetObject failed: TooManyRequests (429)."));

        public Task<ServiceResult> PutBytesAsync(
            string objectName,
            byte[] content,
            string contentType = "application/octet-stream",
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult> DeleteObjectAsync(
            string objectName,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult<IReadOnlyList<string>>> ListAsync(
            string prefix,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult<IReadOnlyList<ObjectStorageObject>>> ListDetailedAsync(
            string prefix,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult> DownloadToFileAsync(
            string objectName,
            string localPath,
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult> UploadFromFileAsync(
            string objectName,
            string localPath,
            string contentType = "application/octet-stream",
            IProgress<long>? progress = null,
            CancellationToken cancellationToken = default) => throw new NotImplementedException();

        public Task<ServiceResult<int>> DeleteAllObjectsAsync(
            CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
