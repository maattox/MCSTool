using System.Text.Json;
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

    public static string FolderNameFor(string packName, string? versionId)
    {
        var name = SanitizeSegment(string.IsNullOrWhiteSpace(packName) ? "pack" : packName);
        if (string.IsNullOrWhiteSpace(versionId))
            return name;
        return name + "_" + SanitizeSegment(versionId);
    }

    public static string DirectoryFor(string dataDirectory, string packName, string? versionId) =>
        Path.Combine(dataDirectory, DirectoryName, FolderNameFor(packName, versionId));

    public static ServiceResult<string> Retain(
        string sourceMrpackPath,
        MrpackAnalysis analysis,
        string dataDirectory)
    {
        ArgumentNullException.ThrowIfNull(analysis);
        if (string.IsNullOrWhiteSpace(sourceMrpackPath))
            return ServiceResult<string>.Fail("No .mrpack path was provided to retain.");
        if (!File.Exists(sourceMrpackPath))
            return ServiceResult<string>.Fail($"File not found: {sourceMrpackPath}");
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return ServiceResult<string>.Fail("No Manager data directory was provided for the retained pack archive.");

        try
        {
            var destDir = DirectoryFor(dataDirectory, analysis.PackName, analysis.VersionId);
            Directory.CreateDirectory(destDir);
            var destPath = Path.Combine(destDir, ArchiveFileName);
            File.Copy(sourceMrpackPath, destPath, overwrite: true);

            var sidecar = new ImportedPackArchiveSidecar(
                analysis.PackName,
                analysis.VersionId,
                analysis.Loader,
                analysis.MinecraftVersion,
                Path.GetFileName(sourceMrpackPath),
                DateTime.UtcNow.ToString("o"),
                destPath);
            File.WriteAllText(
                Path.Combine(destDir, SidecarFileName),
                JsonSerializer.Serialize(sidecar, JsonWriteOptions));

            return ServiceResult<string>.Ok(destPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ServiceResult<string>.Fail($"Cannot retain the original .mrpack: {ex.Message}");
        }
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

internal sealed record ImportedPackArchiveSidecar(
    string PackName,
    string? VersionId,
    string Loader,
    string MinecraftVersion,
    string SourceFileName,
    string RetainedAtUtc,
    string ArchivePath);
