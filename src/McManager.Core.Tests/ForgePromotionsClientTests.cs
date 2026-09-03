using System.Net;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ForgePromotionsClientTests
{
    private const string Mc1122 = "1.12.2";
    private const string Mc1201 = "1.20.1";

    [Fact]
    public void Prefers_recommended_over_latest_for_legacy_1_12_2()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"));
        Assert.NotNull(promos);

        var resolved = ForgePromotionsClient.Resolve(Mc1122, promos);
        Assert.True(resolved.Succeeded, resolved.Error);
        var inst = resolved.Value!;
        Assert.Equal(Mc1122, inst.MinecraftVersion);
        Assert.Equal("forge", inst.Loader);
        Assert.Equal("14.23.5.2854", inst.LoaderVersion);
        Assert.NotEqual("14.23.5.2860", inst.LoaderVersion);
        Assert.Equal("recommended", inst.PromoUsed);
        Assert.Equal("single_jar", inst.ArtifactKind);
        Assert.Equal("forge-1.12.2-14.23.5.2854-installer.jar", inst.InstallerFilename);
        Assert.Equal("forge-1.12.2-14.23.5.2854.jar", inst.RunnableJarFilename);
        Assert.Null(inst.UnixArgsPath);
        Assert.Equal(
            "https://maven.minecraftforge.net/net/minecraftforge/forge/1.12.2-14.23.5.2854/forge-1.12.2-14.23.5.2854-installer.jar",
            inst.InstallerDownloadUrl);
        Assert.DoesNotContain("files.minecraftforge.net", inst.InstallerDownloadUrl, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("none_published", inst.HashAlgorithm);
        Assert.Equal(8, inst.JavaMajor);
    }

    [Fact]
    public void One_twenty_one_is_argfile_tree_and_prefers_recommended()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var resolved = ForgePromotionsClient.Resolve(Mc1201, promos);
        Assert.True(resolved.Succeeded, resolved.Error);
        var inst = resolved.Value!;
        Assert.Equal("47.4.10", inst.LoaderVersion);
        Assert.NotEqual("47.4.13", inst.LoaderVersion);
        Assert.Equal("argfile_tree", inst.ArtifactKind);
        Assert.Equal("libraries/net/minecraftforge/forge/1.20.1-47.4.10/unix_args.txt", inst.UnixArgsPath);
        Assert.Equal(17, inst.JavaMajor);
        Assert.Equal("recommended", inst.PromoUsed);
    }

    [Fact]
    public void Falls_back_to_latest_when_recommended_is_absent()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var resolved = ForgePromotionsClient.Resolve("1.17.1", promos);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("37.1.1", resolved.Value!.LoaderVersion);
        Assert.Equal("latest", resolved.Value.PromoUsed);
        Assert.Equal("argfile_tree", resolved.Value.ArtifactKind);
        Assert.Equal(16, resolved.Value.JavaMajor);
    }

    [Fact]
    public void Honors_pinned_legacy_version_even_when_not_recommended()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var resolved = ForgePromotionsClient.Resolve(Mc1122, promos, forgeVersion: "14.23.5.2860");
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("14.23.5.2860", resolved.Value!.LoaderVersion);
        Assert.Equal("pinned", resolved.Value.PromoUsed);
        Assert.Contains("14.23.5.2860", resolved.Value.InstallerDownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_invalid_pin()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var resolved = ForgePromotionsClient.Resolve(Mc1122, promos, forgeVersion: "not-a-version");
        Assert.False(resolved.Succeeded);
        Assert.Contains("not a valid Forge version", resolved.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Refuses_minecraft_older_than_1_7()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var old = ForgePromotionsClient.Resolve("1.6.4", promos);
        Assert.False(old.Succeeded);
        Assert.Contains("1.7", old.Error, StringComparison.Ordinal);

        Assert.False(ForgePromotionsClient.IsSupportedMinecraft("1.6.4"));
        Assert.True(ForgePromotionsClient.IsSupportedMinecraft("1.7.10"));
        Assert.True(ForgePromotionsClient.IsSupportedMinecraft("1.12.2"));
        Assert.True(ForgePromotionsClient.IsSupportedMinecraft("1.20.1"));
        Assert.True(ForgePromotionsClient.IsSupportedMinecraft("26.1"));
    }

    [Fact]
    public void Malformed_json_fails_closed()
    {
        Assert.Null(ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim-malformed.json")));
        Assert.Null(ForgePromotionsClient.ParsePromos("<metadata></metadata>"));
    }

    [Fact]
    public void Empty_match_fails_for_unknown_game()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"))!;
        var resolved = ForgePromotionsClient.Resolve("1.22.99", promos);
        Assert.False(resolved.Succeeded);
        Assert.Contains("No Forge", resolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Launch_shape_changes_at_1_17()
    {
        Assert.False(ForgePromotionsClient.UsesArgfileTree("1.12.2"));
        Assert.False(ForgePromotionsClient.UsesArgfileTree("1.16.5"));
        Assert.True(ForgePromotionsClient.UsesArgfileTree("1.17.1"));
        Assert.True(ForgePromotionsClient.UsesArgfileTree("1.20.1"));
        Assert.True(ForgePromotionsClient.UsesArgfileTree("26.1"));
    }

    [Fact]
    public void Java_major_uses_static_table()
    {
        Assert.Equal(8, ForgePromotionsClient.JavaMajorForMinecraft("1.12.2"));
        Assert.Equal(8, ForgePromotionsClient.JavaMajorForMinecraft("1.16.5"));
        Assert.Equal(16, ForgePromotionsClient.JavaMajorForMinecraft("1.17.1"));
        Assert.Equal(17, ForgePromotionsClient.JavaMajorForMinecraft("1.20.1"));
        Assert.Equal(21, ForgePromotionsClient.JavaMajorForMinecraft("1.20.5"));
        Assert.Equal(21, ForgePromotionsClient.JavaMajorForMinecraft("1.21.1"));
        Assert.Equal(25, ForgePromotionsClient.JavaMajorForMinecraft("26.1"));
    }

    [Fact]
    public async Task Http_client_uses_promotions_slim_user_agent_and_retries()
    {
        var handler = new MapHandler();
        handler.FailTimes = 2;
        handler.Map(ForgePromotionsClient.PromotionsUrl, Read("forge-promotions-slim.json"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new ForgePromotionsClient(http);

        var resolved = await client.ResolveInstallerAsync(Mc1122);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("14.23.5.2854", resolved.Value!.LoaderVersion);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("files.minecraftforge.net", r.RequestUri!.Host);
            Assert.EndsWith("/promotions_slim.json", r.RequestUri.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("MCSTool/", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("github.com/maattox/MCSTool", r.UserAgent, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Network_failure_names_files_minecraftforge_net()
    {
        var handler = new MapHandler { AlwaysThrow = true };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new ForgePromotionsClient(http);
        var resolved = await client.ResolveInstallerAsync(Mc1122);
        Assert.False(resolved.Succeeded);
        Assert.Contains("files.minecraftforge.net", resolved.Error, StringComparison.Ordinal);
        Assert.Equal(ForgePromotionsClient.MaxAttempts, handler.Requests.Count);
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
