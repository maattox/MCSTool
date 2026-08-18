using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// GET / PUT / DELETE for <c>meta/oversized-world-backup.json</c>.
/// Writer = VM1 backup agent. Manager is a reader (bell + SSH download) and may
/// DELETE only after an explicit operator clear (DEBUG fixture today; typed clear UX later).
/// </summary>
public sealed class OversizedWorldBackupStore
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

    public OversizedWorldBackupStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Meta, OversizedWorldBackupDocument.FileName);
    }

    public string ObjectName => _objectName;

    /// <summary>
    /// GET the flag. HTTP 404 / missing → not blocked. Any other Get failure is an error
    /// (do not treat transport errors as blocked). A present object is blocked even when
    /// JSON is malformed or <c>version</c> is newer, unless a parsed <c>status</c> is
    /// explicitly not <c>blocked</c>.
    /// </summary>
    public async Task<ServiceResult<OversizedWorldBackupReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var bytes = await _objectStorage.GetBytesAsync(_objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<OversizedWorldBackupReadResult>.Ok(new OversizedWorldBackupReadResult
                {
                    Present = false,
                });
            }

            return ServiceResult<OversizedWorldBackupReadResult>.Fail(
                bytes.Error ?? $"Get {_objectName} failed.");
        }

        OversizedWorldBackupDocument? doc = null;
        string? parseWarning = null;
        try
        {
            doc = JsonSerializer.Deserialize<OversizedWorldBackupDocument>(bytes.Value, JsonOptions);
        }
        catch (JsonException ex)
        {
            parseWarning = $"{_objectName} JSON parse failed: {ex.Message}";
        }

        if (doc is null)
        {
            parseWarning ??= $"{_objectName} JSON root is empty.";
            return ServiceResult<OversizedWorldBackupReadResult>.Ok(new OversizedWorldBackupReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version > OversizedWorldBackupDocument.DocumentVersion)
        {
            parseWarning =
                $"{_objectName} is newer than this Manager supports "
                + $"(version={doc.Version}; max={OversizedWorldBackupDocument.DocumentVersion}).";
            return ServiceResult<OversizedWorldBackupReadResult>.Ok(new OversizedWorldBackupReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version <= 0)
            doc.Version = OversizedWorldBackupDocument.DocumentVersion;

        var status = (doc.Status ?? "").Trim();
        if (string.IsNullOrEmpty(status))
        {
            parseWarning = $"{_objectName} is missing status; treating object as blocked.";
            doc.Status = OversizedWorldBackupDocument.StatusBlocked;
        }

        return ServiceResult<OversizedWorldBackupReadResult>.Ok(new OversizedWorldBackupReadResult
        {
            Present = true,
            Document = doc,
            ParseWarning = parseWarning,
        });
    }

    /// <summary>
    /// PUT / replace the flag. Production writer is VM1. Manager uses this only for
    /// tests and DEBUG fixtures — never to *set* the live block in production UI.
    /// </summary>
    public async Task<ServiceResult> PutAsync(
        OversizedWorldBackupDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Version <= 0)
            document.Version = OversizedWorldBackupDocument.DocumentVersion;
        if (document.Version > OversizedWorldBackupDocument.DocumentVersion)
        {
            return ServiceResult.Fail(
                $"{_objectName} version {document.Version} is newer than this Manager can write "
                + $"(max={OversizedWorldBackupDocument.DocumentVersion}).");
        }

        if (string.IsNullOrWhiteSpace(document.Status))
            document.Status = OversizedWorldBackupDocument.StatusBlocked;
        if (string.IsNullOrWhiteSpace(document.DetectedAt))
            document.DetectedAt = OversizedWorldBackupDocument.FormatUtc(DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(document.UpdatedAt))
            document.UpdatedAt = document.DetectedAt;
        if (string.IsNullOrWhiteSpace(document.Reason))
            document.Reason = OversizedWorldBackupDocument.ReasonArchiveExceedsSoftCap;
        if (string.IsNullOrWhiteSpace(document.BackupPrefix))
            document.BackupPrefix = OversizedWorldBackupDocument.DefaultBackupPrefix;

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

public sealed class OversizedWorldBackupReadResult
{
    /// <summary>True when the Object Storage object exists.</summary>
    public bool Present { get; init; }

    public OversizedWorldBackupDocument? Document { get; init; }

    /// <summary>Set when the object is present but JSON is malformed or too new.</summary>
    public string? ParseWarning { get; init; }

    /// <summary>
    /// Automatic Object Storage world backups must skip. Presence fails closed
    /// (malformed / newer / missing status). A parsed non-<c>blocked</c> status is not blocked.
    /// </summary>
    public bool Blocked
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
                OversizedWorldBackupDocument.StatusBlocked,
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
