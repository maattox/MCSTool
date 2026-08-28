using System.Net;
using System.Text.Json;
using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Unauthenticated GitHub Releases client. One GET to <c>/releases/latest</c>
/// (drafts and pre-releases are already excluded by that endpoint).
/// </summary>
public sealed class GitHubLatestReleaseClient
{
    public const string LatestUrl = "https://api.github.com/repos/maattox/oci-mc-server/releases/latest";
    public const string UserAgent = "McManager/0.1 (https://github.com/maattox/oci-mc-server)";
    public const string Accept = "application/vnd.github+json";
    public const string ApiVersion = "2022-11-28";
    public const int HttpTimeoutSeconds = 12;

    private readonly HttpClient _http;

    public GitHubLatestReleaseClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };
        EnsureHeaders(_http);
    }

    public static void EnsureHeaders(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        if (!http.DefaultRequestHeaders.Accept.Any())
            http.DefaultRequestHeaders.Accept.ParseAdd(Accept);
        if (!http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            http.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", ApiVersion);
    }

    /// <summary>
    /// Fetches the latest published Release. 404 / 429 / offline return
    /// <see cref="ServiceResult{T}.Fail"/> — callers must not retry in a loop.
    /// </summary>
    public async Task<ServiceResult<GitHubReleaseInfo>> GetLatestAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync(LatestUrl, cancellationToken).ConfigureAwait(false);
            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return ServiceResult<GitHubReleaseInfo>.Fail("No GitHub Release published yet.");
            if ((int)response.StatusCode == 429 || response.StatusCode == HttpStatusCode.Forbidden)
                return ServiceResult<GitHubReleaseInfo>.Fail("GitHub rate limit.");
            if (!response.IsSuccessStatusCode)
                return ServiceResult<GitHubReleaseInfo>.Fail($"GitHub HTTP {(int)response.StatusCode}.");

            var parsed = ParseLatest(json);
            return parsed is null
                ? ServiceResult<GitHubReleaseInfo>.Fail("GitHub latest-release JSON was missing a tag.")
                : ServiceResult<GitHubReleaseInfo>.Ok(parsed);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return ServiceResult<GitHubReleaseInfo>.Fail("GitHub request timed out.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<GitHubReleaseInfo>.Fail("GitHub unreachable.");
        }
        catch (Exception ex)
        {
            return ServiceResult<GitHubReleaseInfo>.Fail($"GitHub request failed: {ex.Message}");
        }
    }

    public static GitHubReleaseInfo? ParseLatest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return null;

            var tag = ReadString(root, "tag_name");
            if (string.IsNullOrWhiteSpace(tag))
                return null;

            var htmlUrl = ReadString(root, "html_url");
            if (string.IsNullOrWhiteSpace(htmlUrl))
                htmlUrl = ProgramPaths.GitHubUrl + "/releases";

            return new GitHubReleaseInfo(
                tag.Trim(),
                ReadString(root, "name").Trim(),
                ReadString(root, "body"),
                htmlUrl.Trim(),
                ReadBool(root, "draft"),
                ReadBool(root, "prerelease"),
                PreferInstallerAssetUrl(root));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal static string? PreferInstallerAssetUrl(JsonElement root)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        string? anyHttpsExe = null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object)
                continue;
            var name = ReadString(asset, "name");
            var url = ReadString(asset, "browser_download_url");
            if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.Contains("Setup", StringComparison.OrdinalIgnoreCase)
                || name.Contains("MCManager", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            anyHttpsExe ??= url;
        }

        return anyHttpsExe;
    }

    private static string ReadString(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return "";
        return el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : "";
    }

    private static bool ReadBool(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind is JsonValueKind.True;
}

public sealed class GitHubReleaseInfo
{
    public GitHubReleaseInfo(
        string tagName,
        string name,
        string body,
        string htmlUrl,
        bool draft,
        bool prerelease,
        string? installerAssetUrl)
    {
        TagName = tagName;
        Name = name;
        Body = body;
        HtmlUrl = htmlUrl;
        Draft = draft;
        Prerelease = prerelease;
        InstallerAssetUrl = installerAssetUrl;
    }

    public string TagName { get; }
    public string Name { get; }
    public string Body { get; }
    public string HtmlUrl { get; }
    public bool Draft { get; }
    public bool Prerelease { get; }
    public string? InstallerAssetUrl { get; }

    public string DisplayName =>
        string.IsNullOrWhiteSpace(Name) ? TagName : Name;
}
