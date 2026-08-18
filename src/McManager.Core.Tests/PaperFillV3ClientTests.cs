using System.Net;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PaperFillV3ClientTests
{
    private const string Mc12110 = "1.21.10";

    [Fact]
    public void Lists_version_ids_from_project_fixture_in_api_order()
    {
        var project = PaperFillV3Client.ParseProject(Read("paper-fill-v3-project.json"));
        Assert.NotNull(project);
        Assert.Equal("paper", project.Project.Id);

        var ids = PaperFillV3Client.FlattenVersionIds(project);
        Assert.Equal(
            ["26.2", "26.2-rc-2", "1.21.11", "1.21.10", "1.21.1", "1.20.1"],
            ids);
        Assert.Equal("26.2", PaperFillV3Client.DefaultVersionId(project));
    }

    [Fact]
    public void Parses_builds_and_prefers_highest_stable_id()
    {
        var builds = PaperFillV3Client.ParseBuilds(Read("paper-fill-v3-builds-1.21.10.json"));
        Assert.NotNull(builds);
        Assert.Equal(3, builds.Count);
        Assert.Equal("BETA", builds[0].Channel);

        var stable = PaperFillV3Client.SelectStableBuild(builds);
        Assert.NotNull(stable);
        Assert.Equal(130, stable.Id);
        Assert.Equal("STABLE", stable.Channel);
    }

    [Fact]
    public void Resolves_download_url_and_sha256_from_json_not_v2_url_builder()
    {
        var builds = PaperFillV3Client.ParseBuilds(Read("paper-fill-v3-builds-1.21.10.json"))!;
        var version = PaperFillV3Client.ParseVersion(Read("paper-fill-v3-version-1.21.10.json"));
        var resolved = PaperFillV3Client.ResolveStable(Mc12110, builds, version);

        Assert.True(resolved.Succeeded, resolved.Error);
        var build = resolved.Value!;
        Assert.Equal(Mc12110, build.MinecraftVersion);
        Assert.Equal(130, build.BuildId);
        Assert.Equal("STABLE", build.Channel);
        Assert.Equal("paper-1.21.10-130.jar", build.Filename);
        Assert.Equal(
            "https://fill-data.papermc.io/v1/objects/158703f75a26f842ea656b3dc6d75bf3d1ec176b97a2c36384d0b80b3871af53/paper-1.21.10-130.jar",
            build.DownloadUrl);
        Assert.Equal("158703f75a26f842ea656b3dc6d75bf3d1ec176b97a2c36384d0b80b3871af53", build.Sha256);
        Assert.Equal("sha256", build.HashAlgorithm);
        Assert.Equal(54475623, build.Size);
        Assert.Equal(21, build.MinimumJavaVersion);
        Assert.Equal("UNSUPPORTED", build.SupportStatus);
        Assert.Contains("-XX:+UseG1GC", build.RecommendedJvmFlags);
        Assert.DoesNotContain("api.papermc.io", build.DownloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.False(PaperFillV3Client.ContainsLegacyV2Host(build.DownloadUrl));
    }

    [Fact]
    public void Rejects_legacy_fill_v2_download_url_from_json()
    {
        const string json = """
            [
              {
                "id": 1,
                "channel": "STABLE",
                "downloads": {
                  "server:default": {
                    "name": "paper-1.21.10-1.jar",
                    "url": "https://api.papermc.io/v2/projects/paper/versions/1.21.10/builds/1/downloads/paper-1.21.10-1.jar",
                    "checksums": { "sha256": "abc" },
                    "size": 1
                  }
                }
              }
            ]
            """;
        var builds = PaperFillV3Client.ParseBuilds(json)!;
        var resolved = PaperFillV3Client.ResolveStable(Mc12110, builds);
        Assert.False(resolved.Succeeded);
        Assert.Contains("v2", resolved.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("api.papermc.io", resolved.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_fall_back_to_alpha_or_beta_when_no_stable()
    {
        const string json = """
            [
              {
                "id": 9,
                "channel": "ALPHA",
                "downloads": {
                  "server:default": {
                    "name": "paper-26.2-rc-2-9.jar",
                    "url": "https://fill-data.papermc.io/v1/objects/deadbeef/paper-26.2-rc-2-9.jar",
                    "checksums": { "sha256": "deadbeef" },
                    "size": 1
                  }
                }
              }
            ]
            """;
        var builds = PaperFillV3Client.ParseBuilds(json)!;
        Assert.Null(PaperFillV3Client.SelectStableBuild(builds));
        var resolved = PaperFillV3Client.ResolveStable("26.2-rc-2", builds);
        Assert.False(resolved.Succeeded);
        Assert.Contains("No STABLE", resolved.Error, StringComparison.Ordinal);
        Assert.Contains("Unstable", resolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Parses_fill_v3_error_payload()
    {
        var json = Read("paper-fill-v3-error.json");
        Assert.True(PaperFillV3Client.TryParseError(json, out var error));
        Assert.False(error!.Ok);
        Assert.Equal("version_not_found", error.Error);
        Assert.Equal("No version was found with the given identifier.", error.Message);
        Assert.Null(PaperFillV3Client.ParseBuilds(json));
        Assert.Null(PaperFillV3Client.ParseProject(json));
    }

    [Fact]
    public async Task Http_client_uses_v3_urls_descriptive_user_agent_and_fixtures()
    {
        var handler = new MapHandler();
        handler.Map(PaperFillV3Client.ProjectUrl, Read("paper-fill-v3-project.json"));
        handler.Map(PaperFillV3Client.VersionUrl(Mc12110), Read("paper-fill-v3-version-1.21.10.json"));
        handler.Map(PaperFillV3Client.BuildsUrl(Mc12110), Read("paper-fill-v3-builds-1.21.10.json"));
        handler.Map(
            PaperFillV3Client.BuildsUrl("not-a-version"),
            Read("paper-fill-v3-error.json"),
            HttpStatusCode.NotFound);
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new PaperFillV3Client(http);

        var project = await client.GetProjectAsync();
        Assert.True(project.Succeeded, project.Error);
        Assert.Equal("26.2", PaperFillV3Client.DefaultVersionId(project.Value!));

        var resolved = await client.ResolveStableBuildAsync(Mc12110);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal(130, resolved.Value!.BuildId);
        Assert.Equal(21, resolved.Value.MinimumJavaVersion);

        var missing = await client.GetBuildsAsync("not-a-version");
        Assert.False(missing.Succeeded);
        Assert.Contains("No version was found", missing.Error, StringComparison.Ordinal);

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("fill.papermc.io", r.RequestUri!.Host);
            Assert.StartsWith("/v3/projects/paper", r.RequestUri.AbsolutePath, StringComparison.Ordinal);
            Assert.DoesNotContain("api.papermc.io", r.RequestUri.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.Contains("McManager", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("github.com/maattox/oci-mc-server", r.UserAgent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void Url_helpers_stay_on_fill_v3()
    {
        Assert.Equal("https://fill.papermc.io/v3/projects/paper", PaperFillV3Client.ProjectUrl);
        Assert.Equal(
            "https://fill.papermc.io/v3/projects/paper/versions/1.21.10/builds",
            PaperFillV3Client.BuildsUrl(Mc12110));
        Assert.Equal(
            "https://fill.papermc.io/v3/projects/paper/versions/26.2-rc-2",
            PaperFillV3Client.VersionUrl("26.2-rc-2"));
        Assert.DoesNotContain(PaperFillV3Client.LegacyV2Host, PaperFillV3Client.BuildsUrl(Mc12110));
    }

    private static string Read(string fileName)
    {
        var path = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "game-metadata",
            fileName);
        Assert.True(File.Exists(path), $"Fixture missing at {path}");
        return File.ReadAllText(path);
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _map =
            new(StringComparer.Ordinal);

        public List<(Uri RequestUri, string UserAgent)> Requests { get; } = [];

        public void Map(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _map[url] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            var ua = request.Headers.UserAgent.ToString();
            Requests.Add((request.RequestUri!, ua));

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
