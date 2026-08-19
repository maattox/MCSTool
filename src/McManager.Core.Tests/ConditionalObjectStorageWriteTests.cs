using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ConditionalObjectStorageWriteTests
{
    private static readonly ObjectStoragePrefixes Prefixes = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task Allowlist_conflict_does_not_clobber()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"updated_at":"2026-08-18T00:00:00Z","mode_note":"keep-me","entries":[]}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("ip/allowlist.json", original, "etag-1");
        storage.BumpOnGet = "ip/allowlist.json";

        var store = new AllowlistStore(storage, Prefixes);
        var result = await store.PublishIfPresentAsync(
        [
            new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10", IsAdmin = true },
        ]);

        Assert.False(result.Succeeded);
        Assert.True(ObjectStorageConflict.IsConflictMessage(result.Error), result.Error);
        Assert.Equal(original, storage.Content("ip/allowlist.json"));
    }

    [Fact]
    public async Task Allowlist_matching_etag_updates()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"updated_at":"2026-08-18T00:00:00Z","mode_note":"keep-me","entries":[]}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("ip/allowlist.json", original, "etag-1");

        var store = new AllowlistStore(storage, Prefixes);
        var result = await store.PublishIfPresentAsync(
        [
            new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10", IsAdmin = true },
        ]);

        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.SkippedMissing);
        var json = Encoding.UTF8.GetString(storage.Content("ip/allowlist.json"));
        Assert.Contains("Ada", json, StringComparison.Ordinal);
        Assert.DoesNotContain("keep-me-not-updated", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Allowlist_missing_object_is_skipped()
    {
        var store = new AllowlistStore(new EtagMemoryStorage(), Prefixes);
        var result = await store.PublishIfPresentAsync([]);
        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.SkippedMissing);
    }

    [Fact]
    public async Task Allowlist_missing_etag_refuses_overwrite()
    {
        var original = Encoding.UTF8.GetBytes("""{"version":1,"entries":[]}""" + "\n");
        var storage = new NoEtagMemoryStorage();
        storage.Objects["ip/allowlist.json"] = original;

        var store = new AllowlistStore(storage, Prefixes);
        var result = await store.PublishIfPresentAsync(
        [
            new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10" },
        ]);

        Assert.False(result.Succeeded);
        Assert.Contains("did not return an ETag", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.False(storage.PutCalled);
        Assert.Equal(original, storage.Objects["ip/allowlist.json"]);
    }

    [Fact]
    public async Task Budget_conflict_does_not_clobber()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"idle_timeout_minutes":15,"shape_ocpus":4,"shape_memory_gb":24}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("budget/config.json", original, "etag-b");
        storage.BumpOnGet = "budget/config.json";

        var store = new UsageBudgetStore(storage, Prefixes);
        var result = await store.PublishBudgetAsync(new BudgetConfigDocument
        {
            IdleTimeoutMinutes = 30,
            ShapeOcpus = 2,
            ShapeMemoryGb = 12,
        });

        Assert.False(result.Succeeded);
        Assert.True(ObjectStorageConflict.IsConflictMessage(result.Error), result.Error);
        Assert.Equal(original, storage.Content("budget/config.json"));
        Assert.False(storage.Objects.ContainsKey("meta/flags.json"));
    }

    [Fact]
    public async Task Budget_matching_etag_publishes_and_dirties_flags()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"idle_timeout_minutes":15,"shape_ocpus":4,"shape_memory_gb":24}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("budget/config.json", original, "etag-b");

        var store = new UsageBudgetStore(storage, Prefixes);
        var result = await store.PublishBudgetAsync(new BudgetConfigDocument
        {
            IdleTimeoutMinutes = 30,
            ShapeOcpus = 4,
            ShapeMemoryGb = 24,
            MonthlyOcpuTarget = 1400,
        });

        Assert.True(result.Succeeded, result.Error);
        var json = Encoding.UTF8.GetString(storage.Content("budget/config.json"));
        Assert.Contains("\"idle_timeout_minutes\": 30", json, StringComparison.Ordinal);
        Assert.True(storage.Objects.ContainsKey("meta/flags.json"));
        var flags = JsonSerializer.Deserialize<MetaFlagsDocument>(
            storage.Content("meta/flags.json"), JsonOptions);
        Assert.NotNull(flags);
        flags!.Normalize();
        Assert.True(flags.IsDirty("budget", "door"));
        Assert.True(flags.IsDirty("budget", "vm1"));
        Assert.False(flags.IsDirty("budget", "manager"));
    }

    [Fact]
    public async Task Infra_meta_conflict_does_not_clobber()
    {
        var original = ReadInfraFixture();
        var storage = new EtagMemoryStorage();
        storage.Seed("meta/infra.json", original, "etag-m");
        storage.BumpOnGet = "meta/infra.json";

        var store = new InfraMetaStore(storage, Prefixes);
        var result = await store.PublishFromLocalAsync(ConfigFromInfraFixture());

        Assert.False(result.Succeeded);
        Assert.True(ObjectStorageConflict.IsConflictMessage(result.Error), result.Error);
        Assert.Equal(original, storage.Content("meta/infra.json"));
    }

    [Fact]
    public async Task Infra_meta_matching_etag_publishes()
    {
        var original = ReadInfraFixture();
        var storage = new EtagMemoryStorage();
        storage.Seed("meta/infra.json", original, "etag-m");

        var store = new InfraMetaStore(storage, Prefixes);
        var result = await store.PublishFromLocalAsync(ConfigFromInfraFixture());

        Assert.True(result.Succeeded, result.Error);
        Assert.NotEqual(original, storage.Content("meta/infra.json"));
        Assert.True(storage.Objects.ContainsKey("meta/flags.json"));
    }

    private static byte[] ReadInfraFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "infra", "connect-compatible.json");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");
        return File.ReadAllBytes(path);
    }

    private static ManagerLocalConfig ConfigFromInfraFixture()
    {
        var doc = JsonSerializer.Deserialize<InfraMetaDocument>(ReadInfraFixture(), JsonOptions);
        Assert.NotNull(doc);
        return doc!.ToLocalConfig(@"C:\unused\oci", "TESTING", @"C:\unused\key");
    }

    private sealed class EtagMemoryStorage : IObjectStorageService
    {
        public Dictionary<string, (byte[] Content, string Etag)> Objects { get; } = new(StringComparer.Ordinal);

        public string? BumpOnGet { get; set; }

        public void Seed(string objectName, byte[] content, string etag) =>
            Objects[objectName] = (content, etag);

        public byte[] Content(string objectName) => Objects[objectName].Content;

        public Task<ServiceResult<byte[]>> GetBytesAsync(
            string objectName,
            CancellationToken cancellationToken = default)
        {
            var got = GetObjectAsync(objectName, cancellationToken).GetAwaiter().GetResult();
            if (!got.Succeeded || got.Value is null)
                return Task.FromResult(ServiceResult<byte[]>.Fail(got.Error ?? "GetObject failed."));
            return Task.FromResult(ServiceResult<byte[]>.Ok(got.Value.Content));
        }

        public Task<ServiceResult<ObjectStorageGetResult>> GetObjectAsync(
            string objectName,
            CancellationToken cancellationToken = default)
        {
            if (!Objects.TryGetValue(objectName, out var item))
            {
                return Task.FromResult(ServiceResult<ObjectStorageGetResult>.Fail(
                    "ObjectNotFound: not found in the bucket."));
            }

            var result = new ObjectStorageGetResult
            {
                Content = item.Content,
                Etag = item.Etag,
            };

            if (string.Equals(BumpOnGet, objectName, StringComparison.Ordinal))
                Objects[objectName] = (item.Content, item.Etag + "-bumped");

            return Task.FromResult(ServiceResult<ObjectStorageGetResult>.Ok(result));
        }

        public Task<ServiceResult> PutBytesAsync(
            string objectName,
            byte[] content,
            string contentType = "application/octet-stream",
            CancellationToken cancellationToken = default)
            => PutBytesAsync(objectName, content, contentType, ifMatch: null, cancellationToken);

        public Task<ServiceResult> PutBytesAsync(
            string objectName,
            byte[] content,
            string contentType,
            string? ifMatch,
            CancellationToken cancellationToken)
        {
            if (Objects.TryGetValue(objectName, out var current))
            {
                if (string.IsNullOrWhiteSpace(ifMatch)
                    || !string.Equals(ifMatch, current.Etag, StringComparison.Ordinal))
                {
                    return Task.FromResult(ServiceResult.Fail(ObjectStorageConflict.Message(objectName)));
                }

                Objects[objectName] = (content, current.Etag + "-new");
                return Task.FromResult(ServiceResult.Ok());
            }

            if (!string.IsNullOrWhiteSpace(ifMatch))
                return Task.FromResult(ServiceResult.Fail(ObjectStorageConflict.Message(objectName)));

            Objects[objectName] = (content, "created");
            return Task.FromResult(ServiceResult.Ok());
        }

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

    private sealed class NoEtagMemoryStorage : IObjectStorageService
    {
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.Ordinal);
        public bool PutCalled { get; private set; }

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
            PutCalled = true;
            Objects[objectName] = content;
            return Task.FromResult(ServiceResult.Ok());
        }

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
