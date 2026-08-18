using System.IO.Compression;
using System.Text;
using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MrpackAnalyzerTests
{
    [Fact]
    public void Tracked_fixture_mrpack_returns_confirmable_summary()
    {
        var path = FixturePath("fabric-strip.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var result = MrpackAnalyzer.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        var a = result.Value!;

        Assert.Equal("CI Fabric Strip Fixture", a.PackName);
        Assert.Equal("0.0.1-fixture", a.VersionId);
        Assert.Equal("1.21.1", a.MinecraftVersion);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, a.Loader);
        Assert.Equal("0.16.9", a.LoaderVersion);
        Assert.Equal(21, a.JavaMajor);
        Assert.Equal(4, a.FileCount);
        Assert.Equal(1, a.ServerRequiredCount);
        Assert.Equal(1, a.ServerOptionalCount);
        Assert.Equal(2, a.ServerSideCount);
        Assert.Equal(1, a.ClientOnlyCount);
        Assert.Equal(1, a.UnclearSideCount);
        Assert.Contains("mods/server-required.jar", a.ServerSidePaths);
        Assert.Contains("mods/server-optional.jar", a.ServerSidePaths);
        Assert.Equal(["mods/client-only.jar"], a.ClientOnlyPaths);
        Assert.Equal(["mods/unclear-side.jar"], a.UnclearSidePaths);
        Assert.True(a.HasOverrides);
        Assert.False(a.HasServerOverrides);
        Assert.False(a.HasClientOverrides);
        Assert.Contains("Required Java: 21", a.ConfirmableSummary, StringComparison.Ordinal);
        Assert.Contains("Client-only (not installed on the server): 1", a.ConfirmableSummary, StringComparison.Ordinal);
        Assert.Contains("mods/unclear-side.jar", a.ConfirmableSummary, StringComparison.Ordinal);
        Assert.Contains("unclear", a.Warnings[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("api.modrinth.com", a.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Index_json_detects_neoforge_and_quilt_loaders()
    {
        var neo = MrpackAnalyzer.AnalyzeIndexJson(IndexJson("neoforge", "21.1.98", "1.21.1"));
        Assert.True(neo.Succeeded, neo.Error);
        Assert.Equal(MrpackAnalyzer.LoaderNeoForge, neo.Value!.Loader);
        Assert.Equal("21.1.98", neo.Value.LoaderVersion);
        Assert.Equal(21, neo.Value.JavaMajor);

        var quilt = MrpackAnalyzer.AnalyzeIndexJson(IndexJson("quilt-loader", "0.26.0", "1.21.1"));
        Assert.True(quilt.Succeeded, quilt.Error);
        Assert.Equal(MrpackAnalyzer.LoaderQuilt, quilt.Value!.Loader);
        Assert.Equal("0.26.0", quilt.Value.LoaderVersion);

        var forge = MrpackAnalyzer.AnalyzeIndexJson(IndexJson("forge", "14.23.5.2854", "1.12.2"));
        Assert.True(forge.Succeeded, forge.Error);
        Assert.Equal(MrpackAnalyzer.LoaderForge, forge.Value!.Loader);
        Assert.Equal(8, forge.Value.JavaMajor);
    }

    [Fact]
    public void Java_floor_matches_blueprint_table()
    {
        Assert.True(MinecraftJavaFloor.TryGet("1.12.2", out var j8));
        Assert.Equal(8, j8);
        Assert.True(MinecraftJavaFloor.TryGet("1.16.5", out j8));
        Assert.Equal(8, j8);
        Assert.True(MinecraftJavaFloor.TryGet("1.17.1", out var j16));
        Assert.Equal(16, j16);
        Assert.True(MinecraftJavaFloor.TryGet("1.20.1", out var j17));
        Assert.Equal(17, j17);
        Assert.True(MinecraftJavaFloor.TryGet("1.20.4", out j17));
        Assert.Equal(17, j17);
        Assert.True(MinecraftJavaFloor.TryGet("1.20.5", out var j21));
        Assert.Equal(21, j21);
        Assert.True(MinecraftJavaFloor.TryGet("1.21.1", out j21));
        Assert.Equal(21, j21);
        Assert.True(MinecraftJavaFloor.TryGet("26.1", out var j25));
        Assert.Equal(25, j25);
        Assert.False(MinecraftJavaFloor.TryGet("not-a-version", out _));
        Assert.False(MinecraftJavaFloor.TryGet("", out _));
    }

    [Fact]
    public void Rejects_missing_index_wrong_game_and_two_loaders()
    {
        using var empty = MakeZip(("readme.txt", "no index"));
        var missing = MrpackAnalyzer.AnalyzeZip(empty, "empty.mrpack");
        Assert.False(missing.Succeeded);
        Assert.Contains("modrinth.index.json", missing.Error, StringComparison.Ordinal);

        using var nested = MakeZip(("subdir/modrinth.index.json", IndexJson("fabric-loader", "0.16.9", "1.21.1")));
        var nestedResult = MrpackAnalyzer.AnalyzeZip(nested, "nested.mrpack");
        Assert.False(nestedResult.Succeeded);
        Assert.Contains("root", nestedResult.Error, StringComparison.OrdinalIgnoreCase);

        var notZip = MrpackAnalyzer.AnalyzeZip(new MemoryStream("not a zip"u8.ToArray()), "plain.txt");
        Assert.False(notZip.Succeeded);
        Assert.Contains("ZIP", notZip.Error, StringComparison.Ordinal);

        var wrongGame = MrpackAnalyzer.AnalyzeIndexJson("""
            {"formatVersion":1,"game":"minetest","name":"x","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.9"},"files":[]}
            """);
        Assert.False(wrongGame.Succeeded);
        Assert.Contains("minecraft", wrongGame.Error, StringComparison.OrdinalIgnoreCase);

        var twoLoaders = MrpackAnalyzer.AnalyzeIndexJson("""
            {
              "formatVersion": 1,
              "game": "minecraft",
              "name": "x",
              "dependencies": {
                "minecraft": "1.21.1",
                "fabric-loader": "0.16.9",
                "neoforge": "21.1.1"
              },
              "files": []
            }
            """);
        Assert.False(twoLoaders.Succeeded);
        Assert.Contains("more than one loader", twoLoaders.Error, StringComparison.OrdinalIgnoreCase);

        var noLoader = MrpackAnalyzer.AnalyzeIndexJson("""
            {"formatVersion":1,"game":"minecraft","name":"x","dependencies":{"minecraft":"1.21.1"},"files":[]}
            """);
        Assert.False(noLoader.Succeeded);
        Assert.Contains("no recognized loader", noLoader.Error, StringComparison.OrdinalIgnoreCase);

        var badFormat = MrpackAnalyzer.AnalyzeIndexJson("""
            {"formatVersion":2,"game":"minecraft","name":"x","dependencies":{"minecraft":"1.21.1","fabric-loader":"0.16.9"},"files":[]}
            """);
        Assert.False(badFormat.Succeeded);
        Assert.Contains("formatVersion", badFormat.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_env_server_is_unclear_and_missing_downloads_warn()
    {
        var json = """
            {
              "formatVersion": 1,
              "game": "minecraft",
              "name": "Warn Pack",
              "dependencies": { "minecraft": "1.21.1", "fabric-loader": "0.16.9" },
              "files": [
                { "path": "mods/weird.jar", "env": { "server": "maybe" }, "downloads": ["https://example.invalid/weird.jar"] },
                { "path": "mods/nodl.jar", "env": { "server": "required" } }
              ]
            }
            """;
        var result = MrpackAnalyzer.AnalyzeIndexJson(json);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(["mods/weird.jar"], result.Value!.UnclearSidePaths);
        Assert.Equal(["mods/nodl.jar"], result.Value.ServerSidePaths);
        Assert.Contains(result.Value.Warnings, w => w.Contains("unknown env.server", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Value.Warnings, w => w.Contains("no download URL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Homemade_fabric_strip_sample_tags_sodium_client_only_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleZip = Path.Combine(repoRoot, "data", "sample-packs", "homemade", "fabric-strip.mrpack");
        var sampleIndex = Path.Combine(repoRoot, "data", "sample-packs", "homemade", "fabric-strip-src", "modrinth.index.json");
        ServiceResult<MrpackAnalysis> result;
        if (File.Exists(sampleZip))
            result = MrpackAnalyzer.AnalyzeFile(sampleZip);
        else if (File.Exists(sampleIndex))
            result = MrpackAnalyzer.AnalyzeIndexJson(File.ReadAllText(sampleIndex));
        else
            return;
        Assert.True(result.Succeeded, result.Error);
        var a = result.Value!;
        Assert.Equal(MrpackAnalyzer.LoaderFabric, a.Loader);
        Assert.Equal("1.21.1", a.MinecraftVersion);
        Assert.Equal(21, a.JavaMajor);
        Assert.Equal(2, a.ServerRequiredCount);
        Assert.Equal(0, a.ServerOptionalCount);
        Assert.Equal(1, a.ClientOnlyCount);
        Assert.Equal(0, a.UnclearSideCount);
        Assert.Contains(a.ClientOnlyPaths, p => p.Contains("sodium", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(a.ServerSidePaths, p => p.Contains("sodium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(a.ServerSidePaths, p => p.Contains("lithium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(a.ServerSidePaths, p => p.Contains("fabric-api", StringComparison.OrdinalIgnoreCase));
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", fileName);

    private static string IndexJson(string loaderKey, string loaderVersion, string minecraft) =>
        $$"""
        {
          "formatVersion": 1,
          "game": "minecraft",
          "name": "Loader detect",
          "dependencies": {
            "minecraft": "{{minecraft}}",
            "{{loaderKey}}": "{{loaderVersion}}"
          },
          "files": []
        }
        """;

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
}
