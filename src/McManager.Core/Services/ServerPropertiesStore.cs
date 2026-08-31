using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// Reads/writes Object Storage <c>messages/server-properties.json</c>.
/// Manager is the writer. Existing objects use If-Match; first create is unconditional.
/// Dirties <c>messages.vm1</c> (not door — no MOTD/icons).
/// </summary>
public sealed class ServerPropertiesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly IObjectStorageService _objectStorage;
    private readonly string _objectName;
    private readonly string _flagsObjectName;

    public ServerPropertiesStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Messages, ServerPropertiesDocument.FileName);
        _flagsObjectName = Combine(prefixes.Meta, "flags.json");
    }

    public string ObjectName => _objectName;

    public async Task<ServiceResult<ServerPropertiesReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var got = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        if (!got.Succeeded || got.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(got.Error))
            {
                return ServiceResult<ServerPropertiesReadResult>.Ok(new ServerPropertiesReadResult
                {
                    Present = false,
                    Document = ServerPropertiesDocument.Defaults(),
                });
            }

            return ServiceResult<ServerPropertiesReadResult>.Fail(
                got.Error ?? $"Get {_objectName} failed.");
        }

        ServerPropertiesDocument? doc;
        try
        {
            doc = JsonSerializer.Deserialize<ServerPropertiesDocument>(got.Value.Content, JsonOptions);
        }
        catch (JsonException ex)
        {
            return ServiceResult<ServerPropertiesReadResult>.Fail(
                $"{_objectName} JSON parse failed: {ex.Message}");
        }

        if (doc is null)
            return ServiceResult<ServerPropertiesReadResult>.Fail($"{_objectName} JSON root is empty.");

        if (doc.Version > ServerPropertiesDocument.DocumentVersion)
        {
            return ServiceResult<ServerPropertiesReadResult>.Fail(
                $"{_objectName} is newer than this Manager supports "
                + $"(version={doc.Version}; max={ServerPropertiesDocument.DocumentVersion}).");
        }

        if (doc.Version <= 0)
            doc.Version = ServerPropertiesDocument.DocumentVersion;
        doc.Properties ??= new Dictionary<string, string>(StringComparer.Ordinal);

        return ServiceResult<ServerPropertiesReadResult>.Ok(new ServerPropertiesReadResult
        {
            Present = true,
            Document = doc,
            Etag = got.Value.Etag,
        });
    }

    public async Task<ServiceResult<ServerPropertiesPublishResult>> PublishAsync(
        IReadOnlyDictionary<string, string> properties,
        string? minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(properties);

        var sanitized = ServerPropertiesCatalog.Sanitize(properties, minecraftVersion);
        if (!sanitized.Succeeded || sanitized.Value is null)
        {
            return ServiceResult<ServerPropertiesPublishResult>.Fail(
                sanitized.Error ?? "Those settings are not valid.");
        }

        var existing = await _objectStorage.GetObjectAsync(_objectName, cancellationToken).ConfigureAwait(false);
        string? etag = null;
        if (existing.Succeeded && existing.Value is not null)
        {
            etag = existing.Value.Etag;
            var require = ObjectStorageConditional.RequireEtagIfPresent(_objectName, objectExists: true, etag);
            if (!require.Succeeded)
            {
                return ServiceResult<ServerPropertiesPublishResult>.Fail(
                    require.Error ?? ObjectStorageConflict.MissingEtag(_objectName));
            }
        }
        else if (!OciErrorFormatter.IsNotFoundMessage(existing.Error))
        {
            return ServiceResult<ServerPropertiesPublishResult>.Fail(
                existing.Error ?? $"Get {_objectName} failed.");
        }

        var document = new ServerPropertiesDocument
        {
            Version = ServerPropertiesDocument.DocumentVersion,
            Properties = sanitized.Value,
        };
        document.StampUpdated();

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName, putBytes, "application/json", etag, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return ServiceResult<ServerPropertiesPublishResult>.Fail(put.Error ?? $"Put {_objectName} failed.");

        var flagsNote = await DirtyMessagesFlagsAsync(cancellationToken).ConfigureAwait(false);
        return ServiceResult<ServerPropertiesPublishResult>.Ok(new ServerPropertiesPublishResult
        {
            Document = document,
            Message = string.IsNullOrWhiteSpace(flagsNote)
                ? $"Saved {_objectName}."
                : $"Saved {_objectName}; {flagsNote}",
        });
    }

    private async Task<string> DirtyMessagesFlagsAsync(CancellationToken cancellationToken)
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
            return flagsResult.Error ?? "Settings saved but failed to load flags.";
        }

        flags.MarkDirty("messages", ["vm1"], clearWriter: "manager");
        var json = JsonSerializer.Serialize(flags, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _flagsObjectName, bytes, "application/json", flagsEtag, cancellationToken).ConfigureAwait(false);
        if (!put.Succeeded)
            return put.Error ?? "Settings saved but failed to update flags.";
        return "set messages flags vm1=true; manager=false.";
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class ServerPropertiesReadResult
{
    public bool Present { get; init; }
    public ServerPropertiesDocument Document { get; init; } = ServerPropertiesDocument.Defaults();
    public string? Etag { get; init; }
}

public sealed class ServerPropertiesPublishResult
{
    public required ServerPropertiesDocument Document { get; init; }
    public string Message { get; init; } = "";
}
