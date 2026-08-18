using System.Net;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class NeoForgeMavenClientTests
{
    private const string Mc1211 = "1.21.1";

    [Fact]
    public void Parses_maven_xml_not_json_and_picks_highest_non_beta_for_mc()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"));
        Assert.NotNull(versions);
        Assert.Contains("21.1.98", versions);
        Assert.Contains("21.1.200-beta", versions);
        Assert.Contains("21.10.1", versions);

        var resolved = NeoForgeMavenClient.Resolve(Mc1211, versions);
        Assert.True(resolved.Succeeded, resolved.Error);
        var inst = resolved.Value!;
        Assert.Equal(Mc1211, inst.MinecraftVersion);
        Assert.Equal("neoforge", inst.Loader);
        Assert.Equal("21.1.98", inst.LoaderVersion);
        Assert.NotEqual("21.1.200-beta", inst.LoaderVersion);
        Assert.NotEqual("21.10.1", inst.LoaderVersion);
        Assert.NotEqual("21.8.31", inst.LoaderVersion);
        Assert.Equal("neoforge-21.1.98-installer.jar", inst.InstallerFilename);
        Assert.Equal(
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.98/neoforge-21.1.98-installer.jar",
            inst.InstallerDownloadUrl);
        Assert.Equal("libraries/net/neoforged/neoforge/21.1.98/unix_args.txt", inst.UnixArgsPath);
        Assert.Equal("none_published", inst.HashAlgorithm);
        Assert.Equal("argfile_tree", inst.ArtifactKind);
        Assert.Equal(21, inst.JavaMajor);
    }

    [Fact]
    public void Prefix_match_is_component_wise_not_string_starts_with()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"))!;
        var resolved = NeoForgeMavenClient.Resolve(Mc1211, versions);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("21.1.98", resolved.Value!.LoaderVersion);
        // Naive "21.1" prefix would also match 21.10.x; we match (minor, patch) tuples.
        Assert.StartsWith("21.1", "21.10.1", StringComparison.Ordinal);
        Assert.False("21.10.1".StartsWith("21.1.", StringComparison.Ordinal));
    }

    [Fact]
    public void Honors_pinned_beta_when_listed_for_the_game()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"))!;
        var resolved = NeoForgeMavenClient.Resolve(Mc1211, versions, neoForgeVersion: "21.1.200-beta");
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("21.1.200-beta", resolved.Value!.LoaderVersion);
        Assert.Contains("21.1.200-beta", resolved.Value.InstallerDownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_pin_for_wrong_minecraft()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"))!;
        var resolved = NeoForgeMavenClient.Resolve(Mc1211, versions, neoForgeVersion: "21.8.31");
        Assert.False(resolved.Succeeded);
        Assert.Contains("does not target", resolved.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_minecraft_1_20_1_and_older()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"))!;
        var old = NeoForgeMavenClient.Resolve("1.20.1", versions);
        Assert.False(old.Succeeded);
        Assert.Contains("1.20.2", old.Error, StringComparison.Ordinal);
        Assert.Contains("Forge", old.Error, StringComparison.Ordinal);

        var older = NeoForgeMavenClient.Resolve("1.19.2", versions);
        Assert.False(older.Succeeded);

        Assert.False(NeoForgeMavenClient.IsSupportedMinecraft("1.20"));
        Assert.True(NeoForgeMavenClient.IsSupportedMinecraft("1.20.2"));
        Assert.True(NeoForgeMavenClient.IsSupportedMinecraft("1.21.1"));
        Assert.True(NeoForgeMavenClient.IsSupportedMinecraft("26.1"));
    }

    [Fact]
    public void Malformed_xml_fails_closed()
    {
        Assert.Null(NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata-malformed.xml")));
        Assert.Null(NeoForgeMavenClient.ParseVersions("{ \"not\": \"xml\" }"));
    }

    [Fact]
    public void Empty_match_fails_for_unknown_game()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"))!;
        var resolved = NeoForgeMavenClient.Resolve("1.22.99", versions);
        Assert.False(resolved.Succeeded);
        Assert.Contains("No stable", resolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Java_major_uses_static_table()
    {
        Assert.Equal(21, NeoForgeMavenClient.JavaMajorForMinecraft("1.21.1"));
        Assert.Equal(21, NeoForgeMavenClient.JavaMajorForMinecraft("1.20.5"));
        Assert.Equal(17, NeoForgeMavenClient.JavaMajorForMinecraft("1.20.4"));
        Assert.Equal(17, NeoForgeMavenClient.JavaMajorForMinecraft("1.20.2"));
        Assert.Equal(25, NeoForgeMavenClient.JavaMajorForMinecraft("26.1"));
    }

    [Fact]
    public async Task Http_client_uses_maven_metadata_xml_user_agent_and_retries()
    {
        var handler = new MapHandler();
        handler.FailTimes = 2;
        handler.Map(NeoForgeMavenClient.MetadataUrl(), Read("neoforge-maven-metadata.xml"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new NeoForgeMavenClient(http);

        var resolved = await client.ResolveInstallerAsync(Mc1211);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("21.1.98", resolved.Value!.LoaderVersion);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("maven.neoforged.net", r.RequestUri!.Host);
            Assert.EndsWith("/maven-metadata.xml", r.RequestUri.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("McManager", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("github.com/maattox/oci-mc-server", r.UserAgent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Network_failure_names_maven_neoforged_net()
    {
        var handler = new MapHandler { AlwaysThrow = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new NeoForgeMavenClient(http);
        var resolved = await client.ResolveInstallerAsync(Mc1211);
        Assert.False(resolved.Succeeded);
        Assert.Contains("maven.neoforged.net", resolved.Error, StringComparison.Ordinal);
        Assert.Equal(NeoForgeMavenClient.MaxAttempts, handler.Requests.Count);
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
        public int FailTimes { get; set; }
        public bool AlwaysThrow { get; set; }
        private int _failures;

        public void Map(string url, string body, HttpStatusCode status = HttpStatusCode.OK) =>
            _map[url] = (status, body);

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var ua = request.Headers.UserAgent.ToString();
            Requests.Add((request.RequestUri!, ua));

            if (AlwaysThrow)
                throw new HttpRequestException("simulated network down");

            if (_failures < FailTimes)
            {
                _failures++;
                throw new HttpRequestException("transient");
            }

            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var mapped))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"unmapped {url}", Encoding.UTF8, "application/xml"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(mapped.Status)
            {
                Content = new StringContent(mapped.Body, Encoding.UTF8, "application/xml"),
            });
        }
    }
}
