using System.Text.Json;
using System.Text.Json.Serialization;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Keeps the operator's original imported pack archive in Manager local data
/// (blueprint §25 — Phase 5 re-downloads this file, never a zip of VM1 <c>mods/</c>).
/// </summary>
public static class ImportedPackArchiveStore
{
    public const string DirectoryName = "imported-packs";
    public const string ArchiveFileName = "original.mrpack";
    public const string SidecarFileName = "archive.json";

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string FolderNameFor(string packName, string? versionId)
    {
        var name = SanitizeSegment(string.IsNullOrWhiteSpace(packName) ? "pack" : packName);
        if (string.IsNullOrWhiteSpace(versionId))
            return name;
        return name + "_" + SanitizeSegment(versionId);
    }

    public static string DirectoryFor(string dataDirectory, string packName, string? versionId) =>
        Path.Combine(dataDirectory, DirectoryName, FolderNameFor(packName, versionId));

    public static string ArchiveFileNameFor(string sourcePath)
    {
        var ext = Path.GetExtension(sourcePath ?? "");
        if (ext.Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            return ArchiveFileName;
        if (string.IsNullOrEmpty(ext))
            return "original.bin";
        return "original" + ext.ToLowerInvariant();
    }

    public static ServiceResult<string> Retain(
        string sourceMrpackPath,
        MrpackAnalysis analysis,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        return Retain(
            sourceMrpackPath,
            analysis.PackName,
            analysis.VersionId,
            analysis.Loader,
            analysis.MinecraftVersion,
            dataDirectory);
    }

    public static ServiceResult<string> Retain(
        string sourceArchivePath,
        string packName,
        string? versionId,
        string loader,
        string minecraftVersion,
        string dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(sourceArchivePath))
            return ServiceResult<string>.Fail("No pack archive path was provided to retain.");
        if (!File.Exists(sourceArchivePath))
            return ServiceResult<string>.Fail($"File not found: {sourceArchivePath}");
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return ServiceResult<string>.Fail("No Manager data directory was provided for the retained pack archive.");

        try
        {
            var destDir = DirectoryFor(dataDirectory, packName, versionId);
            Directory.CreateDirectory(destDir);
            var destPath = Path.Combine(destDir, ArchiveFileNameFor(sourceArchivePath));
            File.Copy(sourceArchivePath, destPath, overwrite: true);

            var sidecar = new ImportedPackArchiveInfo(
                packName,
                versionId,
                loader,
                minecraftVersion,
                Path.GetFileName(sourceArchivePath),
                DateTime.UtcNow.ToString("o"),
                destPath);
            File.WriteAllText(
                Path.Combine(destDir, SidecarFileName),
                JsonSerializer.Serialize(sidecar, JsonWriteOptions));

            return ServiceResult<string>.Ok(destPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ServiceResult<string>.Fail($"Cannot retain the original pack archive: {ex.Message}");
        }
    }

    /// <summary>
    /// Latest retained original archive under <c>data/imported-packs/</c>.
    /// Looks in each folder for <c>original.*</c> so a moved data directory still works.
    /// </summary>
    public static ImportedPackArchiveInfo? TryFindLatest(string? dataDirectory)
    {
        var all = List(dataDirectory);
        return all.Count == 0
            ? null
            : all.OrderByDescending(a => a.RetainedAt)
                .ThenByDescending(a => a.ArchivePath, StringComparer.OrdinalIgnoreCase)
                .First();
    }

    public static IReadOnlyList<ImportedPackArchiveInfo> List(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return [];

        var root = Path.Combine(dataDirectory, DirectoryName);
        if (!Directory.Exists(root))
            return [];

        var list = new List<ImportedPackArchiveInfo>();
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            if (TryReadFolder(dir, out var info) && info is not null)
                list.Add(info);
        }

        return list;
    }

    private static bool TryReadFolder(string destDir, out ImportedPackArchiveInfo? info)
    {
        info = null;
        try
        {
            var sidecarPath = Path.Combine(destDir, SidecarFileName);
            ImportedPackArchiveInfo? sidecar = null;
            if (File.Exists(sidecarPath))
            {
                sidecar = JsonSerializer.Deserialize<ImportedPackArchiveInfo>(
                    File.ReadAllText(sidecarPath),
                    JsonReadOptions);
            }

            var archivePath = ResolveArchivePath(destDir, sidecar?.ArchivePath);
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                return false;

            var sourceName = sidecar?.SourceFileName;
            if (string.IsNullOrWhiteSpace(sourceName))
                sourceName = Path.GetFileName(archivePath);

            info = new ImportedPackArchiveInfo(
                string.IsNullOrWhiteSpace(sidecar?.PackName) ? "pack" : sidecar.PackName,
                sidecar?.VersionId,
                sidecar?.Loader ?? "",
                sidecar?.MinecraftVersion ?? "",
                sourceName,
                sidecar?.RetainedAtUtc ?? "",
                archivePath);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private static string? ResolveArchivePath(string destDir, string? sidecarPath)
    {
        if (!string.IsNullOrWhiteSpace(sidecarPath) && File.Exists(sidecarPath))
            return sidecarPath;

        foreach (var path in Directory.EnumerateFiles(destDir, "original.*"))
        {
            var name = Path.GetFileName(path);
            if (name.Equals(SidecarFileName, StringComparison.OrdinalIgnoreCase))
                continue;
            return path;
        }

        return null;
    }

    internal static string SanitizeSegment(string value)
    {
        var chars = value.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = chars[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
                continue;
            chars[i] = '_';
        }

        var s = new string(chars).Trim('_');
        if (s.Length == 0)
            s = "pack";
        if (s.Length > 80)
            s = s[..80].TrimEnd('_');
        return s;
    }
}

public sealed record ImportedPackArchiveInfo(
    string PackName,
    string? VersionId,
    string Loader,
    string MinecraftVersion,
    string SourceFileName,
    string RetainedAtUtc,
    string ArchivePath)
{
    [JsonIgnore]
    public DateTimeOffset RetainedAt =>
        DateTimeOffset.TryParse(RetainedAtUtc, out var parsed) ? parsed : DateTimeOffset.MinValue;

    [JsonIgnore]
    public string SuggestedDownloadFileName =>
        string.IsNullOrWhiteSpace(SourceFileName)
            ? Path.GetFileName(ArchivePath)
            : SourceFileName.Trim();
}
