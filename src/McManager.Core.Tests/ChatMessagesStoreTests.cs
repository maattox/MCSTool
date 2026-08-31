using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Core.Usage;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ChatMessagesStoreTests
{
    private static readonly ObjectStoragePrefixes Prefixes = new();

    [Fact]
    public void Object_names_use_the_frozen_messages_prefix()
    {
        var store = new ChatMessagesStore(new EtagMemoryStorage(), Prefixes);
        Assert.Equal("messages/chat.json", store.ObjectName);
        Assert.Equal("messages/server-icon.png", store.IconObjectName);
        Assert.Equal("messages/door-idle.png", store.DoorIdleObjectName);
    }

    [Fact]
    public async Task Get_missing_object_returns_defaults_not_present()
    {
        var store = new ChatMessagesStore(new EtagMemoryStorage(), Prefixes);
        var result = await store.GetAsync();
        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.Present);
        Assert.Equal(1, result.Value.Document.Version);
        Assert.Contains("idle_stop", result.Value.Document.ChatMessages);
        Assert.Null(result.Value.IconPng);
    }

    [Fact]
    public async Task Publish_then_get_round_trips_identity_and_templates()
    {
        var storage = new EtagMemoryStorage();
        var store = new ChatMessagesStore(storage, Prefixes);
        var doc = ChatMessagesDocument.Defaults();
        doc.ServerName = "Friends SMP";
        doc.Description = "Weekend world";
        doc.ChatMessages["idle_stop"] = "Empty for {minutes} minutes. Saving.";

        var png = RealPng();
        var put = await store.PublishAsync(doc, iconPng: png);
        Assert.True(put.Succeeded, put.Error);
        Assert.Contains("messages/chat.json", put.Value!.Message, StringComparison.Ordinal);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.True(got.Value!.Present);
        Assert.Equal("Friends SMP", got.Value.Document.ServerName);
        Assert.Equal("Weekend world", got.Value.Document.Description);
        Assert.False(got.Value.Document.MotdOmitName);
        Assert.Equal("messages/server-icon.png", got.Value.Document.IconObject);
        Assert.Equal("Empty for {minutes} minutes. Saving.", got.Value.Document.ChatMessages["idle_stop"]);
        Assert.Equal("Usage limits reached. Server shutting down.", got.Value.Document.ChatMessages["budget_stop"]);
        Assert.NotNull(got.Value.IconPng);
        Assert.Null(ServerIdentityUx.ValidateIcon(got.Value.IconPng));
        Assert.True(storage.Objects.ContainsKey("messages/door-idle.png"));
        Assert.True(storage.Objects.ContainsKey("messages/door-starting.png"));
        Assert.True(storage.Objects.ContainsKey("messages/door-exhausted.png"));

        var flagsJson = Encoding.UTF8.GetString(storage.Content("meta/flags.json"));
        using var flags = JsonDocument.Parse(flagsJson);
        Assert.True(flags.RootElement.GetProperty("categories").GetProperty("messages").GetProperty("vm1").GetBoolean());
        Assert.True(flags.RootElement.GetProperty("categories").GetProperty("messages").GetProperty("door").GetBoolean());
        Assert.False(flags.RootElement.GetProperty("categories").GetProperty("messages").GetProperty("manager").GetBoolean());
    }

    [Fact]
    public async Task Publish_round_trips_formatted_motd()
    {
        var storage = new EtagMemoryStorage();
        var store = new ChatMessagesStore(storage, Prefixes);
        var doc = ChatMessagesDocument.Defaults();
        doc.ServerName = "Friends SMP";
        doc.Description = "§cHello  §aWorld";

        var put = await store.PublishAsync(doc);
        Assert.True(put.Succeeded, put.Error);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.Equal("§cHello  §aWorld", got.Value!.Document.Description);
        Assert.False(got.Value.Document.MotdOmitName);
        Assert.Equal("Friends SMP\\n§cHello  §aWorld", ServerIdentityUx.BuildMotd(
            got.Value.Document.ServerName,
            got.Value.Document.Description));
    }

    [Fact]
    public async Task Publish_conflict_does_not_clobber()
    {
        var original = Encoding.UTF8.GetBytes(
            """{"version":1,"updated_at":"2026-08-18T00:00:00Z","server_name":"Keep Me","chat_messages":{}}""" + "\n");
        var storage = new EtagMemoryStorage();
        storage.Seed("messages/chat.json", original, "etag-1");
        storage.BumpOnGet = "messages/chat.json";

        var store = new ChatMessagesStore(storage, Prefixes);
        var doc = ChatMessagesDocument.Defaults();
        doc.ServerName = "Overwrite";
        var result = await store.PublishAsync(doc);

        Assert.False(result.Succeeded);
        Assert.True(ObjectStorageConflict.IsConflictMessage(result.Error), result.Error);
        Assert.Equal(original, storage.Content("messages/chat.json"));
    }

    [Fact]
    public async Task Seed_if_missing_does_not_overwrite()
    {
        var storage = new EtagMemoryStorage();
        var store = new ChatMessagesStore(storage, Prefixes);
        var first = await store.SeedIfMissingAsync();
        Assert.True(first.Succeeded, first.Error);

        var json = Encoding.UTF8.GetString(storage.Content("messages/chat.json"));
        Assert.Contains("budget_warn_leftover", json, StringComparison.Ordinal);

        storage.Seed(
            "messages/chat.json",
            Encoding.UTF8.GetBytes("""{"version":1,"server_name":"Already","chat_messages":{}}""" + "\n"),
            "etag-keep");
        var second = await store.SeedIfMissingAsync();
        Assert.True(second.Succeeded, second.Error);
        Assert.Contains("Already", Encoding.UTF8.GetString(storage.Content("messages/chat.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seed_if_missing_writes_setup_identity_and_icon_once()
    {
        var storage = new EtagMemoryStorage();
        var store = new ChatMessagesStore(storage, Prefixes);
        var doc = ChatMessagesDocument.Defaults();
        doc.ServerName = "Vanilla Server";
        doc.Description = ServerIdentityUx.DefaultDescription;
        var first = await store.SeedIfMissingAsync(doc, iconPng: null);
        Assert.True(first.Succeeded, first.Error);

        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.Equal("Vanilla Server", got.Value!.Document.ServerName);
        Assert.Equal(ServerIdentityUx.DefaultDescription, got.Value.Document.Description);
        Assert.Equal("messages/server-icon.png", got.Value.Document.IconObject);
        Assert.NotNull(got.Value.IconPng);
        Assert.True(storage.Objects.ContainsKey("messages/door-idle.png"));

        var flagsJson = Encoding.UTF8.GetString(storage.Content("meta/flags.json"));
        using var flags = JsonDocument.Parse(flagsJson);
        Assert.True(flags.RootElement.GetProperty("categories").GetProperty("messages").GetProperty("vm1").GetBoolean());

        var again = ChatMessagesDocument.Defaults();
        again.ServerName = "Overwrite";
        var second = await store.SeedIfMissingAsync(again, iconPng: null);
        Assert.True(second.Succeeded, second.Error);
        var json = Encoding.UTF8.GetString(storage.Content("messages/chat.json"));
        Assert.Contains("Vanilla Server", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Overwrite", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Publish_fits_non_square_png_to_64()
    {
        var store = new ChatMessagesStore(new EtagMemoryStorage(), Prefixes);
        var tall = ServerIconComposerTests.SolidPng(32, 48, 200, 40, 40);
        var put = await store.PublishAsync(ChatMessagesDocument.Defaults(), iconPng: tall);
        Assert.True(put.Succeeded, put.Error);
        var got = await store.GetAsync();
        Assert.True(got.Succeeded, got.Error);
        Assert.Null(ServerIdentityUx.ValidateIcon(got.Value!.IconPng));
    }

    [Fact]
    public async Task Seed_backfills_door_icons_when_chat_json_already_exists()
    {
        var storage = new EtagMemoryStorage();
        storage.Seed(
            "messages/chat.json",
            Encoding.UTF8.GetBytes("""{"version":1,"server_name":"Already","chat_messages":{}}""" + "\n"),
            "etag-keep");
        var store = new ChatMessagesStore(storage, Prefixes);
        var seed = await store.SeedIfMissingAsync();
        Assert.True(seed.Succeeded, seed.Error);
        Assert.True(storage.Objects.ContainsKey("messages/door-idle.png"));
        Assert.Contains("Already", Encoding.UTF8.GetString(storage.Content("messages/chat.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Newer_version_get_fails_closed()
    {
        var storage = new EtagMemoryStorage();
        storage.Seed(
            "messages/chat.json",
            """{"version":99,"server_name":"Future","chat_messages":{}}"""u8.ToArray(),
            "etag-99");
        var store = new ChatMessagesStore(storage, Prefixes);
        var got = await store.GetAsync();
        Assert.False(got.Succeeded);
        Assert.Contains("newer", got.Error, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] RealPng() => ServerIconComposer.Compose().Value!.ColorPng;

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

public sealed class ServerIdentityUxTests
{
    [Fact]
    public void Motd_uses_name_and_description_as_two_lines()
    {
        Assert.Equal("Friends SMP\\nWeekend world", ServerIdentityUx.BuildMotd("Friends SMP", "Weekend world"));
        Assert.Equal("Friends SMP", ServerIdentityUx.BuildMotd("Friends SMP", "  "));
        Assert.Equal("Weekend world", ServerIdentityUx.BuildMotd("", "Weekend world"));
        Assert.Equal(ServerIdentityUx.DefaultMotd, ServerIdentityUx.BuildMotd(null, null));
        Assert.Equal(
            "§6§l★§r§l §e§lOCI Server§r§l\u00a0§6§l★§r\\ncreated with §9§ngithub.com/maattox/MCSTool§r",
            ServerIdentityUx.BuildMotd(ServerIdentityUx.DefaultName, ServerIdentityUx.DefaultDescription));
        Assert.True(MotdFormatting.IsSafePropertiesValue(ServerIdentityUx.DefaultMotd));
    }

    [Fact]
    public void Motd_drops_extra_description_lines()
    {
        Assert.Equal("Friends SMP\\n§cHello", ServerIdentityUx.BuildMotd("Friends SMP", "§cHello\n§aWorld"));
        Assert.Equal("Line one", ServerIdentityUx.BuildMotd("", "Line one\r\nLine two"));
        Assert.Equal("§cA", ServerIdentityUx.BuildMotd("", "§cA\n§aB"));
        Assert.Equal(59, MotdFormatting.VisibleLength(
            ServerIdentityUx.BuildMotd("", new string('x', 80))));
    }

    [Fact]
    public void Motd_preserves_section_codes_and_internal_spaces()
    {
        var motd = ServerIdentityUx.BuildMotd("Friends SMP", "§cHello  §aWorld");
        Assert.Equal("Friends SMP\\n§cHello  §aWorld", motd);
        Assert.True(MotdFormatting.IsSafePropertiesValue(motd));
        Assert.DoesNotContain('\n', motd);
        Assert.DoesNotContain('\r', motd);
    }

    [Fact]
    public void Display_name_prefers_custom_over_oci_name()
    {
        Assert.Equal("Friends SMP", ServerIdentityUx.DisplayName("Friends SMP", "mcmgr-vm1"));
        Assert.Equal("Friends SMP", ServerIdentityUx.DisplayName("§cFriends SMP", "mcmgr-vm1"));
        Assert.Equal("mcmgr-vm1", ServerIdentityUx.DisplayName("  ", "mcmgr-vm1"));
        Assert.Equal("—", ServerIdentityUx.DisplayName(null, null));
    }

    [Fact]
    public void Default_setup_motd_is_branded_oci_server_without_oracle()
    {
        Assert.Equal(
            ServerIdentityUx.DefaultName,
            ServerIdentityUx.DefaultServerName(SetupServerType.Vanilla, SetupVanillaFlavor.Default));
        Assert.Equal(
            ServerIdentityUx.DefaultName,
            ServerIdentityUx.DefaultServerName(SetupServerType.Vanilla, SetupVanillaFlavor.Optimized));
        Assert.Equal(
            ServerIdentityUx.DefaultName,
            ServerIdentityUx.DefaultServerName(SetupServerType.Modded, SetupVanillaFlavor.Default));
        Assert.Equal(
            "created with §9§ngithub.com/maattox/MCSTool§r",
            ServerIdentityUx.DefaultDescription);

        foreach (var name in new[]
                 {
                     ServerIdentityUx.DefaultName,
                     ServerIdentityUx.DefaultVanillaName,
                     ServerIdentityUx.DefaultPaperName,
                     ServerIdentityUx.DefaultModdedName,
                     ServerIdentityUx.DefaultDescription,
                 })
        {
            Assert.DoesNotContain("Oracle", name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Setup_seed_uses_defaults_until_description_is_customized_empty()
    {
        var vanilla = ServerIdentityUx.CreateSetupSeed(new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            VanillaFlavor = SetupVanillaFlavor.Default,
        });
        Assert.Equal(ServerIdentityUx.DefaultName, vanilla.ServerName);
        Assert.Equal(ServerIdentityUx.DefaultDescription, vanilla.Description);

        var paper = ServerIdentityUx.CreateSetupSeed(new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            VanillaFlavor = SetupVanillaFlavor.Optimized,
        });
        Assert.Equal(ServerIdentityUx.DefaultName, paper.ServerName);
        Assert.Equal(ServerIdentityUx.DefaultDescription, paper.Description);

        var custom = ServerIdentityUx.CreateSetupSeed(new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            IdentityName = "Friends SMP",
            IdentityDescription = "",
            IdentityDescriptionCustomized = true,
        });
        Assert.Equal("Friends SMP", custom.ServerName);
        Assert.Equal("", custom.Description);
        Assert.False(custom.MotdOmitName);

        var omitIgnored = ServerIdentityUx.CreateSetupSeed(new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            IdentityName = "Friends SMP",
            IdentityDescription = "§cLine",
            IdentityDescriptionCustomized = true,
            IdentityMotdOmitName = true,
        });
        Assert.False(omitIgnored.MotdOmitName);
        Assert.Equal("§cLine", omitIgnored.Description);
    }

    [Fact]
    public void Try_read_setup_icon_skips_missing_and_accepts_64_png()
    {
        Assert.Null(ServerIdentityUx.TryReadSetupIcon("", out var emptySkip));
        Assert.Null(emptySkip);

        Assert.Null(ServerIdentityUx.TryReadSetupIcon(@"C:\missing-mcmgr-icon.png", out var missingSkip));
        Assert.Contains("missing", missingSkip, StringComparison.OrdinalIgnoreCase);

        var path = Path.Combine(Path.GetTempPath(), "mcmgr-setup-icon-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(path, ServerIconComposerTests.SolidPng(40, 40, 10, 180, 40));
            var bytes = ServerIdentityUx.TryReadSetupIcon(path, out var skip);
            Assert.Null(skip);
            Assert.NotNull(bytes);
            Assert.True(bytes!.Length > 24);
        }
        finally
        {
            try { File.Delete(path); } catch { /* temp */ }
        }
    }

    [Fact]
    public void Icon_must_be_64_png()
    {
        var png = new byte[24];
        png[0] = 0x89;
        png[1] = 0x50;
        png[2] = 0x4E;
        png[3] = 0x47;
        png[4] = 0x0D;
        png[5] = 0x0A;
        png[6] = 0x1A;
        png[7] = 0x0A;
        png[19] = 64;
        png[23] = 64;
        Assert.Null(ServerIdentityUx.ValidateIcon(png));

        png[19] = 32;
        Assert.Contains("64", ServerIdentityUx.ValidateIcon(png), StringComparison.Ordinal);
        Assert.Null(ServerIdentityUx.ValidateSourceIcon(png));
        Assert.Equal("Choose a PNG file.", ServerIdentityUx.ValidateIcon(null));
        Assert.Equal("Icon must be a PNG file.", ServerIdentityUx.ValidateIcon([1, 2, 3]));
    }
}
