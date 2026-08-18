using System.Text.Json;
using System.Text.Json.Serialization;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// PaperMC Fill v3 client (blueprint §17). Lists versions/builds and resolves STABLE
/// server:default download URL + SHA-256 from the JSON — never Fill v2 URL builders.
/// </summary>
public sealed class PaperFillV3Client
{
    public const string ProjectUrl = "https://fill.papermc.io/v3/projects/paper";
    public const string EmbeddedProjectFixtureName = "McManager.Core.Setup.paper-fill-v3-project.json";
    public const string UserAgent = "McManager/0.1 (https://github.com/maattox/oci-mc-server)";
    public const string StableChannel = "STABLE";
    public const string ServerDefaultDownloadKey = "server:default";
    public const string HashAlgorithm = "sha256";
    public const string LegacyV2Host = "api.papermc.io";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;

    public PaperFillV3Client(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        EnsureUserAgent(_http);
    }

    public static void EnsureUserAgent(HttpClient http)
    {
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
    }

    public static string VersionUrl(string minecraftVersion) =>
        $"{ProjectUrl}/versions/{EncodeVersion(minecraftVersion)}";

    public static string BuildsUrl(string minecraftVersion) =>
        $"{ProjectUrl}/versions/{EncodeVersion(minecraftVersion)}/builds";

    public async Task<ServiceResult<PaperFillProject>> GetProjectAsync(CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(ProjectUrl, cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<PaperFillProject>.Fail(text.Error!);
        var project = ParseProject(text.Value!);
        return project is null
            ? ServiceResult<PaperFillProject>.Fail("Fill v3 project JSON deserialized to null.")
            : ServiceResult<PaperFillProject>.Ok(project);
    }

    /// <summary>
    /// Setup Optimized Vanilla picker: live Fill v3 project list, else bundled fixture.
    /// </summary>
    public async Task<PaperCatalogResult> LoadProjectCatalogAsync(CancellationToken cancellationToken = default)
    {
        var live = await GetProjectAsync(cancellationToken).ConfigureAwait(false);
        if (live.Succeeded && live.Value is not null)
            return new PaperCatalogResult(live.Value, fromFixture: false, notes: "Loaded from Fill v3.");

        var fixture = LoadEmbeddedProjectFixture();
        var reason = string.IsNullOrWhiteSpace(live.Error) ? "Fill v3 unavailable" : live.Error.TrimEnd('.');
        return new PaperCatalogResult(
            fixture,
            fromFixture: true,
            notes: $"{reason}. Using bundled Paper version fixture.");
    }

    public static PaperFillProject LoadEmbeddedProjectFixture()
    {
        var assembly = typeof(PaperFillV3Client).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedProjectFixtureName)
            ?? throw new InvalidOperationException($"Embedded fixture missing: {EmbeddedProjectFixtureName}");
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        return ParseProject(json)
            ?? throw new InvalidOperationException("Paper project fixture deserialized to null.");
    }

    public async Task<ServiceResult<PaperFillVersionDocument>> GetVersionAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(VersionUrl(minecraftVersion), cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<PaperFillVersionDocument>.Fail(text.Error!);
        var doc = ParseVersion(text.Value!);
        return doc is null
            ? ServiceResult<PaperFillVersionDocument>.Fail("Fill v3 version JSON deserialized to null.")
            : ServiceResult<PaperFillVersionDocument>.Ok(doc);
    }

    public async Task<ServiceResult<IReadOnlyList<PaperFillBuild>>> GetBuildsAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var text = await GetTextAsync(BuildsUrl(minecraftVersion), cancellationToken).ConfigureAwait(false);
        if (!text.Succeeded)
            return ServiceResult<IReadOnlyList<PaperFillBuild>>.Fail(text.Error!);
        var builds = ParseBuilds(text.Value!);
        return builds is null
            ? ServiceResult<IReadOnlyList<PaperFillBuild>>.Fail("Fill v3 builds JSON deserialized to null.")
            : ServiceResult<IReadOnlyList<PaperFillBuild>>.Ok(builds);
    }

    public async Task<ServiceResult<PaperResolvedBuild>> ResolveStableBuildAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var builds = await GetBuildsAsync(minecraftVersion, cancellationToken).ConfigureAwait(false);
        if (!builds.Succeeded)
            return ServiceResult<PaperResolvedBuild>.Fail(builds.Error!);

        PaperFillVersionDocument? version = null;
        var versionResult = await GetVersionAsync(minecraftVersion, cancellationToken).ConfigureAwait(false);
        if (versionResult.Succeeded)
            version = versionResult.Value;

        return ResolveStable(minecraftVersion, builds.Value!, version);
    }

    public static PaperFillProject? ParseProject(string json)
    {
        if (TryParseError(json, out _))
            return null;
        return JsonSerializer.Deserialize<PaperFillProject>(json, JsonOptions);
    }

    public static PaperFillVersionDocument? ParseVersion(string json)
    {
        if (TryParseError(json, out _))
            return null;
        return JsonSerializer.Deserialize<PaperFillVersionDocument>(json, JsonOptions);
    }

    public static IReadOnlyList<PaperFillBuild>? ParseBuilds(string json)
    {
        if (TryParseError(json, out _))
            return null;
        return JsonSerializer.Deserialize<List<PaperFillBuild>>(json, JsonOptions);
    }

    public static bool TryParseError(string json, out PaperFillError? error)
    {
        error = null;
        var trimmed = json.AsSpan().TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return false;

        try
        {
            var parsed = JsonSerializer.Deserialize<PaperFillError>(json, JsonOptions);
            if (parsed is { Ok: false })
            {
                error = parsed;
                return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Family-map order from the API (newest family first), then each family's listed ids.
    /// Version ids are opaque strings — do not pattern-match.
    /// </summary>
    public static IReadOnlyList<string> FlattenVersionIds(PaperFillProject project)
    {
        var ids = new List<string>();
        foreach (var family in project.Versions.Values)
        {
            foreach (var id in family)
            {
                if (!string.IsNullOrWhiteSpace(id))
                    ids.Add(id.Trim());
            }
        }

        return ids;
    }

    public static string DefaultVersionId(PaperFillProject project) =>
        FlattenVersionIds(project).FirstOrDefault() ?? "";

    public static PaperFillBuild? SelectStableBuild(IEnumerable<PaperFillBuild> builds) =>
        builds
            .Where(b => string.Equals(b.Channel, StableChannel, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(b => b.Id)
            .FirstOrDefault();

    public static PaperFillDownload? ServerDefaultDownload(PaperFillBuild build)
    {
        if (build.Downloads.TryGetValue(ServerDefaultDownloadKey, out var download))
            return download;
        return null;
    }

    public static ServiceResult<PaperResolvedBuild> ResolveStable(
        string minecraftVersion,
        IReadOnlyList<PaperFillBuild> builds,
        PaperFillVersionDocument? version = null)
    {
        var mc = minecraftVersion.Trim();
        if (string.IsNullOrWhiteSpace(mc))
            return ServiceResult<PaperResolvedBuild>.Fail("Minecraft version is required.");

        var stable = SelectStableBuild(builds);
        if (stable is null)
        {
            return ServiceResult<PaperResolvedBuild>.Fail(
                $"No STABLE Paper build for Minecraft {mc} yet. Unstable channels are not installed automatically.");
        }

        var download = ServerDefaultDownload(stable);
        if (download is null
            || string.IsNullOrWhiteSpace(download.Url)
            || string.IsNullOrWhiteSpace(download.Checksums.Sha256))
        {
            return ServiceResult<PaperResolvedBuild>.Fail(
                $"STABLE Paper build {stable.Id} for {mc} is missing downloads[\"{ServerDefaultDownloadKey}\"] url or sha256.");
        }

        if (ContainsLegacyV2Host(download.Url))
        {
            return ServiceResult<PaperResolvedBuild>.Fail(
                "Fill v2 (api.papermc.io) download URLs are not supported; use the URL from Fill v3 JSON.");
        }

        return ServiceResult<PaperResolvedBuild>.Ok(new PaperResolvedBuild(
            minecraftVersion: mc,
            buildId: stable.Id,
            channel: stable.Channel,
            filename: download.Name,
            downloadUrl: download.Url,
            sha256: download.Checksums.Sha256,
            size: download.Size,
            minimumJavaVersion: version?.Version.Java.Version.Minimum,
            supportStatus: string.IsNullOrWhiteSpace(version?.Version.Support.Status)
                ? null
                : version!.Version.Support.Status,
            recommendedJvmFlags: version?.Version.Java.Flags.Recommended ?? []));
    }

    public static bool ContainsLegacyV2Host(string url) =>
        url.Contains(LegacyV2Host, StringComparison.OrdinalIgnoreCase);

    private static string EncodeVersion(string minecraftVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftVersion);
        return Uri.EscapeDataString(minecraftVersion.Trim());
    }

    private async Task<ServiceResult<string>> GetTextAsync(string url, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(url, cancellationToken).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (TryParseError(json, out var err))
        {
            var detail = err!.Message;
            if (string.IsNullOrWhiteSpace(detail))
                detail = err.Error;
            if (string.IsNullOrWhiteSpace(detail))
                detail = "Fill v3 error.";
            return ServiceResult<string>.Fail(detail);
        }

        if (!response.IsSuccessStatusCode)
            return ServiceResult<string>.Fail($"Fill v3 HTTP {(int)response.StatusCode} from {url}.");

        return ServiceResult<string>.Ok(json);
    }
}

public sealed class PaperCatalogResult
{
    public PaperCatalogResult(PaperFillProject project, bool fromFixture, string notes)
    {
        Project = project;
        FromFixture = fromFixture;
        Notes = notes;
    }

    public PaperFillProject Project { get; }
    public bool FromFixture { get; }
    public string Notes { get; }
}

public sealed class PaperResolvedBuild
{
    public PaperResolvedBuild(
        string minecraftVersion,
        int buildId,
        string channel,
        string filename,
        string downloadUrl,
        string sha256,
        long size,
        int? minimumJavaVersion,
        string? supportStatus,
        IReadOnlyList<string> recommendedJvmFlags)
    {
        MinecraftVersion = minecraftVersion;
        BuildId = buildId;
        Channel = channel;
        Filename = filename;
        DownloadUrl = downloadUrl;
        Sha256 = sha256;
        Size = size;
        MinimumJavaVersion = minimumJavaVersion;
        SupportStatus = supportStatus;
        RecommendedJvmFlags = recommendedJvmFlags;
    }

    public string MinecraftVersion { get; }
    public int BuildId { get; }
    public string Channel { get; }
    public string Filename { get; }
    public string DownloadUrl { get; }
    public string Sha256 { get; }
    public long Size { get; }
    public int? MinimumJavaVersion { get; }
    public string? SupportStatus { get; }
    public IReadOnlyList<string> RecommendedJvmFlags { get; }
    public string HashAlgorithm => PaperFillV3Client.HashAlgorithm;
}

public sealed class PaperFillError
{
    [JsonPropertyName("ok")]
    public bool? Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}

public sealed class PaperFillProject
{
    [JsonPropertyName("project")]
    public PaperFillProjectInfo Project { get; set; } = new();

    [JsonPropertyName("versions")]
    public Dictionary<string, List<string>> Versions { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PaperFillProjectInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public sealed class PaperFillVersionDocument
{
    [JsonPropertyName("version")]
    public PaperFillVersionInfo Version { get; set; } = new();

    [JsonPropertyName("builds")]
    public List<int> Builds { get; set; } = [];
}

public sealed class PaperFillVersionInfo
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("support")]
    public PaperFillSupport Support { get; set; } = new();

    [JsonPropertyName("java")]
    public PaperFillJava Java { get; set; } = new();
}

public sealed class PaperFillSupport
{
    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("end")]
    public string End { get; set; } = "";
}

public sealed class PaperFillJava
{
    [JsonPropertyName("version")]
    public PaperFillJavaVersion Version { get; set; } = new();

    [JsonPropertyName("flags")]
    public PaperFillJavaFlags Flags { get; set; } = new();
}

public sealed class PaperFillJavaVersion
{
    [JsonPropertyName("minimum")]
    public int? Minimum { get; set; }
}

public sealed class PaperFillJavaFlags
{
    [JsonPropertyName("recommended")]
    public List<string> Recommended { get; set; } = [];
}

public sealed class PaperFillBuild
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("time")]
    public DateTimeOffset? Time { get; set; }

    [JsonPropertyName("channel")]
    public string Channel { get; set; } = "";

    [JsonPropertyName("downloads")]
    public Dictionary<string, PaperFillDownload> Downloads { get; set; } = new(StringComparer.Ordinal);
}

public sealed class PaperFillDownload
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("checksums")]
    public PaperFillChecksums Checksums { get; set; } = new();
}

public sealed class PaperFillChecksums
{
    [JsonPropertyName("sha256")]
    public string Sha256 { get; set; } = "";
}
