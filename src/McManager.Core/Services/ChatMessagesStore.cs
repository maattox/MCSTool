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
    private readonly string _flagsObjectName;

    public ChatMessagesStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _prefixes = prefixes;
        _objectName = Combine(prefixes.Messages, ChatMessagesDocument.FileName);
        _iconObjectName = Combine(prefixes.Messages, ChatMessagesDocument.IconFileName);
        _flagsObjectName = Combine(prefixes.Meta, "flags.json");
    }

    public string ObjectName => _objectName;

    public string IconObjectName => _iconObjectName;

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
    public async Task<ServiceResult> SeedIfMissingAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        if (existing.Succeeded && existing.Value is not null)
            return ServiceResult.Ok();
        if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
            return ServiceResult.Fail(existing.Error ?? $"Get {_objectName} failed.");

        var json = JsonSerializer.Serialize(ChatMessagesDocument.Defaults(), JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName, bytes, "application/json", ifMatch: null, cancellationToken).ConfigureAwait(false);
        return put.Succeeded ? ServiceResult.Ok() : ServiceResult.Fail(put.Error ?? $"Put {_objectName} failed.");
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
            var iconError = ServerIdentityUx.ValidateIcon(iconPng);
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

        if (clearIcon)
            document.IconObject = null;
        else if (iconPng is { Length: > 0 })
            document.IconObject = _iconObjectName;
        else if (!string.IsNullOrWhiteSpace(previous?.IconObject))
            document.IconObject = previous.IconObject;

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName, putBytes, "application/json", etag, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return ServiceResult<ChatMessagesPublishResult>.Fail(put.Error ?? $"Put {_objectName} failed.");

        if (clearIcon)
        {
            await _objectStorage.DeleteObjectAsync(_iconObjectName, cancellationToken).ConfigureAwait(false);
        }
        else if (iconPng is { Length: > 0 })
        {
            var putIcon = await _objectStorage.PutBytesAsync(
                _iconObjectName, iconPng, "image/png", ifMatch: null, cancellationToken).ConfigureAwait(false);
            if (!putIcon.Succeeded)
            {
                return ServiceResult<ChatMessagesPublishResult>.Fail(
                    putIcon.Error ?? $"Put {_iconObjectName} failed.");
            }
        }

        var flagsNote = await DirtyVm1MessagesFlagAsync(cancellationToken).ConfigureAwait(false);
        return ServiceResult<ChatMessagesPublishResult>.Ok(new ChatMessagesPublishResult
        {
            Document = document,
            Message = string.IsNullOrWhiteSpace(flagsNote)
                ? $"Saved {_objectName}."
                : $"Saved {_objectName}; {flagsNote}",
        });
    }

    private async Task<string> DirtyVm1MessagesFlagAsync(CancellationToken cancellationToken)
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

        flags.MarkDirty("messages", ["vm1"], clearWriter: "manager");
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
