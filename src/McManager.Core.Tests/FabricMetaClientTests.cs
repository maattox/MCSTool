using System.Net;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FabricMetaClientTests
{
    private const string Mc1218 = "1.21.8";

    [Fact]
    public void Picks_first_stable_loader_and_installer_not_newer_unstable()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-1.21.8.json"));
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"));
        Assert.NotNull(loaders);
        Assert.NotNull(installers);

        var loader = FabricMetaClient.SelectStableLoader(loaders);
        var installer = FabricMetaClient.SelectStableInstaller(installers);
        Assert.NotNull(loader);
        Assert.Equal("0.17.2", loader.Loader.Version);
        Assert.NotNull(installer);
        Assert.Equal("1.1.0", installer.Version);
        Assert.NotEqual("1.1.2", installer.Version);
        Assert.NotEqual("0.19.3", loader.Loader.Version);
    }

    [Fact]
    public void Resolves_three_axis_server_jar_url_and_launcher_filename()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-1.21.8.json"))!;
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"))!;
        var resolved = FabricMetaClient.Resolve(Mc1218, loaders, installers);

        Assert.True(resolved.Succeeded, resolved.Error);
        var launcher = resolved.Value!;
        Assert.Equal(Mc1218, launcher.MinecraftVersion);
        Assert.Equal("fabric", launcher.Loader);
        Assert.Equal("0.17.2", launcher.LoaderVersion);
        Assert.Equal("1.1.0", launcher.InstallerVersion);
        Assert.Equal("fabric-server-mc.1.21.8-loader.0.17.2-launcher.1.1.0.jar", launcher.Filename);
        Assert.Equal(
            "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/1.1.0/server/jar",
            launcher.DownloadUrl);
        Assert.Equal("none_published", launcher.HashAlgorithm);
        Assert.Equal("launcher_jar", launcher.ArtifactKind);
        Assert.Equal(21, launcher.JavaMajor);
        Assert.Equal(3, FabricMetaClient.CountVersionAxes(launcher.DownloadUrl));
        Assert.False(
            launcher.DownloadUrl.EndsWith("/0.17.2/server/jar", StringComparison.Ordinal),
            "installer version segment must not be omitted");
    }

    [Fact]
    public void Honors_pinned_unstable_loader_when_listed_for_the_game()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-1.21.8.json"))!;
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"))!;
        var resolved = FabricMetaClient.Resolve(Mc1218, loaders, installers, loaderVersion: "0.19.3");
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("0.19.3", resolved.Value!.LoaderVersion);
        Assert.Contains("/0.19.3/1.1.0/server/jar", resolved.Value.DownloadUrl, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_loader_pin_not_valid_for_game()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-1.21.8.json"))!;
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"))!;
        var resolved = FabricMetaClient.Resolve(Mc1218, loaders, installers, loaderVersion: "0.99.0");
        Assert.False(resolved.Succeeded);
        Assert.Contains("0.99.0", resolved.Error, StringComparison.Ordinal);
        Assert.Contains("not valid", resolved.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_fall_back_to_unstable_when_no_stable_loader()
    {
        var loaders = FabricMetaClient.ParseGameLoaders("""
            [
              {
                "loader": { "version": "0.19.3", "stable": false, "maven": "net.fabricmc:fabric-loader:0.19.3" },
                "intermediary": { "version": "1.21.8", "stable": true }
              }
            ]
            """)!;
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"))!;
        var resolved = FabricMetaClient.Resolve(Mc1218, loaders, installers);
        Assert.False(resolved.Succeeded);
        Assert.Contains("No stable Fabric loader", resolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Empty_loader_list_fails_for_unknown_game()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-unknown.json"))!;
        var installers = FabricMetaClient.ParseInstallers(Read("fabric-meta-installer.json"))!;
        Assert.Empty(loaders);
        var resolved = FabricMetaClient.Resolve("not-a-version", loaders, installers);
        Assert.False(resolved.Succeeded);
        Assert.Contains("No stable Fabric loader", resolved.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Server_jar_url_requires_installer_segment()
    {
        var url = FabricMetaClient.ServerJarUrl("1.21.8", "0.17.2", "1.1.0");
        Assert.Equal("https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/1.1.0/server/jar", url);
        Assert.Equal(3, FabricMetaClient.CountVersionAxes(url));
        Assert.Throws<ArgumentException>(() => FabricMetaClient.ServerJarUrl("1.21.8", "0.17.2", " "));
        Assert.Equal(
            2,
            FabricMetaClient.CountVersionAxes(
                "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/server/jar"));
    }

    [Fact]
    public void Java_major_uses_minecraft_table_not_launcher_min_java()
    {
        Assert.Equal(21, FabricMetaClient.JavaMajorForMinecraft("1.21.8"));
        Assert.Equal(21, FabricMetaClient.JavaMajorForMinecraft("1.20.5"));
        Assert.Equal(17, FabricMetaClient.JavaMajorForMinecraft("1.20.4"));
        Assert.Equal(16, FabricMetaClient.JavaMajorForMinecraft("1.17.1"));
        Assert.Equal(25, FabricMetaClient.JavaMajorForMinecraft("26.1"));
        Assert.Equal(25, FabricMetaClient.JavaMajorForMinecraft("26.2"));
    }

    [Fact]
    public async Task Http_client_uses_meta_v2_urls_and_descriptive_user_agent()
    {
        var handler = new MapHandler();
        handler.Map(FabricMetaClient.InstallerListUrl(), Read("fabric-meta-installer.json"));
        handler.Map(FabricMetaClient.LoaderForGameUrl(Mc1218), Read("fabric-meta-loader-1.21.8.json"));
        handler.Map(
            FabricMetaClient.LoaderForGameUrl("not-a-version"),
            Read("fabric-meta-loader-unknown.json"));
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var client = new FabricMetaClient(http);

        var resolved = await client.ResolveLauncherAsync(Mc1218);
        Assert.True(resolved.Succeeded, resolved.Error);
        Assert.Equal("0.17.2", resolved.Value!.LoaderVersion);
        Assert.Equal("1.1.0", resolved.Value.InstallerVersion);

        var missing = await client.ResolveLauncherAsync("not-a-version");
        Assert.False(missing.Succeeded);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.Equal("meta.fabricmc.net", r.RequestUri!.Host);
            Assert.StartsWith("/v2/versions/", r.RequestUri.AbsolutePath, StringComparison.Ordinal);
            Assert.Contains("MCSTool/", r.UserAgent, StringComparison.Ordinal);
            Assert.Contains("github.com/maattox/MCSTool", r.UserAgent, StringComparison.Ordinal);
        });
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
