using McManager.Core.Config;

namespace McManager.Core.Services;

public sealed class WorldBackupInfo
{
    public required string ObjectName { get; init; }
    public required string FileName { get; init; }
    public long SizeBytes { get; init; }
    public DateTimeOffset? TimeCreated { get; init; }

    public string SizeDisplay => FormatSize(SizeBytes);

    public string TimeDisplay =>
        TimeCreated is null
            ? "—"
            : TimeCreated.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm") + " UTC";

    public static string FormatSize(long bytes)
    {
        const double kib = 1024.0;
        const double mib = kib * 1024;
        const double gib = mib * 1024;
        if (bytes >= gib)
            return $"{bytes / gib:F2} GiB";
        if (bytes >= mib)
            return $"{bytes / mib:F1} MiB";
        if (bytes >= kib)
            return $"{bytes / kib:F0} KiB";
        return $"{bytes} B";
    }
}

public sealed class BackupUploadCheck
{
    public bool Allowed { get; init; }
    public bool SoftCapWarning { get; init; }
    public string Message { get; init; } = "";
    public long SoftCapBytes { get; init; }
    public long CurrentBackupBytes { get; init; }
    public long ZipBytes { get; init; }
}

/// <summary>List / download / upload world zips under the configured backups prefix.</summary>
public sealed class BackupStore
{
    private readonly IObjectStorageService _objectStorage;
    private readonly string _prefix;
    private readonly double _softCapGb;

    public BackupStore(IObjectStorageService objectStorage, ObjectStorageSettings settings)
    {
        _objectStorage = objectStorage;
        _prefix = NormalizePrefix(settings.Prefixes.Backups);
        _softCapGb = settings.SoftCapGb > 0 ? settings.SoftCapGb : 9.5;
    }

    public string Prefix => _prefix;
    public double SoftCapGb => _softCapGb;
    public long SoftCapBytes => (long)(_softCapGb * 1024d * 1024d * 1024d);

    public async Task<ServiceResult<IReadOnlyList<WorldBackupInfo>>> ListWorldBackupsAsync(
        CancellationToken cancellationToken = default)
    {
        var listed = await _objectStorage.ListDetailedAsync(_prefix, cancellationToken);
        if (!listed.Succeeded || listed.Value is null)
            return ServiceResult<IReadOnlyList<WorldBackupInfo>>.Fail(listed.Error ?? "List failed.");

        var backups = listed.Value
            .Where(IsWorldBackupZip)
            .Select(o => new WorldBackupInfo
            {
                ObjectName = o.Name,
                FileName = Path.GetFileName(o.Name),
                SizeBytes = o.SizeBytes,
                TimeCreated = o.TimeCreated,
            })
            .OrderByDescending(b => b.TimeCreated ?? DateTimeOffset.MinValue)
            .ThenByDescending(b => b.FileName, StringComparer.Ordinal)
            .ToList();

        return ServiceResult<IReadOnlyList<WorldBackupInfo>>.Ok(backups);
    }

    public static long SumBackupBytes(IEnumerable<WorldBackupInfo> backups) =>
        backups.Sum(b => b.SizeBytes);

    public string FormatSoftCapLine(long currentBackupBytes) =>
        $"Backups ~{WorldBackupInfo.FormatSize(currentBackupBytes)} / {_softCapGb:0.##} GiB (soft cap)";

    public BackupUploadCheck EvaluateUpload(long zipBytes, long currentBackupBytes)
    {
        var softCap = SoftCapBytes;
        var limitLabel = $"{_softCapGb:0.##} GB";

        if (zipBytes > softCap || currentBackupBytes + zipBytes > softCap)
        {
            return new BackupUploadCheck
            {
                Allowed = false,
                SoftCapWarning = true,
                SoftCapBytes = softCap,
                CurrentBackupBytes = currentBackupBytes,
                ZipBytes = zipBytes,
                Message =
                    $"Upload failed: selected file would exceed storage limit of {limitLabel}.",
            };
        }

        return new BackupUploadCheck
        {
            Allowed = true,
            SoftCapWarning = false,
            SoftCapBytes = softCap,
            CurrentBackupBytes = currentBackupBytes,
            ZipBytes = zipBytes,
            Message = "Within soft cap.",
        };
    }

    public async Task<ServiceResult> DownloadAsync(
        string objectName,
        string localPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        await _objectStorage.DownloadToFileAsync(objectName, localPath, progress, cancellationToken);

    public async Task<ServiceResult<string>> UploadNewBackupAsync(
        string localZipPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(localZipPath) || !File.Exists(localZipPath))
            return ServiceResult<string>.Fail($"Local zip not found: {localZipPath}");

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
        var objectName = $"{_prefix}world-{stamp}.zip";
        var put = await _objectStorage.UploadFromFileAsync(
            objectName,
            localZipPath,
            "application/zip",
            progress,
            cancellationToken);

        return put.Succeeded
            ? ServiceResult<string>.Ok(objectName)
            : ServiceResult<string>.Fail(put.Error ?? "Upload failed.");
    }

    private bool IsWorldBackupZip(ObjectStorageObject obj)
    {
        var name = obj.Name;
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (!name.StartsWith(_prefix, StringComparison.Ordinal))
            return false;

        var file = Path.GetFileName(name);
        if (string.IsNullOrWhiteSpace(file))
            return false;
        if (string.Equals(file, ".keep", StringComparison.Ordinal)
            || string.Equals(file, "index.json", StringComparison.OrdinalIgnoreCase))
            return false;

        // Lab pattern: world-<stamp>.zip
        return file.StartsWith("world-", StringComparison.OrdinalIgnoreCase)
               && file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return "backups/";
        return prefix.EndsWith('/') ? prefix : prefix + "/";
    }
}
