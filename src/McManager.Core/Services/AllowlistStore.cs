using System.Text;
using System.Text.Json;
using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Reads/writes Object Storage <c>ip/allowlist.json</c> when that object already exists.
/// Does not create the object (Setup / later IP-mode work owns first seed).
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
    /// PUT updated entries only if <c>ip/allowlist.json</c> is already in the bucket.
    /// Missing object → skipped (not an error).
    /// </summary>
    public async Task<ServiceResult<AllowlistPublishResult>> PublishIfPresentAsync(
        IReadOnlyList<FriendEntry> friends,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _objectStorage.GetBytesAsync(_objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<AllowlistPublishResult>.Ok(new AllowlistPublishResult
                {
                    SkippedMissing = true,
                    Message = $"{_objectName} is not in the bucket yet; Security List is the live allowlist.",
                });
            }

            return ServiceResult<AllowlistPublishResult>.Fail(
                bytes.Error ?? $"Get {_objectName} failed.");
        }

        IpAllowlistDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<IpAllowlistDocument>(bytes.Value, JsonOptions)
                  ?? new IpAllowlistDocument();
        }
        catch (JsonException ex)
        {
            return ServiceResult<AllowlistPublishResult>.Fail(
                $"{_objectName} JSON parse failed: {ex.Message}");
        }

        doc.Version = doc.Version <= 0 ? 1 : doc.Version;
        if (string.IsNullOrWhiteSpace(doc.ModeNote))
            doc.ModeNote = "MVP uses private allowlist only";
        doc.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        doc.Entries = friends.ToList();

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName,
            putBytes,
            "application/json",
            cancellationToken);
        if (!put.Succeeded)
            return ServiceResult<AllowlistPublishResult>.Fail(put.Error ?? $"Put {_objectName} failed.");

        return ServiceResult<AllowlistPublishResult>.Ok(new AllowlistPublishResult
        {
            SkippedMissing = false,
            Message = $"Updated {_objectName} ({doc.Entries.Count} entries).",
        });
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class AllowlistPublishResult
{
    public bool SkippedMissing { get; init; }
    public string Message { get; init; } = "";
}
