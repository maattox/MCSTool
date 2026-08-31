using System.Net;
using System.Text;
using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class GitHubLatestReleaseClientTests
{
    private const string Local = "0.1.0";

    [Fact]
    public void Parses_newer_latest_fixture_into_prompt_payload()
    {
        var info = GitHubLatestReleaseClient.ParseLatest(Read("release-latest-newer.json"));
        Assert.NotNull(info);
        Assert.Equal("v0.2.0", info.TagName);
        Assert.Equal("MCSTool 0.2.0", info.Name);
        Assert.Contains("Inno Setup", info.Body, StringComparison.Ordinal);
        Assert.Equal("https://github.com/maattox/MCSTool/releases/tag/v0.2.0", info.HtmlUrl);
        Assert.False(info.Draft);
        Assert.False(info.Prerelease);
        Assert.Equal(
            "https://github.com/maattox/MCSTool/releases/download/v0.2.0/MCSTool-Setup-0.2.0.exe",
            info.InstallerAssetUrl);

        Assert.True(AppUpdateCheck.IsNewerThan(Local, info.TagName));
        var prompt = AppUpdateCheck.TryBuildPrompt(Local, info);
        Assert.NotNull(prompt);
        Assert.Equal("MCSTool 0.2.0", prompt.Title);
        Assert.Contains("update check", prompt.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(info.HtmlUrl, prompt.OpenUrl);
    }

    [Fact]
    public void Same_tag_does_not_build_a_prompt()
    {
        var info = GitHubLatestReleaseClient.ParseLatest(Read("release-latest-same.json"));
        Assert.NotNull(info);
        Assert.Equal("v0.1.0", info.TagName);
        Assert.False(AppUpdateCheck.IsNewerThan(Local, info.TagName));
        Assert.Null(AppUpdateCheck.TryBuildPrompt(Local, info));
    }

    [Fact]
    public void Parses_draft_shaped_body_without_requiring_live_github()
    {
        var info = GitHubLatestReleaseClient.ParseLatest(Read("release-latest-draft-shaped.json"));
        Assert.NotNull(info);
        Assert.Equal("v9.9.9", info.TagName);
        Assert.True(info.Draft);
        Assert.True(info.Prerelease);
        Assert.Contains("/releases/latest", info.Name, StringComparison.Ordinal);
        Assert.Contains("drafts are ignored", info.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("0.1.0", "v0.2.0", true)]
    [InlineData("0.1.0", "0.2.0", true)]
    [InlineData("0.1.0", "v0.1.0", false)]
    [InlineData("0.1.0", "v0.0.9", false)]
    [InlineData("0.1.0", "v0.1.0+build.5", false)]
    [InlineData("0.1.0", "not-a-version", false)]
    [InlineData("0.1.0+abc", "v0.1.1", true)]
    public void Compares_stripped_tags_to_local_version(string local, string tag, bool newer)
    {
        Assert.Equal(newer, AppUpdateCheck.IsNewerThan(local, tag));
    }

    [Fact]
    public async Task Newer_tag_from_http_fixture_yields_prompt_and_descriptive_user_agent()
    {
        var handler = new MapHandler();
        handler.Map(GitHubLatestReleaseClient.LatestUrl, Read("release-latest-newer.json"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: true, Local, client);
        Assert.NotNull(prompt);
        Assert.Equal("MCSTool 0.2.0", prompt.Title);
        Assert.Equal("https://github.com/maattox/MCSTool/releases/tag/v0.2.0", prompt.OpenUrl);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(GitHubLatestReleaseClient.LatestUrl, request.RequestUri.ToString());
        Assert.Contains("McManager", request.UserAgent, StringComparison.Ordinal);
        Assert.Contains("github.com/maattox/MCSTool", request.UserAgent, StringComparison.Ordinal);
        Assert.Contains("application/vnd.github+json", request.Accept, StringComparison.Ordinal);
        Assert.Null(request.Authorization);
    }

    [Fact]
    public async Task Same_tag_from_http_fixture_yields_no_prompt()
    {
        var handler = new MapHandler();
        handler.Map(GitHubLatestReleaseClient.LatestUrl, Read("release-latest-same.json"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: true, Local, client);
        Assert.Null(prompt);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Toggle_off_does_not_call_github()
    {
        var handler = new MapHandler();
        handler.Map(GitHubLatestReleaseClient.LatestUrl, Read("release-latest-newer.json"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: false, Local, client);
        Assert.Null(prompt);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Http_404_is_quiet_no_prompt_no_retry()
    {
        var handler = new MapHandler();
        handler.Map(
            GitHubLatestReleaseClient.LatestUrl,
            """{"message":"Not Found"}""",
            HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: true, Local, client);
        Assert.Null(prompt);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Http_429_is_quiet_no_prompt_single_get()
    {
        var handler = new MapHandler();
        handler.Map(
            GitHubLatestReleaseClient.LatestUrl,
            """{"message":"API rate limit exceeded"}""",
            (HttpStatusCode)429);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: true, Local, client);
        Assert.Null(prompt);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Offline_http_exception_is_quiet()
    {
        var handler = new MapHandler { ThrowOnSend = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new GitHubLatestReleaseClient(http);

        var latest = await client.GetLatestAsync();
        Assert.False(latest.Succeeded);
        Assert.Contains("unreachable", latest.Error, StringComparison.OrdinalIgnoreCase);
        var prompt = await AppUpdateCheck.EvaluateAsync(checkForUpdates: true, Local, client);
        Assert.Null(prompt);
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "github", fileName);
        Assert.True(File.Exists(path), $"Fixture missing at {path}");
        return File.ReadAllText(path);
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _map =
            new(StringComparer.Ordinal);

        public List<(Uri RequestUri, string UserAgent, string Accept, string? Authorization)> Requests { get; } = [];

        public bool ThrowOnSend { get; set; }

        public void Map(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _map[url] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var ua = request.Headers.UserAgent.ToString();
            var accept = request.Headers.Accept.ToString();
            var auth = request.Headers.Authorization?.ToString();
            Requests.Add((request.RequestUri!, ua, accept, auth));

            if (ThrowOnSend)
                throw new HttpRequestException("simulated offline");

            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var mapped))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"unmapped {url}", Encoding.UTF8, "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(mapped.Status)
            {
                Content = new StringContent(mapped.Body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
