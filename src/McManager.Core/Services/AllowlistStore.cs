using System.Text;
using System.Text.Json;
using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Reads/writes Object Storage <c>ip/allowlist.json</c>. Existing objects use
/// If-Match. Reconcile may create the object so local, Security List, and the
/// bucket copy can match.
/// </summary>
public sealed class AllowlistStore
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

    public AllowlistStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Ip, "allowlist.json");
    }

    public string ObjectName => _objectName;

    /// <summary>
    /// GET <c>ip/allowlist.json</c>. Missing object is success with
    /// <see cref="AllowlistReadResult.Present"/> false (not an error).
    /// </summary>
    public async Task<ServiceResult<AllowlistReadResult>> TryReadAsync(
        CancellationToken cancellationToken = default)
    {
        var got = await _objectStorage.GetObjectAsync(_objectName, cancellationToken);
        if (!got.Succeeded || got.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(got.Error))
            {
                return ServiceResult<AllowlistReadResult>.Ok(new AllowlistReadResult
                {
                    Present = false,
                    Entries = [],
                });
            }

            return ServiceResult<AllowlistReadResult>.Fail(
                got.Error ?? "Could not read the whitelist from cloud storage.");
        }

        IpAllowlistDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<IpAllowlistDocument>(got.Value.Content, JsonOptions)
                  ?? new IpAllowlistDocument();
        }
        catch (JsonException ex)
        {
            return ServiceResult<AllowlistReadResult>.Fail(
                $"The whitelist in cloud storage could not be read: {ex.Message}");
        }

        return ServiceResult<AllowlistReadResult>.Ok(new AllowlistReadResult
        {
            Present = true,
            Entries = doc.Entries ?? [],
            Etag = got.Value.Etag,
        });
    }

    /// <summary>
    /// PUT updated entries only if <c>ip/allowlist.json</c> is already in the bucket.
    /// Missing object → skipped (not an error). Existing object uses If-Match.
    /// </summary>
    public Task<ServiceResult<AllowlistPublishResult>> PublishIfPresentAsync(
        IReadOnlyList<FriendEntry> friends,
        CancellationToken cancellationToken = default)
        => PublishAsync(friends, createIfMissing: false, cancellationToken);

    /// <summary>
    /// PUT merged entries. Existing object uses If-Match. Missing object is
    /// created when <paramref name="createIfMissing"/> is true.
    /// </summary>
    public async Task<ServiceResult<AllowlistPublishResult>> PublishAsync(
        IReadOnlyList<FriendEntry> friends,
        bool createIfMissing,
        CancellationToken cancellationToken = default)
    {
        var got = await _objectStorage.GetObjectAsync(_objectName, cancellationToken);
        if (!got.Succeeded || got.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(got.Error))
            {
                if (!createIfMissing)
                {
                    return ServiceResult<AllowlistPublishResult>.Ok(new AllowlistPublishResult
                    {
                        SkippedMissing = true,
                        Message = "The whitelist is not in cloud storage yet. Oracle's firewall rules are up to date.",
                    });
                }

                return await PutDocumentAsync(
                    new IpAllowlistDocument(),
                    friends,
                    ifMatch: null,
                    created: true,
                    cancellationToken);
            }

            return ServiceResult<AllowlistPublishResult>.Fail(
                got.Error ?? "Could not read the whitelist from cloud storage.");
        }

        if (string.IsNullOrWhiteSpace(got.Value.Etag))
        {
            return ServiceResult<AllowlistPublishResult>.Fail(
                ObjectStorageConflict.MissingEtag(_objectName));
        }

        IpAllowlistDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<IpAllowlistDocument>(got.Value.Content, JsonOptions)
                  ?? new IpAllowlistDocument();
        }
        catch (JsonException ex)
        {
            return ServiceResult<AllowlistPublishResult>.Fail(
                $"The whitelist in cloud storage could not be read: {ex.Message}");
        }

        return await PutDocumentAsync(
            doc,
            friends,
            got.Value.Etag,
            created: false,
            cancellationToken);
    }

    private async Task<ServiceResult<AllowlistPublishResult>> PutDocumentAsync(
        IpAllowlistDocument doc,
        IReadOnlyList<FriendEntry> friends,
        string? ifMatch,
        bool created,
        CancellationToken cancellationToken)
    {
        doc.Version = doc.Version <= 0 ? 1 : doc.Version;
        if (string.IsNullOrWhiteSpace(doc.ModeNote)
            || doc.ModeNote.Contains("MVP uses private", StringComparison.OrdinalIgnoreCase)
            || doc.ModeNote.Contains("ip/mode.json is private", StringComparison.OrdinalIgnoreCase))
        {
            doc.ModeNote =
                "Product is private-only. This allowlist is always applied. ip/mode.json is withdrawn.";
        }
        doc.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        doc.Entries = friends.ToList();

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName,
            putBytes,
            "application/json",
            ifMatch,
            cancellationToken);
        if (!put.Succeeded)
            return ServiceResult<AllowlistPublishResult>.Fail(put.Error ?? "Upload failed.");

        return ServiceResult<AllowlistPublishResult>.Ok(new AllowlistPublishResult
        {
            SkippedMissing = false,
            Created = created,
            Message = created
                ? $"Whitelist added to cloud storage ({doc.Entries.Count} entries)."
                : $"Whitelist saved to cloud storage ({doc.Entries.Count} entries).",
        });
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class AllowlistReadResult
{
    public bool Present { get; init; }
    public IReadOnlyList<FriendEntry> Entries { get; init; } = [];
    public string? Etag { get; init; }
}

public sealed class AllowlistPublishResult
{
    public bool SkippedMissing { get; init; }
    public bool Created { get; init; }
    public string Message { get; init; } = "";
}
