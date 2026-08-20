using System.IO.Compression;
using System.Text;
using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupPackImportTests
{
    [Fact]
    public void Tracked_jar_root_fixture_can_continue_when_present()
    {
        var path = FixturePath("jar-root.zip");
        if (!File.Exists(path))
            return;

        var result = SetupPackImport.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.Equal(SetupPackImport.KindManualZip, preview.Kind);
        Assert.True(preview.CanContinue);
        Assert.Null(preview.BlockReason);
        Assert.NotEqual(SetupPackImport.UnclearSideRefusal, preview.BlockReason);
        if (preview.UnclearSideCount > 0)
        {
            Assert.Contains(SetupPackImport.UnclearSideKeepCopy, preview.ConfirmableSummary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Manual_zip_with_unclear_side_jars_can_continue()
    {
        var serverJar = MakeJar(
            ("fabric.mod.json",
                """{"schemaVersion":1,"id":"content","version":"0","environment":"*","depends":{"minecraft":"1.21.1"}}"""));
        using var zip = MakeZipBytes(
            ("mods/content.jar", serverJar),
            ("mods/mystery-side.jar", Encoding.UTF8.GetBytes("not-a-valid-jar")));
        var path = WriteTemp("unclear-manual.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            var preview = result.Value!;
            Assert.Equal(SetupPackImport.KindManualZip, preview.Kind);
            Assert.True(preview.CanContinue);
            Assert.Null(preview.BlockReason);
            Assert.True(preview.UnclearSideCount > 0);
            Assert.Contains(SetupPackImport.UnclearSideKeepCopy, preview.ConfirmableSummary, StringComparison.Ordinal);
            Assert.Contains("mystery-side.jar", preview.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SetupPackImport.UnclearSideRefusal, preview.ConfirmableSummary, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Unclear_side_warning_caps_examples_and_stays_novice()
    {
        var paths = Enumerable.Range(1, 10).Select(i => $"mods/unclear-{i}.jar").ToList();
        var text = SetupPackImport.FormatUnclearSideWarning(10, paths);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(SetupPackImport.UnclearSideKeepCopy, text, StringComparison.Ordinal);
        Assert.Contains("unclear-1.jar", text, StringComparison.Ordinal);
        Assert.Contains("unclear-6.jar", text, StringComparison.Ordinal);
        Assert.DoesNotContain("unclear-7.jar", text, StringComparison.Ordinal);
        Assert.Contains("and 4 more", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VM1", text, StringComparison.Ordinal);
        Assert.Null(SetupPackImport.FormatUnclearSideWarning(0, paths));
    }

    [Fact]
    public void Tracked_mrpack_fixture_blocks_on_unclear_side()
    {
        var path = FixturePath("fabric-strip.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var result = SetupPackImport.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.Equal(SetupPackImport.KindMrpack, preview.Kind);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, preview.Loader);
        Assert.False(preview.CanContinue);
        Assert.Equal(SetupPackImport.UnclearSideRefusal, preview.BlockReason);
        Assert.Equal(0, preview.OverrideListSkipCount);
        Assert.Null(preview.OverrideListWarning);
        Assert.DoesNotContain(
            SetupPackImport.OverrideListMisdeclarationCopy,
            preview.ConfirmableSummary,
            StringComparison.Ordinal);
        Assert.DoesNotContain("modrinth.com/search", preview.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Tracked_mistag_mrpack_can_continue()
    {
        var path = FixturePath("fabric-mistag.mrpack");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var result = SetupPackImport.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.Equal(SetupPackImport.KindMrpack, preview.Kind);
        Assert.True(preview.CanContinue);
        Assert.Null(preview.BlockReason);
        Assert.Equal(1, preview.ClientOnlyCount);
        Assert.Equal(0, preview.UnclearSideCount);
        Assert.Equal(1, preview.OverrideListSkipCount);
        Assert.Contains("sodium-fabric-mistag.jar", preview.OverrideListSkipPaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.False(string.IsNullOrWhiteSpace(preview.OverrideListWarning));
        Assert.Contains(SetupPackImport.OverrideListMisdeclarationCopy, preview.OverrideListWarning, StringComparison.Ordinal);
        Assert.Contains("sodium-fabric-mistag.jar", preview.OverrideListWarning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(SetupPackImport.OverrideListMisdeclarationCopy, preview.ConfirmableSummary, StringComparison.Ordinal);
        Assert.Contains("Override list: 1", preview.ConfirmableSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void Tracked_manual_zip_is_second_adapter()
    {
        var path = FixturePath("manual-server.zip");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var result = SetupPackImport.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(SetupPackImport.KindManualZip, result.Value!.Kind);
        Assert.DoesNotContain("curseforge.com/search", result.Value.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Zip_with_modrinth_index_routes_to_mrpack_adapter()
    {
        using var zip = MakeZip(
            ("modrinth.index.json", IndexJson("fabric-loader", "0.16.9", "1.21.1")));
        var path = WriteTemp("actually-an-mrpack.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(SetupPackImport.KindMrpack, result.Value!.Kind);
            Assert.Equal(MrpackAnalyzer.LoaderFabric, result.Value.Loader);
            Assert.True(result.Value.CanContinue);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Quilt_is_detected_but_not_installable()
    {
        var analyzed = MrpackAnalyzer.AnalyzeIndexJson(IndexJson("quilt-loader", "0.26.0", "1.21.1"));
        Assert.True(analyzed.Succeeded, analyzed.Error);
        var preview = SetupPackImport.FromMrpack(analyzed.Value!, "quilt.mrpack");
        Assert.False(preview.CanContinue);
        Assert.Equal(SetupPackImport.QuiltRefusal, preview.BlockReason);
    }

    [Fact]
    public void Fabric_index_with_no_unclear_files_can_continue()
    {
        var analyzed = MrpackAnalyzer.AnalyzeIndexJson(IndexJson("fabric-loader", "0.16.9", "1.21.1"));
        Assert.True(analyzed.Succeeded, analyzed.Error);
        var preview = SetupPackImport.FromMrpack(analyzed.Value!, "ok.mrpack");
        Assert.True(preview.CanContinue);
        Assert.Null(preview.BlockReason);
        Assert.Equal("1.21.1", preview.MinecraftVersion);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, preview.Loader);
    }

    [Fact]
    public void Incomplete_curseforge_zip_cannot_continue()
    {
        using var zip = MakeZip(
            ("manifest.json", CfManifestJson()),
            ("libraries/net/neoforged/example.jar", "lib"),
            ("neoforge-21.1.0-installer.jar", "installer"));
        var path = WriteTemp("cf-incomplete.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            var preview = result.Value!;
            Assert.Equal(SetupPackImport.KindManualZip, preview.Kind);
            Assert.False(preview.CanContinue);
            Assert.Equal(ManualServerPackAnalyzer.CurseForgeIncompleteRefusal, preview.BlockReason);
            Assert.Equal(0, preview.OverrideListSkipCount);
            Assert.Null(preview.OverrideListWarning);
            Assert.Contains("Server Files", preview.BlockReason, StringComparison.Ordinal);
            Assert.DoesNotContain("api.curseforge.com", preview.BlockReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Mixed_curseforge_cannot_continue()
    {
        using var zip = MakeZip(
            ("manifest.json", CfManifestJsonTwoFiles()),
            ("mods/only-one.jar", "mod"));
        var path = WriteTemp("cf-mixed.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            Assert.False(result.Value!.CanContinue);
            Assert.Equal(ManualServerPackAnalyzer.CurseForgeMixedRefusal, result.Value.BlockReason);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Complete_curseforge_server_files_and_mrpack_can_continue()
    {
        var serverJar = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"content","version":"0","environment":"*"}"""));
        using var cf = MakeZipBytes(
            ("manifest.json", Encoding.UTF8.GetBytes(CfManifestJson())),
            ("libraries/net/neoforged/example.jar", Encoding.UTF8.GetBytes("lib")),
            ("mods/content.jar", serverJar),
            ("run.sh", Encoding.UTF8.GetBytes("#!/bin/sh")));
        var cfPath = WriteTemp("cf-server.zip", cf);
        try
        {
            var result = SetupPackImport.AnalyzeFile(cfPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(result.Value!.CanContinue);
            Assert.Null(result.Value.BlockReason);
            Assert.Equal(MrpackAnalyzer.LoaderNeoForge, result.Value.Loader);
        }
        finally
        {
            TryDelete(cfPath);
        }

        using var mrpack = MakeZip(
            ("modrinth.index.json", IndexJson("fabric-loader", "0.16.9", "1.21.1")));
        var mrPath = WriteTemp("ok.mrpack", mrpack);
        try
        {
            var result = SetupPackImport.AnalyzeFile(mrPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(SetupPackImport.KindMrpack, result.Value!.Kind);
            Assert.True(result.Value.CanContinue);
        }
        finally
        {
            TryDelete(mrPath);
        }
    }

    [Fact]
    public void Distribution_vanilla_paper_or_loader_from_wizard()
    {
        var vanilla = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            VanillaFlavor = SetupVanillaFlavor.Default,
        };
        var paper = new SetupWizardState
        {
            ServerType = SetupServerType.Vanilla,
            VanillaFlavor = SetupVanillaFlavor.Optimized,
        };
        var modded = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            PackLoader = MrpackAnalyzer.LoaderNeoForge,
            PackLoaderVersion = "21.1.98",
            MinecraftVersion = "1.21.1",
            PackName = "Example Pack",
            PackConfirmed = true,
        };

        Assert.Equal("vanilla", SetupPackImport.ToDistribution(vanilla));
        Assert.Equal("paper", SetupPackImport.ToDistribution(paper));
        Assert.Equal("neoforge", SetupPackImport.ToDistribution(modded));
        Assert.True(SetupPackImport.IsOnboxDistribution("fabric"));
        Assert.False(SetupPackImport.IsOnboxDistribution("quilt"));

        var pin = SetupPackImport.LoaderPin(modded.PackLoader, modded.PackLoaderVersion);
        Assert.Equal("NEOFORGE_VERSION", pin!.Value.Name);
        Assert.Equal("21.1.98", pin.Value.Value);
    }

    [Fact]
    public void Plan_summary_names_modded_pack_not_a_catalog()
    {
        var state = new SetupWizardState
        {
            ServerType = SetupServerType.Modded,
            MinecraftVersion = "1.21.1",
            PackName = "CI Fabric Strip Fixture",
            PackLoader = "fabric",
            PackLoaderVersion = "0.16.9",
            PackConfirmed = true,
            EulaAccepted = true,
        };

        var text = InfraPlanSummary.Build(state);
        Assert.Contains("Modded — CI Fabric Strip Fixture (fabric 0.16.9) 1.21.1", text, StringComparison.Ordinal);
        Assert.Contains("same exported pack required to join", text, StringComparison.Ordinal);
        Assert.DoesNotContain("search", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0.0.0.0/0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Client_pack_copy_is_dedicated_and_novice()
    {
        Assert.Contains("not playable", SetupPackImport.ClientPackCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot rebuild a client pack", SetupPackImport.ClientPackCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mods folder on the server", SetupPackImport.ClientPackCopy, StringComparison.Ordinal);
        Assert.DoesNotContain("VM1", SetupPackImport.ClientPackCopy, StringComparison.Ordinal);
        Assert.DoesNotContain("catalog", SetupPackImport.ClientPackCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot join until they have it", SetupPackImport.ClientPackAckLabel, StringComparison.OrdinalIgnoreCase);

        var line = SetupPackImport.FriendsNeedLine("CI Fabric Strip Fixture", "1.21.1", "fabric", "0.16.9");
        Assert.Contains("CI Fabric Strip Fixture", line, StringComparison.Ordinal);
        Assert.Contains("Minecraft 1.21.1", line, StringComparison.Ordinal);
        Assert.Contains("Fabric 0.16.9", line, StringComparison.Ordinal);
        Assert.Contains("same file you uploaded", line, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("modrinth.com/search", line, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Override_list_warning_caps_examples_and_stays_novice()
    {
        var paths = Enumerable.Range(1, 10).Select(i => $"mods/client-mod-{i}.jar").ToList();
        var text = SetupPackImport.FormatOverrideListWarning(10, paths);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains(SetupPackImport.OverrideListMisdeclarationCopy, text, StringComparison.Ordinal);
        Assert.Contains("client-mod-1.jar", text, StringComparison.Ordinal);
        Assert.Contains("client-mod-6.jar", text, StringComparison.Ordinal);
        Assert.DoesNotContain("client-mod-7.jar", text, StringComparison.Ordinal);
        Assert.Contains("and 4 more", text, StringComparison.Ordinal);
        Assert.DoesNotContain("VM1", text, StringComparison.Ordinal);
        Assert.Null(SetupPackImport.FormatOverrideListWarning(0, paths));
    }

    [Fact]
    public void Analyze_with_failed_refresh_still_uses_embedded_list()
    {
        var handler = new FailHandler();
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var refresh = new ExcludeIncludeListRefresh(http);
        var path = FixturePath("fabric-mistag.mrpack");
        var result = SetupPackImport.AnalyzeFile(path, refresh);
        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.True(preview.CanContinue);
        Assert.Equal(1, preview.OverrideListSkipCount);
        Assert.False(string.IsNullOrWhiteSpace(preview.OverrideListWarning));
        Assert.False(refresh.UsedRemote(ExcludeIncludeListRefresh.ModrinthRawUrl));
    }

    [Fact]
    public void Analyze_with_remote_list_can_replace_layer1()
    {
        var handler = new MapHandler();
        handler.Map(
            ExcludeIncludeListRefresh.ModrinthRawUrl,
            """{"globalExcludes":["lithium-mistag"],"globalForceIncludes":[],"modpacks":{}}""");
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
        var refresh = new ExcludeIncludeListRefresh(http);
        var path = FixturePath("fabric-mistag.mrpack");
        var result = SetupPackImport.AnalyzeFile(path, refresh);
        Assert.True(result.Succeeded, result.Error);
        var preview = result.Value!;
        Assert.True(preview.CanContinue);
        Assert.True(refresh.UsedRemote(ExcludeIncludeListRefresh.ModrinthRawUrl));
        Assert.Equal(1, preview.OverrideListSkipCount);
        Assert.Contains("lithium-mistag", preview.OverrideListSkipPaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sodium", preview.OverrideListSkipPaths[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("lithium-mistag", preview.OverrideListWarning, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Server_type_normalizes_unknown_to_vanilla()
    {
        Assert.Equal(SetupServerType.Vanilla, SetupServerType.Normalize(null));
        Assert.Equal(SetupServerType.Vanilla, SetupServerType.Normalize("paper"));
        Assert.Equal(SetupServerType.Modded, SetupServerType.Normalize("Modded"));
        Assert.True(SetupServerType.IsVanilla(""));
        Assert.True(SetupServerType.IsModded("modded"));
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

    private static string CfManifestJson() =>
        """
        {
          "minecraft": {
            "version": "1.21.1",
            "modLoaders": [{ "id": "neoforge-21.1.0", "primary": true }]
          },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "MCMGR Synthetic CF Export",
          "version": "0.1.0",
          "files": [{ "projectID": 1, "fileID": 1, "required": true }],
          "overrides": "overrides"
        }
        """;

    private static string CfManifestJsonTwoFiles() =>
        """
        {
          "minecraft": {
            "version": "1.21.1",
            "modLoaders": [{ "id": "neoforge-21.1.0", "primary": true }]
          },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "MCMGR Synthetic CF Export",
          "version": "0.1.0",
          "files": [
            { "projectID": 1, "fileID": 1, "required": true },
            { "projectID": 2, "fileID": 2, "required": true }
          ],
          "overrides": "overrides"
        }
        """;

    private static MemoryStream MakeZip(params (string Name, string Content)[] entries) =>
        MakeZipBytes(entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content))).ToArray());

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

    private static string WriteTemp(string name, MemoryStream zip)
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-setup-pack-" + Guid.NewGuid().ToString("N") + "-" + name);
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }

    private sealed class FailHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException("github down"));
    }

    private sealed class MapHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string> _map = new(StringComparer.Ordinal);

        public void Map(string url, string body) => _map[url] = body;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var url = request.RequestUri?.ToString() ?? "";
            if (!_map.TryGetValue(url, out var body))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                {
                    Content = new StringContent($"unmapped {url}"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }
}
