using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MrpackInstallerTests
{
    [Fact]
    public async Task Installs_server_side_skips_client_only_copies_overrides_and_retains_archive()
    {
        var required = Encoding.UTF8.GetBytes("required-jar");
        var optional = Encoding.UTF8.GetBytes("optional-jar");
        var clientOnly = Encoding.UTF8.GetBytes("should-not-download");
        var requiredUrl = "https://cdn.modrinth.com/data/AAAA/versions/1/server-required.jar";
        var optionalUrl = "https://cdn.modrinth.com/data/BBBB/versions/1/server-optional.jar";
        var clientUrl = "https://cdn.modrinth.com/data/CCCC/versions/1/client-only.jar";

        var index = IndexJson(
            """
            [
              {
                "path": "mods/server-required.jar",
                "hashes": { "sha512": "HASH_REQUIRED", "sha1": "unused" },
                "env": { "client": "required", "server": "required" },
                "downloads": ["URL_REQUIRED"]
              },
              {
                "path": "mods/server-optional.jar",
                "hashes": { "sha1": "HASH_OPTIONAL" },
                "env": { "client": "optional", "server": "optional" },
                "downloads": ["URL_OPTIONAL"]
              },
              {
                "path": "mods/client-only.jar",
                "hashes": { "sha512": "HASH_CLIENT" },
                "env": { "client": "required", "server": "unsupported" },
                "downloads": ["URL_CLIENT"]
              }
            ]
            """
            .Replace("HASH_REQUIRED", Sha512Hex(required), StringComparison.Ordinal)
            .Replace("HASH_OPTIONAL", Sha1Hex(optional), StringComparison.Ordinal)
            .Replace("HASH_CLIENT", Sha512Hex(clientOnly), StringComparison.Ordinal)
            .Replace("URL_REQUIRED", requiredUrl, StringComparison.Ordinal)
            .Replace("URL_OPTIONAL", optionalUrl, StringComparison.Ordinal)
            .Replace("URL_CLIENT", clientUrl, StringComparison.Ordinal));

        using var mrpack = MakeZip(
            (MrpackAnalyzer.IndexEntryName, index),
            ("overrides/config/from-overrides.txt", "both"),
            ("server-overrides/config/from-server.txt", "server"),
            ("server-overrides/config/from-overrides.txt", "server-wins"),
            ("client-overrides/config/from-client.txt", "client-only-override"));

        var packPath = WriteTemp("pack.mrpack", mrpack);
        var dest = NewTempDir();
        var data = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            handler.Map(requiredUrl, required);
            handler.Map(optionalUrl, optional);
            handler.Map(clientUrl, clientOnly);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var installer = new MrpackInstaller(http);

            var result = await installer.InstallAsync(packPath, dest, data);
            Assert.True(result.Succeeded, result.Error);
            var value = result.Value!;

            Assert.True(File.Exists(Path.Combine(dest, "mods", "server-required.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "server-optional.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "client-only.jar")));
            Assert.Equal("server-wins", File.ReadAllText(Path.Combine(dest, "config", "from-overrides.txt")));
            Assert.Equal("server", File.ReadAllText(Path.Combine(dest, "config", "from-server.txt")));
            Assert.False(File.Exists(Path.Combine(dest, "config", "from-client.txt")));

            Assert.DoesNotContain(handler.Requests, r => r.AbsoluteUri == clientUrl);
            Assert.DoesNotContain(handler.Requests, r =>
                r.Host.Equals("api.modrinth.com", StringComparison.OrdinalIgnoreCase));
            Assert.All(handler.Requests, r =>
            {
                Assert.Equal("cdn.modrinth.com", r.Host);
                Assert.Contains("MCSTool/", handler.LastUserAgent, StringComparison.Ordinal);
            });

            Assert.NotNull(value.RetainedArchivePath);
            Assert.True(File.Exists(value.RetainedArchivePath));
            Assert.Equal(
                new FileInfo(packPath).Length,
                new FileInfo(value.RetainedArchivePath!).Length);
            Assert.True(File.Exists(Path.Combine(
                Path.GetDirectoryName(value.RetainedArchivePath!)!,
                ImportedPackArchiveStore.SidecarFileName)));
            Assert.Contains(MrpackInstallResult.ClientPackReminder, value.Summary, StringComparison.Ordinal);
            Assert.Contains("mods/client-only.jar", value.SkippedClientOnlyPaths);
            Assert.Contains("mods/server-required.jar", value.InstalledRelativePaths);
            Assert.Contains("mods/server-optional.jar", value.InstalledRelativePaths);
            Assert.Equal(["mods/client-only.jar"], value.SkippedPackDeclaredPaths);
            Assert.Empty(value.SkippedOverrideListPaths);
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
            TryDeleteDir(data);
        }
    }

    [Fact]
    public async Task Tracked_fixture_fails_loudly_on_unclear_side_without_http()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", "fabric-strip.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var handler = new BytesHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var installer = new MrpackInstaller(http);
        var dest = NewTempDir();
        try
        {
            var result = await installer.InstallAsync(path, dest, retainDataDirectory: null);
            Assert.False(result.Succeeded);
            Assert.Contains("unclear", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("mods/unclear-side.jar", result.Error, StringComparison.Ordinal);
            Assert.Contains("Do not guess", result.Error, StringComparison.Ordinal);
            Assert.Empty(handler.Requests);
            Assert.False(File.Exists(Path.Combine(dest, "mods", "server-required.jar")));
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void Refuses_path_escape_and_prefers_sha512()
    {
        var dest = NewTempDir();
        try
        {
            var escape = MrpackInstaller.ResolveUnderDest(dest, "../outside.jar");
            Assert.False(escape.Succeeded);
            Assert.Contains("unsafe", escape.Error, StringComparison.OrdinalIgnoreCase);

            var rooted = MrpackInstaller.ResolveUnderDest(dest, Path.Combine(dest, "x.jar"));
            Assert.False(rooted.Succeeded);

            var ok = MrpackInstaller.ResolveUnderDest(dest, "mods/foo.jar");
            Assert.True(ok.Succeeded, ok.Error);
            Assert.Equal(Path.GetFullPath(Path.Combine(dest, "mods", "foo.jar")), ok.Value);

            Assert.True(MrpackInstaller.TryGetPreferredHash(
                new Dictionary<string, string>
                {
                    ["sha1"] = "aaaa",
                    ["sha512"] = "bbbb",
                },
                out var alg,
                out var hex));
            Assert.Equal("sha512", alg);
            Assert.Equal("bbbb", hex);
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Hash_mismatch_does_not_leave_the_jar()
    {
        var body = Encoding.UTF8.GetBytes("hello");
        var url = "https://cdn.modrinth.com/data/X/versions/1/mod.jar";
        var index = IndexJson(
            $$"""
            [{
              "path": "mods/mod.jar",
              "hashes": { "sha512": "{{Sha512Hex(Encoding.UTF8.GetBytes("other"))}}" },
              "env": { "server": "required" },
              "downloads": ["{{url}}"]
            }]
            """);

        using var mrpack = MakeZip((MrpackAnalyzer.IndexEntryName, index));
        var packPath = WriteTemp("badhash.mrpack", mrpack);
        var dest = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            handler.Map(url, body);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var result = await new MrpackInstaller(http).InstallAsync(packPath, dest, null);
            Assert.False(result.Succeeded);
            Assert.Contains("mismatch", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(dest, "mods", "mod.jar")));
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Homemade_fabric_strip_installs_cdn_jars_and_strips_sodium_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleZip = Path.Combine(repoRoot, "data", "sample-packs", "homemade", "fabric-strip.mrpack");
        if (!File.Exists(sampleZip))
            return;

        var dest = NewTempDir();
        var data = NewTempDir();
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(MrpackInstaller.HttpTimeoutSeconds) };
            var installer = new MrpackInstaller(http);
            var result = await installer.InstallAsync(sampleZip, dest, data);
            Assert.True(result.Succeeded, result.Error);
            var mods = Path.Combine(dest, "mods");
            Assert.True(File.Exists(Path.Combine(mods, "fabric-api-0.116.15+1.21.1.jar")));
            Assert.True(File.Exists(Path.Combine(mods, "lithium-fabric-0.15.4+mc1.21.1.jar")));
            Assert.False(Directory.EnumerateFiles(mods, "*sodium*", SearchOption.TopDirectoryOnly).Any());
            Assert.Equal("hello", File.ReadAllText(Path.Combine(dest, "config", "mcmgr-sample.txt")).Trim());
            Assert.NotNull(result.Value!.RetainedArchivePath);
            Assert.True(File.Exists(result.Value.RetainedArchivePath));
            Assert.Contains(result.Value.SkippedPackDeclaredPaths, p => p.Contains("sodium", StringComparison.OrdinalIgnoreCase));
            Assert.Empty(result.Value.SkippedOverrideListPaths);
        }
        finally
        {
            TryDeleteDir(dest);
            TryDeleteDir(data);
        }
    }

    [Fact]
    public async Task Tracked_mistag_fixture_installs_embedded_lithium_skips_sodium_and_override_jar()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", "fabric-mistag.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var handler = new BytesHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var dest = NewTempDir();
        try
        {
            var result = await new MrpackInstaller(http).InstallAsync(path, dest, retainDataDirectory: null);
            Assert.True(result.Succeeded, result.Error);
            var value = result.Value!;
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "lithium-mistag.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "sodium-fabric-mistag.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "config", "ok.toml")));
            Assert.Equal(["mods/lithium-mistag.jar"], value.InstalledRelativePaths);
            Assert.Equal(["mods/sodium-fabric-mistag.jar"], value.SkippedOverrideListPaths);
            Assert.Empty(value.SkippedPackDeclaredPaths);
            Assert.Contains("config/ok.toml", value.CopiedOverridePaths);
            Assert.DoesNotContain(
                value.CopiedOverridePaths,
                p => p.Contains("sodium", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Tracked_gui_client_fixture_installs_lithium_skips_overlay_and_in_jar()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", "fabric-gui-client.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var handler = new BytesHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
        var dest = NewTempDir();
        try
        {
            var result = await new MrpackInstaller(http).InstallAsync(path, dest, retainDataDirectory: null);
            Assert.True(result.Succeeded, result.Error);
            var value = result.Value!;
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "lithium-keep.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "example-loading-screen-1.0.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "early-splash.jar")));
            Assert.Equal(["mods/lithium-keep.jar"], value.InstalledRelativePaths);
            Assert.Equal(["mods/example-loading-screen-1.0.jar"], value.SkippedOverrideListPaths);
            Assert.Equal(["mods/early-splash.jar"], value.SkippedInJarMetadataPaths);
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Overlay_and_in_jar_skip_client_gui_jars_and_keep_server_mod()
    {
        var splashJar = MakeJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"splash","version":"0","entrypoints":{"client":["com.example.Splash"]}}
            """));
        var lithiumJar = MakeJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"lithium","version":"0","environment":"*","entrypoints":{"main":["com.example.Main"]}}
            """));
        var loadingBytes = Encoding.UTF8.GetBytes("not-a-real-jar");
        var index = IndexJson(
            """
            [
              {
                "path": "mods/example-loading-screen-1.0.jar",
                "env": { "client": "required", "server": "required" },
                "downloads": []
              },
              {
                "path": "mods/early-splash.jar",
                "env": { "client": "required", "server": "required" },
                "downloads": []
              },
              {
                "path": "mods/lithium-keep.jar",
                "env": { "server": "required" },
                "downloads": []
              }
            ]
            """);

        using var mrpack = MakeZipBytes(
            (MrpackAnalyzer.IndexEntryName, Encoding.UTF8.GetBytes(index)),
            ("mods/example-loading-screen-1.0.jar", loadingBytes),
            ("mods/early-splash.jar", splashJar),
            ("mods/lithium-keep.jar", lithiumJar));
        var packPath = WriteTemp("gui-client.mrpack", mrpack);
        var dest = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var result = await new MrpackInstaller(http).InstallAsync(packPath, dest, null);
            Assert.True(result.Succeeded, result.Error);
            var value = result.Value!;
            Assert.Empty(handler.Requests);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "lithium-keep.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "example-loading-screen-1.0.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "early-splash.jar")));
            Assert.Equal(["mods/lithium-keep.jar"], value.InstalledRelativePaths);
            Assert.Equal(["mods/example-loading-screen-1.0.jar"], value.SkippedOverrideListPaths);
            Assert.Equal(["mods/early-splash.jar"], value.SkippedInJarMetadataPaths);
            Assert.Contains("In-jar metadata skipped: 1", value.Summary, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Downloaded_client_only_jar_is_stripped_after_place()
    {
        var clientJar = MakeJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"gui","version":"0","environment":"client"}
            """));
        var url = "https://cdn.modrinth.com/data/AAAA/versions/1/gui-only.jar";
        var index = IndexJson(
            """
            [{
              "path": "mods/gui-only.jar",
              "hashes": { "sha512": "HASH_GUI" },
              "env": { "server": "required" },
              "downloads": ["URL_GUI"]
            }]
            """
            .Replace("HASH_GUI", Sha512Hex(clientJar), StringComparison.Ordinal)
            .Replace("URL_GUI", url, StringComparison.Ordinal));

        using var mrpack = MakeZip((MrpackAnalyzer.IndexEntryName, index));
        var packPath = WriteTemp("cdn-gui.mrpack", mrpack);
        var dest = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            handler.Map(url, clientJar);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var result = await new MrpackInstaller(http).InstallAsync(packPath, dest, null);
            Assert.True(result.Succeeded, result.Error);
            Assert.Single(handler.Requests);
            Assert.False(File.Exists(Path.Combine(dest, "mods", "gui-only.jar")));
            Assert.Empty(result.Value!.InstalledRelativePaths);
            Assert.Equal(["mods/gui-only.jar"], result.Value.SkippedInJarMetadataPaths);
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Mixed_url_and_embedded_install_without_failing_empty_downloads()
    {
        var fromUrl = Encoding.UTF8.GetBytes("from-url");
        var fromZip = Encoding.UTF8.GetBytes("from-zip");
        var url = "https://cdn.modrinth.com/data/AAAA/versions/1/url-mod.jar";
        var index = IndexJson(
            """
            [
              {
                "path": "mods/url-mod.jar",
                "hashes": { "sha512": "HASH_URL" },
                "env": { "server": "required" },
                "downloads": ["URL_MOD"]
              },
              {
                "path": "mods/embedded-mod.jar",
                "hashes": { "sha512": "HASH_ZIP" },
                "env": { "server": "required" },
                "downloads": []
              }
            ]
            """
            .Replace("HASH_URL", Sha512Hex(fromUrl), StringComparison.Ordinal)
            .Replace("HASH_ZIP", Sha512Hex(fromZip), StringComparison.Ordinal)
            .Replace("URL_MOD", url, StringComparison.Ordinal));

        using var mrpack = MakeZipBytes(
            (MrpackAnalyzer.IndexEntryName, Encoding.UTF8.GetBytes(index)),
            ("mods/embedded-mod.jar", fromZip));
        var packPath = WriteTemp("mixed.mrpack", mrpack);
        var dest = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            handler.Map(url, fromUrl);
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var result = await new MrpackInstaller(http).InstallAsync(packPath, dest, null);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "url-mod.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "embedded-mod.jar")));
            Assert.Equal("from-zip", File.ReadAllText(Path.Combine(dest, "mods", "embedded-mod.jar")));
            Assert.Single(handler.Requests);
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public async Task Empty_downloads_without_zip_copy_fails_clearly()
    {
        var index = IndexJson(
            """
            [{
              "path": "mods/missing.jar",
              "hashes": { "sha512": "abcd" },
              "env": { "server": "required" },
              "downloads": []
            }]
            """);
        using var mrpack = MakeZip((MrpackAnalyzer.IndexEntryName, index));
        var packPath = WriteTemp("nodl.mrpack", mrpack);
        var dest = NewTempDir();
        try
        {
            var handler = new BytesHandler();
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var result = await new MrpackInstaller(http).InstallAsync(packPath, dest, null);
            Assert.False(result.Succeeded);
            Assert.Contains("no download URL", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("not in the zip", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(handler.Requests);
        }
        finally
        {
            TryDelete(packPath);
            TryDeleteDir(dest);
        }
    }

    private static string IndexJson(string filesArray) =>
        $$"""
        {
          "formatVersion": 1,
          "game": "minecraft",
          "versionId": "0.0.1-test",
          "name": "Install Strip",
          "dependencies": { "minecraft": "1.21.1", "fabric-loader": "0.16.9" },
          "files": {{filesArray}}
        }
        """;

    private static string Sha512Hex(byte[] bytes) =>
        Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();

    private static string Sha1Hex(byte[] bytes) =>
        Convert.ToHexString(SHA1.HashData(bytes)).ToLowerInvariant();

    private static MemoryStream MakeZip(params (string Name, string Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                writer.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static MemoryStream MakeZipBytes(params (string Name, byte[] Content)[] entries)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = zip.CreateEntry(name);
                using var output = entry.Open();
                output.Write(content);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static byte[] MakeJar(params (string Name, string Content)[] entries)
    {
        using var zip = MakeZip(entries);
        return zip.ToArray();
    }

    private static string WriteTemp(string fileName, MemoryStream zip)
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-mrpack-" + Guid.NewGuid().ToString("N") + "-" + fileName);
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-mrpack-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }

    private static void TryDeleteDir(string path)
    {
        try { Directory.Delete(path, recursive: true); } catch (IOException) { }
    }

    private sealed class BytesHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _map = new(StringComparer.Ordinal);

        public List<Uri> Requests { get; } = [];

        public string LastUserAgent { get; private set; } = "";

        public void Map(string url, byte[] body) => _map[url] = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastUserAgent = request.Headers.UserAgent.ToString();
            Requests.Add(request.RequestUri!);
            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
                {
                    Content = new ByteArrayContent([]),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(body),
            });
        }
    }
}
