using System.Text;
using System.Text.Json;
using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Reads/writes Object Storage <c>ip/mode.json</c> when that object already exists.
/// Does not create the object (Setup / bucket seed owns first write).
/// Missing or unknown <c>mode</c> is private — never treated as public.
/// </summary>
public sealed class IpModeStore
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

    public IpModeStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Ip, "mode.json");
    }

    public string ObjectName => _objectName;

    /// <summary>
    /// PUT updated mode + blacklist only if <c>ip/mode.json</c> is already in the bucket.
    /// Missing object → skipped (not an error). Does not touch the Security List.
    /// </summary>
    public async Task<ServiceResult<IpModePublishResult>> PublishIfPresentAsync(
        string mode,
        IReadOnlyList<BlacklistEntry> blacklist,
        CancellationToken cancellationToken = default)
    {
        var bytes = await _objectStorage.GetBytesAsync(_objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<IpModePublishResult>.Ok(new IpModePublishResult
                {
                    SkippedMissing = true,
                    Message = $"{_objectName} is not in the bucket yet; mode is saved on this PC only.",
                });
            }

            return ServiceResult<IpModePublishResult>.Fail(
                bytes.Error ?? $"Get {_objectName} failed.");
        }

        IpModeDocument doc;
        try
        {
            doc = JsonSerializer.Deserialize<IpModeDocument>(bytes.Value, JsonOptions)
                  ?? new IpModeDocument();
        }
        catch (JsonException ex)
        {
            return ServiceResult<IpModePublishResult>.Fail(
                $"{_objectName} JSON parse failed: {ex.Message}");
        }

        if (doc.Version > IpModeDocument.CurrentVersion)
        {
            return ServiceResult<IpModePublishResult>.Fail(
                $"{_objectName} version {doc.Version} is newer than this Manager supports.");
        }

        doc.Version = doc.Version <= 0 ? IpModeDocument.CurrentVersion : doc.Version;
        doc.UpdatedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        doc.Mode = IpAccessMode.Normalize(mode);
        doc.Blacklist = blacklist.ToList();

        var json = JsonSerializer.Serialize(doc, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName,
            putBytes,
            "application/json",
            cancellationToken);
        if (!put.Succeeded)
            return ServiceResult<IpModePublishResult>.Fail(put.Error ?? $"Put {_objectName} failed.");

        return ServiceResult<IpModePublishResult>.Ok(new IpModePublishResult
        {
            SkippedMissing = false,
            Message = $"Updated {_objectName} (mode={doc.Mode}, {doc.Blacklist.Count} blacklist).",
        });
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class IpModePublishResult
{
    public bool SkippedMissing { get; init; }
    public string Message { get; init; } = "";
}
