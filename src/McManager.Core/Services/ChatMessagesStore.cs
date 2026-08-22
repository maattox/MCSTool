using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// Reads/writes Object Storage <c>messages/chat.json</c> (and optional <c>messages/server-icon.png</c>).
/// Manager is the writer. Existing objects use If-Match; first create is unconditional.
/// </summary>
public sealed class ChatMessagesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly IObjectStorageService _objectStorage;
    private readonly ObjectStoragePrefixes _prefixes;
    private readonly string _objectName;
    private readonly string _iconObjectName;
    private readonly string _doorIdleObjectName;
    private readonly string _doorStartingObjectName;
    private readonly string _doorExhaustedObjectName;
    private readonly string _flagsObjectName;

    public ChatMessagesStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _prefixes = prefixes;
        _objectName = Combine(prefixes.Messages, ChatMessagesDocument.FileName);
        _iconObjectName = Combine(prefixes.Messages, ChatMessagesDocument.IconFileName);
        _doorIdleObjectName = Combine(prefixes.Messages, ChatMessagesDocument.DoorIdleFileName);
        _doorStartingObjectName = Combine(prefixes.Messages, ChatMessagesDocument.DoorStartingFileName);
        _doorExhaustedObjectName = Combine(prefixes.Messages, ChatMessagesDocument.DoorExhaustedFileName);
        _flagsObjectName = Combine(prefixes.Meta, "flags.json");
    }

    public string ObjectName => _objectName;

    public string IconObjectName => _iconObjectName;

    public string DoorIdleObjectName => _doorIdleObjectName;

    public async Task<ServiceResult<ChatMessagesReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var got = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        if (!got.Succeeded || got.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(got.Error))
            {
                return ServiceResult<ChatMessagesReadResult>.Ok(new ChatMessagesReadResult
                {
                    Present = false,
                    Document = ChatMessagesDocument.Defaults(),
                });
            }

            return ServiceResult<ChatMessagesReadResult>.Fail(
                got.Error ?? $"Get {_objectName} failed.");
        }

        ChatMessagesDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ChatMessagesDocument>(got.Value.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ServiceResult<ChatMessagesReadResult>.Fail(
                $"{_objectName} JSON parse failed: {ex.Message}");
        }

        if (doc is null)
            return ServiceResult<ChatMessagesReadResult>.Fail($"{_objectName} JSON root is empty.");

        if (doc.Version > ChatMessagesDocument.DocumentVersion)
        {
            return ServiceResult<ChatMessagesReadResult>.Fail(
                $"{_objectName} is newer than this Manager supports "
                + $"(version={doc.Version}; max={ChatMessagesDocument.DocumentVersion}).");
        }

        if (doc.Version <= 0)
            doc.Version = ChatMessagesDocument.DocumentVersion;
        doc.FillMissingChatKeys();

        byte[]? icon = null;
        if (!string.IsNullOrWhiteSpace(doc.IconObject))
        {
            var iconGot = await _objectStorage.GetBytesAsync(doc.IconObject.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (iconGot.Succeeded && iconGot.Value is { Length: > 0 })
                icon = iconGot.Value;
        }

        return ServiceResult<ChatMessagesReadResult>.Ok(new ChatMessagesReadResult
        {
            Present = true,
            Document = doc,
            Etag = got.Value.Etag,
            IconPng = icon,
        });
    }

    /// <summary>Create default <c>messages/chat.json</c> when missing. Does not overwrite.</summary>
    public Task<ServiceResult> SeedIfMissingAsync(CancellationToken cancellationToken = default) =>
        SeedIfMissingAsync(seed: null, iconPng: null, cancellationToken);

    /// <summary>
    /// Create <c>messages/chat.json</c> (and optional 64×64 PNG) when missing.
    /// Does not overwrite an existing object. Marks <c>messages.vm1</c> so the first boot applies identity.
    /// </summary>
    public async Task<ServiceResult> SeedIfMissingAsync(
        ChatMessagesDocument? seed,
        byte[]? iconPng,
        CancellationToken cancellationToken = default)
    {
        var existing = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        if (existing.Succeeded && existing.Value is not null)
        {
            var idleGot = await _objectStorage.GetBytesAsync(_doorIdleObjectName, cancellationToken)
                .ConfigureAwait(false);
            if (idleGot.Succeeded && idleGot.Value is { Length: > 0 })
                return ServiceResult.Ok();

            byte[]? source = iconPng;
            if (source is not { Length: > 0 })
            {
                try
                {
                    var prev = JsonSerializer.Deserialize<ChatMessagesDocument>(
                        existing.Value.Content, JsonOptions);
                    if (!string.IsNullOrWhiteSpace(prev?.IconObject))
                    {
                        var gotIcon = await _objectStorage.GetBytesAsync(prev.IconObject.Trim(), cancellationToken)
                            .ConfigureAwait(false);
                        if (gotIcon.Succeeded && gotIcon.Value is { Length: > 0 })
                            source = gotIcon.Value;
                    }
                }
                catch (JsonException)
                {
                    source = iconPng;
                }
            }

            var backfill = ComposeIcons(source);
            if (!backfill.Succeeded || backfill.Value is null)
                return ServiceResult.Ok();

            var putBackfill = await PutIconSetAsync(backfill.Value, cancellationToken).ConfigureAwait(false);
            if (!putBackfill.Succeeded)
                return putBackfill;
            await DirtyMessagesFlagsAsync(dirtyDoor: true, cancellationToken).ConfigureAwait(false);
            return ServiceResult.Ok();
        }
        if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
            return ServiceResult.Fail(existing.Error ?? $"Get {_objectName} failed.");

        var doc = ChatMessagesDocument.Defaults();
        if (seed is not null)
        {
            doc.ServerName = seed.ServerName?.Trim() ?? "";
            doc.Description = seed.Description?.Trim() ?? "";
        }

        doc.FillMissingChatKeys();
        var icons = ComposeIcons(iconPng);
        if (!icons.Succeeded || icons.Value is null)
            return ServiceResult.Fail(icons.Error ?? "Could not build server icons.");

        doc.IconObject = _iconObjectName;

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName, bytes, "application/json", ifMatch: null, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return ServiceResult.Fail(put.Error ?? $"Put {_objectName} failed.");

        var putIcons = await PutIconSetAsync(icons.Value, cancellationToken).ConfigureAwait(false);
        if (!putIcons.Succeeded)
            return putIcons;

        await DirtyMessagesFlagsAsync(dirtyDoor: true, cancellationToken).ConfigureAwait(false);
        return ServiceResult.Ok();
    }

    public async Task<ServiceResult<ChatMessagesPublishResult>> PublishAsync(
        ChatMessagesDocument document,
        byte[]? iconPng = null,
        bool clearIcon = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (iconPng is { Length: > 0 })
        {
            var iconError = ServerIdentityUx.ValidateSourceIcon(iconPng);
            if (iconError is not null)
                return ServiceResult<ChatMessagesPublishResult>.Fail(iconError);
        }

        var existing = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        string? etag = null;
        ChatMessagesDocument? previous = null;
        if (existing.Succeeded && existing.Value is not null)
        {
            etag = existing.Value.Etag;
            var require = ObjectStorageConditional.RequireEtagIfPresent(_objectName, objectExists: true, etag);
            if (!require.Succeeded)
            {
                return ServiceResult<ChatMessagesPublishResult>.Fail(
                    require.Error ?? ObjectStorageConflict.MissingEtag(_objectName));
            }

            try
            {
                previous = JsonSerializer.Deserialize<ChatMessagesDocument>(existing.Value.Content, JsonOptions);
            }
            catch (JsonException)
            {
                previous = null;
            }
        }
        else if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
        {
            return ServiceResult<ChatMessagesPublishResult>.Fail(
                existing.Error ?? $"Get {_objectName} failed.");
        }

        if (document.Version > ChatMessagesDocument.DocumentVersion)
        {
            return ServiceResult<ChatMessagesPublishResult>.Fail(
                $"{_objectName} version {document.Version} is newer than this Manager can write "
                + $"(max={ChatMessagesDocument.DocumentVersion}).");
        }

        document.Version = ChatMessagesDocument.DocumentVersion;
        document.ChatMessages ??= new Dictionary<string, string>(StringComparer.Ordinal);
        if (previous?.ChatMessages is { Count: > 0 })
        {
            foreach (var pair in previous.ChatMessages)
            {
                if (!document.ChatMessages.ContainsKey(pair.Key))
                    document.ChatMessages[pair.Key] = pair.Value;
            }
        }

        document.FillMissingChatKeys();
        document.ServerName = document.ServerName?.Trim() ?? "";
        document.Description = document.Description?.Trim() ?? "";
        document.StampUpdated();

        byte[]? composeSource = iconPng;
        if (clearIcon)
            composeSource = null;
        else if (composeSource is not { Length: > 0 }
                 && !string.IsNullOrWhiteSpace(previous?.IconObject))
        {
            var existingIcon = await _objectStorage.GetBytesAsync(previous.IconObject.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (existingIcon.Succeeded && existingIcon.Value is { Length: > 0 })
                composeSource = existingIcon.Value;
        }

        var icons = ComposeIcons(composeSource);
        if (!icons.Succeeded || icons.Value is null)
        {
            return ServiceResult<ChatMessagesPublishResult>.Fail(
                icons.Error ?? "Could not build server icons.");
        }

        document.IconObject = _iconObjectName;

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName, putBytes, "application/json", etag, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return ServiceResult<ChatMessagesPublishResult>.Fail(put.Error ?? $"Put {_objectName} failed.");

        var putIcons = await PutIconSetAsync(icons.Value, cancellationToken).ConfigureAwait(false);
        if (!putIcons.Succeeded)
        {
            return ServiceResult<ChatMessagesPublishResult>.Fail(
                putIcons.Error ?? "Put server icons failed.");
        }

        var flagsNote = await DirtyMessagesFlagsAsync(dirtyDoor: true, cancellationToken).ConfigureAwait(false);
        return ServiceResult<ChatMessagesPublishResult>.Ok(new ChatMessagesPublishResult
        {
            Document = document,
            Message = string.IsNullOrWhiteSpace(flagsNote)
                ? $"Saved {_objectName}."
                : $"Saved {_objectName}; {flagsNote}",
        });
    }

    private static ServiceResult<ServerIconSet> ComposeIcons(byte[]? sourcePng) =>
        ServerIconComposer.Compose(sourcePng);

    private async Task<ServiceResult> PutIconSetAsync(ServerIconSet icons, CancellationToken cancellationToken)
    {
        var colorError = ServerIdentityUx.ValidateIcon(icons.ColorPng);
        if (colorError is not null)
            return ServiceResult.Fail(colorError);

        var puts = new (string Name, byte[] Bytes)[]
        {
            (_iconObjectName, icons.ColorPng),
            (_doorIdleObjectName, icons.IdlePng),
            (_doorStartingObjectName, icons.StartingPng),
            (_doorExhaustedObjectName, icons.ExhaustedPng),
        };
        foreach (var (name, bytes) in puts)
        {
            var put = await _objectStorage.PutBytesAsync(
                name, bytes, "image/png", ifMatch: null, cancellationToken).ConfigureAwait(false);
            if (!put.Succeeded)
                return ServiceResult.Fail(put.Error ?? $"Put {name} failed.");
        }

        return ServiceResult.Ok();
    }

    private async Task<string> DirtyMessagesFlagsAsync(bool dirtyDoor, CancellationToken cancellationToken)
    {
        var flagsResult = await _objectStorage.GetObjectAsync(_flagsObjectName, cancellationToken)
            .ConfigureAwait(false);
        MetaFlagsDocument flags;
        string? flagsEtag = null;
        if (flagsResult.Succeeded && flagsResult.Value is not null)
        {
            flagsEtag = flagsResult.Value.Etag;
            var require = ObjectStorageConditional.RequireEtagIfPresent(
                _flagsObjectName, objectExists: true, flagsEtag);
            if (!require.Succeeded)
                return require.Error ?? ObjectStorageConflict.MissingEtag(_flagsObjectName);

            try
            {
                flags = JsonSerializer.Deserialize<MetaFlagsDocument>(flagsResult.Value.Content, JsonOptions)
                        ?? MetaFlagsDocument.Empty();
            }
            catch (JsonException)
            {
                flags = MetaFlagsDocument.Empty();
            }

            flags.Normalize();
        }
        else if (OciErrorFormatter.IsNotFoundMessage(flagsResult.Error))
        {
            flags = MetaFlagsDocument.Empty();
        }
        else
        {
            return flagsResult.Error ?? "Messages saved but failed to load flags.";
        }

        flags.MarkDirty(
            "messages",
            dirtyDoor ? ["vm1", "door"] : ["vm1"],
            clearWriter: "manager");
        var json = JsonSerializer.Serialize(flags, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _flagsObjectName, bytes, "application/json", flagsEtag, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return put.Error ?? "Messages saved but failed to update flags.";
        return "set messages flags vm1=true; manager=false.";
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class ChatMessagesReadResult
{
    public bool Present { get; init; }
    public ChatMessagesDocument Document { get; init; } = ChatMessagesDocument.Defaults();
    public string? Etag { get; init; }
    public byte[]? IconPng { get; init; }
}

public sealed class ChatMessagesPublishResult
{
    public required ChatMessagesDocument Document { get; init; }
    public string Message { get; init; } = "";
}
