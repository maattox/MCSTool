using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace McManager.Core.Setup;

/// <summary>
/// Writable Layer 2 overlay on the admin PC. Keep-excluded Layer 3 mods are
/// stored per original-archive SHA-256 so future installs of <em>that pack
/// file</em> skip the jar. Never mutates the embedded product overlay.
/// </summary>
public static class Layer2LocalOverlay
{
    public const string DirectoryName = "pack-lists";
    public const string FileName = "mcmgr-layer2-local.json";
    public const string IdentityPrefix = "sha256:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string FilePath(string dataDirectory) =>
        Path.Combine(dataDirectory, DirectoryName, FileName);

    public static string IdentityKey(string? sha256Hex)
    {
        var hex = (sha256Hex ?? "").Trim().ToLowerInvariant();
        if (hex.StartsWith(IdentityPrefix, StringComparison.OrdinalIgnoreCase))
            hex = hex[IdentityPrefix.Length..];
        return IdentityPrefix + hex;
    }

    public static string? TryHashFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var fs = File.OpenRead(path);
            var hash = SHA256.HashData(fs);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static ExcludeIncludeLists Load(string? dataDirectory)
    {
        if (string.IsNullOrWhiteSpace(dataDirectory))
            return ExcludeIncludeLists.Empty;
        var path = FilePath(dataDirectory);
        if (!File.Exists(path))
            return ExcludeIncludeLists.Empty;
        try
        {
            return ExcludeIncludeLists.Parse(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return ExcludeIncludeLists.Empty;
        }
    }

    /// <summary>Adds <paramref name="term"/> under <c>modpacks[sha256:…].excludes</c>. Not a global exclude.</summary>
    public static void PromoteExclude(string dataDirectory, string sha256Hex, string term)
    {
        MutatePack(dataDirectory, sha256Hex, term, (pack, needle) =>
        {
            pack.ForceIncludes.RemoveAll(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase));
            if (!pack.Excludes.Exists(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase)))
                pack.Excludes.Add(needle);
        }, createIfMissing: true);
    }

    /// <summary>
    /// Adds <paramref name="term"/> under <c>modpacks[sha256:…].forceIncludes</c> so Layer 1
    /// excludes and in-jar client tags do not skip it on later analyzes of this archive.
    /// </summary>
    public static void PromoteForceInclude(string dataDirectory, string sha256Hex, string term)
    {
        MutatePack(dataDirectory, sha256Hex, term, (pack, needle) =>
        {
            pack.Excludes.RemoveAll(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase));
            if (!pack.ForceIncludes.Exists(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase)))
                pack.ForceIncludes.Add(needle);
        }, createIfMissing: true);
    }

    /// <summary>
    /// Filename (or other overlay term) used when persisting an operator Skip.
    /// Same shape as crash Keep-excluded terms.
    /// </summary>
    public static string TermForRelativePath(string relativePath)
    {
        var n = (relativePath ?? "").Replace('\\', '/').Trim();
        if (n.Length == 0)
            return "";
        var slash = n.LastIndexOf('/');
        return slash < 0 ? n : n[(slash + 1)..];
    }

    /// <summary>
    /// Removes <paramref name="term"/> from <c>modpacks[sha256:…].excludes</c> (Unskip).
    /// No-op when the file, pack, or term is missing.
    /// </summary>
    public static void RemoveExclude(string dataDirectory, string sha256Hex, string term) =>
        MutatePack(dataDirectory, sha256Hex, term, (pack, needle) =>
            pack.Excludes.RemoveAll(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase)),
            createIfMissing: false);

    /// <summary>
    /// Removes <paramref name="term"/> from <c>modpacks[sha256:…].forceIncludes</c>.
    /// No-op when the file, pack, or term is missing.
    /// </summary>
    public static void RemoveForceInclude(string dataDirectory, string sha256Hex, string term) =>
        MutatePack(dataDirectory, sha256Hex, term, (pack, needle) =>
            pack.ForceIncludes.RemoveAll(s => string.Equals(s, needle, StringComparison.OrdinalIgnoreCase)),
            createIfMissing: false);

    private static void MutatePack(
        string dataDirectory,
        string sha256Hex,
        string term,
        Action<OverlayPackDto, string> mutate,
        bool createIfMissing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256Hex);
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        var key = IdentityKey(sha256Hex);
        var path = FilePath(dataDirectory);
        if (!File.Exists(path) && !createIfMissing)
            return;

        OverlayFileDto dto;
        if (File.Exists(path))
        {
            try
            {
                dto = JsonSerializer.Deserialize<OverlayFileDto>(File.ReadAllText(path), JsonOptions)
                    ?? new OverlayFileDto();
            }
            catch (JsonException)
            {
                if (!createIfMissing)
                    return;
                dto = new OverlayFileDto();
            }
        }
        else
        {
            dto = new OverlayFileDto();
        }

        dto.GlobalExcludes ??= [];
        dto.GlobalForceIncludes ??= [];
        dto.Modpacks ??= new Dictionary<string, OverlayPackDto>(StringComparer.OrdinalIgnoreCase);

        if (!dto.Modpacks.TryGetValue(key, out var pack) || pack is null)
        {
            if (!createIfMissing)
                return;
            pack = new OverlayPackDto();
            dto.Modpacks[key] = pack;
        }

        pack.Excludes ??= [];
        pack.ForceIncludes ??= [];
        mutate(pack, term.Trim());

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions) + "\n");
    }

    private sealed class OverlayFileDto
    {
        public List<string>? GlobalExcludes { get; set; }
        public List<string>? GlobalForceIncludes { get; set; }
        public Dictionary<string, OverlayPackDto>? Modpacks { get; set; }
    }

    private sealed class OverlayPackDto
    {
        public List<string> Excludes { get; set; } = [];
        public List<string> ForceIncludes { get; set; } = [];
    }
}
