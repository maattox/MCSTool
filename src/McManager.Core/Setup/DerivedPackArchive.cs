using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Builds a product-derived zip with <c>mcmgr-pack.json</c> and <c>modrinth.index.json</c>
/// for unstructured / jar-root packs (Step 8.8 P9).
/// </summary>
public static class DerivedPackArchive
{
    private static readonly JsonSerializerOptions SidecarWriteOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions IndexWriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly string[] CopyPrefixes =
    [
        "mods/",
        "config/",
        "defaultconfigs/",
        "kubejs/",
        "scripts/",
        "libraries/",
        "world/",
        "worlds/",
        "datapacks/",
        "resourcepacks/",
    ];

    private static readonly string[] SkipPrefixes =
    [
        "shaderpacks/",
        "screenshots/",
        "client-overrides/",
        "__macosx/",
        ".fabric/",
    ];

    public static ServiceResult<string> Build(
        string sourceZipPath,
        ManualServerPackAnalysis analysis,
        DerivedPackFields confirmed,
        string destZipPath,
        string? originalFileName = null)
    {
        if (string.IsNullOrWhiteSpace(sourceZipPath) || !File.Exists(sourceZipPath))
            return ServiceResult<string>.Fail($"File not found: {sourceZipPath}");
        if (string.IsNullOrWhiteSpace(destZipPath))
            return ServiceResult<string>.Fail("No destination path for the derived pack.");
        if (Path.GetFullPath(sourceZipPath).Equals(
                Path.GetFullPath(destZipPath),
                StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<string>.Fail("Derived pack path must differ from the original file.");
        }

        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(confirmed);

        try
        {
            var destDir = Path.GetDirectoryName(destZipPath);
            if (!string.IsNullOrWhiteSpace(destDir))
                Directory.CreateDirectory(destDir);
            if (File.Exists(destZipPath))
                File.Delete(destZipPath);

            using var sourceStream = File.OpenRead(sourceZipPath);
            using var sourceZip = new ZipArchive(sourceStream, ZipArchiveMode.Read, leaveOpen: false);

            var rawNames = sourceZip.Entries
                .Select(e => MrpackAnalyzer.NormalizeZipPath(e.FullName))
                .Where(n => n.Length > 0 && !ManualServerPackAnalyzer.ShouldIgnoreEntry(n))
                .ToList();
            var wrapper = ManualServerPackAnalyzer.DetectWrapperPrefix(rawNames);

            var clientOnly = new HashSet<string>(
                analysis.ClientOnlyPaths.Select(NormalizeRelative),
                StringComparer.OrdinalIgnoreCase);

            var payloadEntries = new List<PayloadEntry>();
            foreach (var entry in sourceZip.Entries)
            {
                var raw = MrpackAnalyzer.NormalizeZipPath(entry.FullName);
                if (raw.Length == 0 || ManualServerPackAnalyzer.ShouldIgnoreEntry(raw) || raw.EndsWith('/'))
                    continue;

                var relative = ManualServerPackAnalyzer.StripWrapper(raw, wrapper);
                if (relative.Length == 0)
                    continue;
                if (!TryMapToOverrides(relative, analysis.MapRootJarsToMods, out var overridePath))
                    continue;

                using var ms = new MemoryStream();
                using (var input = entry.Open())
                    input.CopyTo(ms);
                var bytes = ms.ToArray();
                payloadEntries.Add(new PayloadEntry(overridePath, bytes));
            }

            var indexFiles = new List<DerivedIndexFile>();
            foreach (var payload in payloadEntries)
            {
                var indexPath = payload.OverridePath.StartsWith("overrides/", StringComparison.OrdinalIgnoreCase)
                    ? payload.OverridePath["overrides/".Length..]
                    : payload.OverridePath;
                var serverEnv = IsClientOnlyPath(indexPath, clientOnly)
                    ? MrpackAnalyzer.EnvUnsupported
                    : MrpackAnalyzer.EnvRequired;
                indexFiles.Add(new DerivedIndexFile(
                    indexPath,
                    payload.Bytes,
                    serverEnv));
            }

            var sidecar = new DerivedPackSidecar
            {
                PackName = analysis.PackName,
                MinecraftVersion = confirmed.MinecraftVersion,
                Loader = confirmed.Loader,
                LoaderVersion = confirmed.LoaderVersion,
                JavaMajor = confirmed.JavaMajor,
                DetectedMinecraftVersion = analysis.DetectedMinecraftVersion,
                DetectedLoader = analysis.DetectedLoader,
                OriginalFileName = originalFileName ?? Path.GetFileName(sourceZipPath),
            };

            var versionId = string.IsNullOrWhiteSpace(analysis.VersionId) ? "derived" : analysis.VersionId;
            var depKey = DerivedPackIdentity.DependencyKey(confirmed.Loader);
            var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["minecraft"] = confirmed.MinecraftVersion,
                [depKey] = confirmed.LoaderVersion,
            };

            using (var destStream = File.Create(destZipPath))
            using (var destZip = new ZipArchive(destStream, ZipArchiveMode.Create, leaveOpen: false))
            {
                WriteTextEntry(
                    destZip,
                    DerivedPackIdentity.SidecarEntryName,
                    JsonSerializer.Serialize(sidecar, SidecarWriteOptions));

                var indexDoc = new DerivedIndexDocument
                {
                    FormatVersion = MrpackAnalyzer.SupportedFormatVersion,
                    Game = MrpackAnalyzer.ExpectedGame,
                    VersionId = versionId,
                    Name = analysis.PackName,
                    Dependencies = dependencies,
                    Files = indexFiles.Select(f => f.ToIndexFile()).ToList(),
                };
                WriteTextEntry(
                    destZip,
                    MrpackAnalyzer.IndexEntryName,
                    JsonSerializer.Serialize(indexDoc, IndexWriteOptions));

                foreach (var payload in payloadEntries)
                    WriteBinaryEntry(destZip, payload.OverridePath, payload.Bytes);
            }

            return ServiceResult<string>.Ok(Path.GetFullPath(destZipPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return ServiceResult<string>.Fail($"Cannot build derived pack: {ex.Message}");
        }
    }

    /// <summary>
    /// Builds into <c>data/imported-packs/&lt;folder&gt;/derived.zip</c> and returns the full path.
    /// </summary>
    public static ServiceResult<string> BuildIntoDataDirectory(
        string sourceZipPath,
        ManualServerPackAnalysis analysis,
        DerivedPackFields confirmed,
        string dataDirectory,
        string? originalFileName = null)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return ServiceResult<string>.Fail("No Manager data directory was provided.");

        var destDir = ImportedPackArchiveStore.DirectoryFor(
            dataDirectory,
            analysis.PackName,
            analysis.VersionId);
        Directory.CreateDirectory(destDir);
        var destPath = Path.Combine(destDir, "derived.zip");
        return Build(sourceZipPath, analysis, confirmed, destPath, originalFileName);
    }

    internal static bool TryMapToOverrides(
        string relative,
        bool mapRootJarsToMods,
        out string overridePath)
    {
        overridePath = "";
        var normalized = relative.Replace('\\', '/');
        var lower = normalized.ToLowerInvariant();

        foreach (var skip in SkipPrefixes)
        {
            if (lower.StartsWith(skip, StringComparison.Ordinal))
                return false;
        }

        if (string.Equals(normalized, DerivedPackIdentity.SidecarEntryName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, MrpackAnalyzer.IndexEntryName, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.Equals(normalized, "manifest.json", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var prefix in CopyPrefixes)
        {
            if (lower.Equals(prefix.TrimEnd('/'), StringComparison.Ordinal)
                || lower.StartsWith(prefix, StringComparison.Ordinal))
            {
                overridePath = "overrides/" + normalized;
                return true;
            }
        }

        if (!normalized.Contains('/')
            && normalized.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && mapRootJarsToMods
            && ManualPackFileFilter.IsRootModJar(normalized))
        {
            overridePath = "overrides/mods/" + normalized;
            return true;
        }

        return false;
    }

    private static bool IsClientOnlyPath(string indexPath, HashSet<string> clientOnly)
    {
        var normalized = indexPath.Replace('\\', '/');
        if (clientOnly.Contains(normalized))
            return true;
        var leaf = normalized.Contains('/')
            ? normalized[(normalized.LastIndexOf('/') + 1)..]
            : normalized;
        return clientOnly.Any(p =>
        {
            var cLeaf = p.Contains('/') ? p[(p.LastIndexOf('/') + 1)..] : p;
            return string.Equals(cLeaf, leaf, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static string NormalizeRelative(string path) =>
        (path ?? "").Replace('\\', '/').Trim();

    private static void WriteTextEntry(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void WriteBinaryEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var output = entry.Open();
        output.Write(content, 0, content.Length);
    }

    private static string HashHex(byte[] bytes, string algorithm) =>
        Convert.ToHexString(
            algorithm.Equals("sha512", StringComparison.OrdinalIgnoreCase)
                ? SHA512.HashData(bytes)
                : SHA1.HashData(bytes)).ToLowerInvariant();

    private sealed record PayloadEntry(string OverridePath, byte[] Bytes);

    private sealed record DerivedIndexFile(string Path, byte[] Bytes, string ServerEnv)
    {
        public DerivedIndexFileEntry ToIndexFile() => new()
        {
            Path = Path,
            Hashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sha1"] = HashHex(Bytes, "sha1"),
                ["sha512"] = HashHex(Bytes, "sha512"),
            },
            Env = new DerivedIndexEnv { Client = MrpackAnalyzer.EnvRequired, Server = ServerEnv },
            Downloads = [],
            FileSize = Bytes.Length,
        };
    }

    private sealed class DerivedIndexDocument
    {
        public int FormatVersion { get; set; }
        public string? Game { get; set; }
        public string? VersionId { get; set; }
        public string? Name { get; set; }
        public Dictionary<string, string>? Dependencies { get; set; }
        public List<DerivedIndexFileEntry>? Files { get; set; }
    }

    private sealed class DerivedIndexFileEntry
    {
        public string? Path { get; set; }
        public Dictionary<string, string>? Hashes { get; set; }
        public DerivedIndexEnv? Env { get; set; }
        public List<string>? Downloads { get; set; }
        public long FileSize { get; set; }
    }

    private sealed class DerivedIndexEnv
    {
        public string? Client { get; set; }
        public string? Server { get; set; }
    }
}
