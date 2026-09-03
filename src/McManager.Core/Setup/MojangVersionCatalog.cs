using System.Text.Json;
using System.Text.Json.Serialization;

namespace McManager.Core.Setup;

/// <summary>
/// Read-only Mojang version list for the Setup picker (blueprint §13.3).
/// Persists version <em>id</em> only — jar URL/sha1 are not install source of truth.
/// </summary>
public sealed class MojangVersionCatalog
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    public const string EmbeddedFixtureName = "McManager.Core.Setup.mojang-version-manifest-v2.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public MojangVersionCatalog(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("MCSTool/0.1 (Setup version picker)");
    }

    public async Task<MojangCatalogResult> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(ManifestUrl, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var manifest = Parse(json)
                ?? throw new InvalidOperationException("Mojang manifest deserialized to null.");
            return new MojangCatalogResult(manifest, fromFixture: false, notes: "Loaded from piston-meta.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var fixture = LoadEmbeddedFixture();
            return new MojangCatalogResult(
                fixture,
                fromFixture: true,
                notes: $"Live manifest unavailable ({ex.Message.TrimEnd('.')}). Using bundled fixture.");
        }
    }

    public static MojangVersionManifest LoadEmbeddedFixture()
    {
        var assembly = typeof(MojangVersionCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedFixtureName)
            ?? throw new InvalidOperationException($"Embedded fixture missing: {EmbeddedFixtureName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return Parse(json) ?? throw new InvalidOperationException("Fixture deserialized to null.");
    }

    public static MojangVersionManifest? Parse(string json) =>
        JsonSerializer.Deserialize<MojangVersionManifest>(json, JsonOptions);

    public static IReadOnlyList<MojangVersionInfo> Filter(
        MojangVersionManifest manifest,
        bool includeSnapshots)
    {
        var list = manifest.Versions
            .Where(v =>
                string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase)
                || (includeSnapshots && string.Equals(v.Type, "snapshot", StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(v => v.ReleaseTime ?? DateTimeOffset.MinValue)
            .ToList();
        return list;
    }

    public static string DefaultVersionId(MojangVersionManifest manifest)
    {
        var latest = manifest.Latest.Release;
        if (!string.IsNullOrWhiteSpace(latest)
            && manifest.Versions.Any(v => string.Equals(v.Id, latest, StringComparison.Ordinal)))
        {
            return latest;
        }

        return manifest.Versions
            .FirstOrDefault(v => string.Equals(v.Type, "release", StringComparison.OrdinalIgnoreCase))
            ?.Id ?? "";
    }
}

public sealed class MojangCatalogResult
{
    public MojangCatalogResult(MojangVersionManifest manifest, bool fromFixture, string notes)
    {
        Manifest = manifest;
        FromFixture = fromFixture;
        Notes = notes;
    }

    public MojangVersionManifest Manifest { get; }
    public bool FromFixture { get; }
    public string Notes { get; }
}

public sealed class MojangVersionManifest
{
    [JsonPropertyName("latest")]
    public MojangLatest Latest { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<MojangVersionInfo> Versions { get; set; } = [];
}

public sealed class MojangLatest
{
    [JsonPropertyName("release")]
    public string Release { get; set; } = "";

    [JsonPropertyName("snapshot")]
    public string Snapshot { get; set; } = "";
}

public sealed class MojangVersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("releaseTime")]
    public DateTimeOffset? ReleaseTime { get; set; }
}
