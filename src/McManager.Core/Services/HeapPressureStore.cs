using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// GET / PUT / DELETE for <c>meta/heap-pressure.json</c>.
/// Writer = VM1 idle agent. Manager is a reader (bell) and DELETEs after a
/// successful larger Danger apply (or DEBUG clear).
/// </summary>
public sealed class HeapPressureStore
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

    public HeapPressureStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Meta, HeapPressureDocument.FileName);
    }

    public string ObjectName => _objectName;

    /// <summary>
    /// GET the flag. HTTP 404 / missing → no pressure. Any other Get failure is an error
    /// (do not treat transport errors as pressure). A present object is pressure even when
    /// JSON is malformed or <c>version</c> is newer, unless a parsed <c>status</c> is
    /// explicitly not <c>pressure</c>.
    /// </summary>
    public async Task<ServiceResult<HeapPressureReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var bytes = await _objectStorage.GetBytesAsync(_objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<HeapPressureReadResult>.Ok(new HeapPressureReadResult
                {
                    Present = false,
                });
            }

            return ServiceResult<HeapPressureReadResult>.Fail(
                bytes.Error ?? $"Get {_objectName} failed.");
        }

        HeapPressureDocument? doc = null;
        string? parseWarning = null;
        try
        {
            doc = JsonSerializer.Deserialize<HeapPressureDocument>(bytes.Value, JsonOptions);
        }
        catch (JsonException ex)
        {
            parseWarning = $"{_objectName} JSON parse failed: {ex.Message}";
        }

        if (doc is null)
        {
            parseWarning ??= $"{_objectName} JSON root is empty.";
            return ServiceResult<HeapPressureReadResult>.Ok(new HeapPressureReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version > HeapPressureDocument.DocumentVersion)
        {
            parseWarning =
                $"{_objectName} is newer than this Manager supports "
                + $"(version={doc.Version}; max={HeapPressureDocument.DocumentVersion}).";
            return ServiceResult<HeapPressureReadResult>.Ok(new HeapPressureReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version <= 0)
            doc.Version = HeapPressureDocument.DocumentVersion;

        var status = (doc.Status ?? "").Trim();
        if (string.IsNullOrEmpty(status))
        {
            parseWarning = $"{_objectName} is missing status; treating object as pressure.";
            doc.Status = HeapPressureDocument.StatusPressure;
        }

        return ServiceResult<HeapPressureReadResult>.Ok(new HeapPressureReadResult
        {
            Present = true,
            Document = doc,
            ParseWarning = parseWarning,
        });
    }

    /// <summary>
    /// PUT / replace the flag. Production writer is VM1. Manager uses this only for
    /// tests and DEBUG fixtures.
    /// </summary>
    public async Task<ServiceResult> PutAsync(
        HeapPressureDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Version <= 0)
            document.Version = HeapPressureDocument.DocumentVersion;
        if (document.Version > HeapPressureDocument.DocumentVersion)
        {
            return ServiceResult.Fail(
                $"{_objectName} version {document.Version} is newer than this Manager can write "
                + $"(max={HeapPressureDocument.DocumentVersion}).");
        }

        if (string.IsNullOrWhiteSpace(document.Status))
            document.Status = HeapPressureDocument.StatusPressure;
        if (string.IsNullOrWhiteSpace(document.DetectedAt))
            document.DetectedAt = HeapPressureDocument.FormatUtc(DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(document.UpdatedAt))
            document.UpdatedAt = document.DetectedAt;
        if (string.IsNullOrWhiteSpace(document.Reason))
            document.Reason = HeapPressureDocument.ReasonOom;

        var json = JsonSerializer.Serialize(document, JsonOptions);
        var putBytes = Encoding.UTF8.GetBytes(json.EndsWith('\n') ? json : json + "\n");
        var put = await _objectStorage.PutBytesAsync(
            _objectName,
            putBytes,
            "application/json",
            cancellationToken);
        if (!put.Succeeded)
            return ServiceResult.Fail(put.Error ?? $"Put {_objectName} failed.");

        return ServiceResult.Ok();
    }

    /// <summary>
    /// DELETE the flag. Missing object is success (idempotent).
    /// </summary>
    public async Task<ServiceResult> ClearAsync(CancellationToken cancellationToken = default)
    {
        var deleted = await _objectStorage.DeleteObjectAsync(_objectName, cancellationToken);
        if (!deleted.Succeeded)
            return ServiceResult.Fail(deleted.Error ?? $"Delete {_objectName} failed.");

        return ServiceResult.Ok();
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class HeapPressureReadResult
{
    /// <summary>True when the Object Storage object exists.</summary>
    public bool Present { get; init; }

    public HeapPressureDocument? Document { get; init; }

    /// <summary>Set when the object is present but JSON is malformed or too new.</summary>
    public string? ParseWarning { get; init; }

    /// <summary>
    /// Manager should warn. Presence fails closed (malformed / newer / missing status).
    /// A parsed non-<c>pressure</c> status is not pressure.
    /// </summary>
    public bool Pressured
    {
        get
        {
            if (!Present)
                return false;
            if (Document is null)
                return true;
            var status = (Document.Status ?? "").Trim();
            if (string.IsNullOrEmpty(status))
                return true;
            return string.Equals(
                status,
                HeapPressureDocument.StatusPressure,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
