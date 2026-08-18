using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class IpModeStoreTests
{
    private static readonly ObjectStoragePrefixes Prefixes = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    [Theory]
    [InlineData(null, "private")]
    [InlineData("", "private")]
    [InlineData("private", "private")]
    [InlineData("PRIVATE", "private")]
    [InlineData("garbage", "private")]
    [InlineData("public", "public")]
    [InlineData("PUBLIC", "public")]
    public void Normalize_never_treats_invalid_as_public(string? raw, string expected)
    {
        Assert.Equal(expected, IpAccessMode.Normalize(raw));
        Assert.Equal(expected == "public", IpAccessMode.IsPublic(raw));
    }

    [Fact]
    public void Object_name_is_the_frozen_ip_mode_key()
    {
        var store = new IpModeStore(new MemoryObjectStorage(), Prefixes);
        Assert.Equal("ip/mode.json", store.ObjectName);
    }

    [Fact]
    public async Task Publish_missing_object_is_skipped()
    {
        var store = new IpModeStore(new MemoryObjectStorage(), Prefixes);
        var result = await store.PublishIfPresentAsync(IpAccessMode.Public, []);
        Assert.True(result.Succeeded, result.Error);
        Assert.True(result.Value!.SkippedMissing);
    }

    [Fact]
    public async Task Publish_present_object_writes_public_and_blacklist()
    {
        var memory = new MemoryObjectStorage();
        memory.Objects["ip/mode.json"] =
            """{"version":1,"updated_at":"2026-08-11T00:00:00Z","mode":"private","blacklist":[]}"""u8.ToArray();
        var store = new IpModeStore(memory, Prefixes);

        var put = await store.PublishIfPresentAsync(
            IpAccessMode.Public,
            [new BlacklistEntry { Id = "b1", Name = "grief", Ip = "203.0.113.9" }]);
        Assert.True(put.Succeeded, put.Error);
        Assert.False(put.Value!.SkippedMissing);

        var doc = JsonSerializer.Deserialize<IpModeDocument>(memory.Objects["ip/mode.json"], JsonOptions);
        Assert.NotNull(doc);
        Assert.Equal(IpAccessMode.Public, doc.Mode);
        Assert.Single(doc.Blacklist);
        Assert.Equal("203.0.113.9", doc.Blacklist[0].Ip);
        Assert.Equal("grief", doc.Blacklist[0].Name);
    }

    [Fact]
    public async Task Publish_normalizes_garbage_mode_to_private()
    {
        var memory = new MemoryObjectStorage();
        memory.Objects["ip/mode.json"] =
            """{"version":1,"mode":"private","blacklist":[]}"""u8.ToArray();
        var store = new IpModeStore(memory, Prefixes);

        var put = await store.PublishIfPresentAsync("not-a-mode", []);
        Assert.True(put.Succeeded, put.Error);

        var doc = JsonSerializer.Deserialize<IpModeDocument>(memory.Objects["ip/mode.json"], JsonOptions);
        Assert.Equal(IpAccessMode.Private, doc!.Mode);
    }

    [Fact]
    public async Task Publish_refuses_newer_version()
    {
        var memory = new MemoryObjectStorage();
        memory.Objects["ip/mode.json"] =
            """{"version":99,"mode":"private","blacklist":[]}"""u8.ToArray();
        var store = new IpModeStore(memory, Prefixes);

        var put = await store.PublishIfPresentAsync(IpAccessMode.Public, []);
        Assert.False(put.Succeeded);
        Assert.Contains("newer", put.Error, StringComparison.OrdinalIgnoreCase);
        var doc = JsonSerializer.Deserialize<IpModeDocument>(memory.Objects["ip/mode.json"], JsonOptions);
        Assert.Equal(IpAccessMode.Private, doc!.Mode);
    }

    [Fact]
    public void Friends_local_file_without_mode_is_private()
    {
        var json = """{"schema_version":1,"friends":[]}""";
        var file = JsonSerializer.Deserialize<FriendsLocalFile>(json, JsonOptions);
        Assert.NotNull(file);
        Assert.Equal(IpAccessMode.Private, IpAccessMode.Normalize(file.Mode));
        Assert.Empty(file.Blacklist);
    }

    [Fact]
    public void Friends_local_file_round_trips_mode_and_blacklist()
    {
        var file = new FriendsLocalFile
        {
            SchemaVersion = 1,
            Mode = IpAccessMode.Public,
            Friends = [new FriendEntry { Id = "a", Name = "Ada", Ip = "203.0.113.10", IsAdmin = true }],
            Blacklist = [new BlacklistEntry { Id = "b", Name = "blocked", Ip = "198.51.100.7" }],
        };
        var json = JsonSerializer.Serialize(file);
        var loaded = JsonSerializer.Deserialize<FriendsLocalFile>(json, JsonOptions);
        Assert.NotNull(loaded);
        Assert.Equal(IpAccessMode.Public, loaded.Mode);
        Assert.Single(loaded.Friends);
        Assert.Single(loaded.Blacklist);
        Assert.Equal("198.51.100.7", loaded.Blacklist[0].Ip);
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
}
