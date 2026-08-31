using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ServerPropertiesCatalogTests
{
    [Theory]
    [InlineData("1.12.2", false, true)]
    [InlineData("1.16.5", false, true)]
    [InlineData("1.17.1", false, true)]
    [InlineData("1.18.2", true, true)]
    [InlineData("1.20.1", true, true)]
    [InlineData("1.21.8", true, true)]
    [InlineData("1.21.9", true, false)]
    [InlineData("1.21.10", true, false)]
    [InlineData("26.2", true, false)]
    [InlineData("26.2-pre1", true, false)]
    public void Version_gates_simulation_distance_and_pvp(
        string version,
        bool simulation,
        bool pvp)
    {
        Assert.Equal(simulation, ServerPropertiesCatalog.SupportsSimulationDistance(version));
        Assert.Equal(pvp, ServerPropertiesCatalog.SupportsPvpProperty(version));
    }

    [Fact]
    public void Unknown_version_hides_gated_keys()
    {
        Assert.False(ServerPropertiesCatalog.SupportsSimulationDistance(null));
        Assert.False(ServerPropertiesCatalog.SupportsPvpProperty(""));
        Assert.False(ServerPropertiesCatalog.SupportsSimulationDistance("latest"));
        Assert.DoesNotContain(
            ServerPropertiesCatalog.Pvp,
            ServerPropertiesCatalog.VisibleKeys(null));
        Assert.DoesNotContain(
            ServerPropertiesCatalog.SimulationDistance,
            ServerPropertiesCatalog.VisibleKeys("nope"));
    }

    [Fact]
    public void Sanitize_rejects_rcon_and_online_mode()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["difficulty"] = "hard",
            ["enable-rcon"] = "false",
        };
        var result = ServerPropertiesCatalog.Sanitize(source, "1.20.1");
        Assert.False(result.Succeeded);
        Assert.Contains("enable-rcon", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_omits_pvp_on_1_21_9_and_clamps_sim_to_view()
    {
        var source = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["difficulty"] = "2",
            ["gamemode"] = "1",
            ["max-players"] = "12",
            ["pvp"] = "false",
            ["view-distance"] = "8",
            ["simulation-distance"] = "12",
            ["hardcore"] = "true",
            ["force-gamemode"] = "0",
            ["allow-flight"] = "TRUE",
            ["spawn-protection"] = "0",
        };
        var result = ServerPropertiesCatalog.Sanitize(source, "1.21.9");
        Assert.True(result.Succeeded, result.Error);
        var props = result.Value!;
        Assert.Equal("normal", props["difficulty"]);
        Assert.Equal("creative", props["gamemode"]);
        Assert.Equal("12", props["max-players"]);
        Assert.False(props.ContainsKey("pvp"));
        Assert.Equal("8", props["view-distance"]);
        Assert.Equal("8", props["simulation-distance"]);
        Assert.Equal("true", props["hardcore"]);
        Assert.Equal("false", props["force-gamemode"]);
        Assert.Equal("true", props["allow-flight"]);
        Assert.Equal("0", props["spawn-protection"]);
    }

    [Fact]
    public void Sanitize_keeps_pvp_on_1_20_and_skips_simulation_on_1_12()
    {
        var withPvp = ServerPropertiesCatalog.Sanitize(
            new Dictionary<string, string> { ["pvp"] = "false", ["difficulty"] = "easy" },
            "1.20.1");
        Assert.True(withPvp.Succeeded, withPvp.Error);
        Assert.Equal("false", withPvp.Value!["pvp"]);
        Assert.True(withPvp.Value.ContainsKey("simulation-distance"));

        var old = ServerPropertiesCatalog.Sanitize(
            new Dictionary<string, string> { ["simulation-distance"] = "6", ["pvp"] = "true" },
            "1.12.2");
        Assert.True(old.Succeeded, old.Error);
        Assert.False(old.Value!.ContainsKey("simulation-distance"));
        Assert.Equal("true", old.Value["pvp"]);
    }
}

public sealed class ServerPropertiesStoreTests
{
    private static readonly ObjectStoragePrefixes Prefixes = new();

    [Fact]
    public void Object_name_uses_the_frozen_messages_prefix()
    {
        var store = new ServerPropertiesStore(new EtagMemoryStorage(), Prefixes);
        Assert.Equal("messages/server-properties.json", store.ObjectName);
    }

    [Fact]
    public async Task Get_missing_object_returns_defaults_not_present()
    {
        var store = new ServerPropertiesStore(new EtagMemoryStorage(), Prefixes);
        var result = await store.GetAsync();
        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.Present);
        Assert.Equal("normal", result.Value.Document.Properties["difficulty"]);
        Assert.Equal("20", result.Value.Document.Properties["max-players"]);
    }

    [Fact]
    public async Task Publish_then_get_round_trips_and_dirties_vm1_only()
    {
        var storage = new EtagMemoryStorage();
        var store = new ServerPropertiesStore(storage, Prefixes);
        var put = await store.PublishAsync(
            new Dictionary<string, string>
            {
                ["difficulty"] = "hard",
                ["max-players"] = "8",
                ["view-distance"] = "6",
                ["simulation-distance"] = "4",
            },
            "1.20.1");
        Assert.True(put.Succeeded, put.Error);
        Assert.Contains("messages/server-properties.json", put.Value!.Message, StringComparison.Ordinal);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.True(got.Value!.Present);
        Assert.Equal("hard", got.Value.Document.Properties["difficulty"]);
        Assert.Equal("8", got.Value.Document.Properties["max-players"]);
        Assert.Equal("6", got.Value.Document.Properties["view-distance"]);
        Assert.Equal("4", got.Value.Document.Properties["simulation-distance"]);

        var flagsJson = Encoding.UTF8.GetString(storage.Content("meta/flags.json"));
        using var flags = JsonDocument.Parse(flagsJson);
        var messages = flags.RootElement.GetProperty("categories").GetProperty("messages");
        Assert.True(messages.GetProperty("vm1").GetBoolean());
        Assert.False(messages.GetProperty("door").GetBoolean());
        Assert.False(messages.GetProperty("manager").GetBoolean());
    }

    [Fact]
    public async Task Publish_refuses_forbidden_keys()
    {
        var storage = new EtagMemoryStorage();
        var store = new ServerPropertiesStore(storage, Prefixes);
        var result = await store.PublishAsync(
            new Dictionary<string, string> { ["online-mode"] = "false", ["difficulty"] = "easy" },
            "1.20.1");
        Assert.False(result.Succeeded);
        Assert.Contains("online-mode", result.Error, StringComparison.Ordinal);
        Assert.False(storage.Objects.ContainsKey("messages/server-properties.json"));
    }

    [Fact]
    public async Task Publish_conflict_does_not_clobber()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"updated_at":"2026-08-18T00:00:00Z","properties":{"difficulty":"easy"}}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("messages/server-properties.json", original, "etag-1");
        storage.BumpOnGet = "messages/server-properties.json";

        var store = new ServerPropertiesStore(storage, Prefixes);
        var result = await store.PublishAsync(
            new Dictionary<string, string> { ["difficulty"] = "hard" },
            "1.20.1");

        Assert.False(result.Succeeded);
        Assert.True(ObjectStorageConflict.IsConflictMessage(result.Error), result.Error);
        Assert.Equal(original, storage.Content("messages/server-properties.json"));
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
                if (!string.IsNullOrWhiteSpace(ifMatch)
                    && !string.Equals(ifMatch, current.Etag, StringComparison.Ordinal))
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
