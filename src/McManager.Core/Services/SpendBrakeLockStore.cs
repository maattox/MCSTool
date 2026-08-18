using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>
/// Get / PUT / DELETE for <c>meta/spend-brake-triggered.json</c>.
/// Writer = $1 budget Function (and tests). Only clearer = Manager after typed confirmation.
/// Door and Manager are readers. Presence is the lock (fail closed on malformed JSON).
/// </summary>
public sealed class SpendBrakeLockStore
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

    public SpendBrakeLockStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _objectName = Combine(prefixes.Meta, SpendBrakeLockDocument.FileName);
    }

    public string ObjectName => _objectName;

    /// <summary>
    /// GET the lock object. HTTP 404 / missing → unlocked. Any other Get failure is an error
    /// (do not treat transport errors as unlocked). A present object is always locked, even
    /// when JSON is malformed or <c>version</c> is newer than this Manager supports.
    /// </summary>
    public async Task<ServiceResult<SpendBrakeLockReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var bytes = await _objectStorage.GetBytesAsync(_objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult
                {
                    Present = false,
                });
            }

            return ServiceResult<SpendBrakeLockReadResult>.Fail(
                bytes.Error ?? $"Get {_objectName} failed.");
        }

        SpendBrakeLockDocument? doc = null;
        string? parseWarning = null;
        try
        {
            doc = JsonSerializer.Deserialize<SpendBrakeLockDocument>(bytes.Value, JsonOptions);
        }
        catch (JsonException ex)
        {
            parseWarning = $"{_objectName} JSON parse failed: {ex.Message}";
        }

        if (doc is null)
        {
            parseWarning ??= $"{_objectName} JSON root is empty.";
            return ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version > SpendBrakeLockDocument.DocumentVersion)
        {
            parseWarning =
                $"{_objectName} is newer than this Manager supports "
                + $"(version={doc.Version}; max={SpendBrakeLockDocument.DocumentVersion}).";
            return ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult
            {
                Present = true,
                ParseWarning = parseWarning,
            });
        }

        if (doc.Version <= 0)
            doc.Version = SpendBrakeLockDocument.DocumentVersion;

        if (string.IsNullOrWhiteSpace(doc.TriggeredAt))
            parseWarning = $"{_objectName} is missing triggered_at; treating object as locked.";

        return ServiceResult<SpendBrakeLockReadResult>.Ok(new SpendBrakeLockReadResult
        {
            Present = true,
            Document = doc,
            ParseWarning = parseWarning,
        });
    }

    /// <summary>
    /// PUT / replace the lock. Production writer is the budget Function (Step 2.2 source).
    /// Manager must not use this to set the lock; tests and DEBUG fixtures may.
    /// </summary>
    public async Task<ServiceResult> PutAsync(
        SpendBrakeLockDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.Version <= 0)
            document.Version = SpendBrakeLockDocument.DocumentVersion;
        if (document.Version > SpendBrakeLockDocument.DocumentVersion)
        {
            return ServiceResult.Fail(
                $"{_objectName} version {document.Version} is newer than this Manager can write "
                + $"(max={SpendBrakeLockDocument.DocumentVersion}).");
        }

        if (string.IsNullOrWhiteSpace(document.TriggeredAt))
            document.TriggeredAt = SpendBrakeLockDocument.FormatUtc(DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(document.UpdatedAt))
            document.UpdatedAt = document.TriggeredAt;
        if (string.IsNullOrWhiteSpace(document.Source))
            document.Source = SpendBrakeLockDocument.SourceBudgetFunction;
        if (string.IsNullOrWhiteSpace(document.Reason))
            document.Reason = SpendBrakeLockDocument.ReasonCompartmentBudgetThreshold;

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
    /// DELETE the lock. Manager is the only production clearer (after typed confirmation).
    /// Missing object is success (idempotent).
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

public sealed class SpendBrakeLockReadResult
{
    /// <summary>True when the Object Storage object exists. That is the lock.</summary>
    public bool Present { get; init; }

    public bool Locked => Present;

    public SpendBrakeLockDocument? Document { get; init; }

    /// <summary>Set when the object is present but JSON is malformed or too new. Still locked.</summary>
    public string? ParseWarning { get; init; }
}
