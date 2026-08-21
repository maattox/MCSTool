using System.IO.Compression;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ManualServerPackInstallerTests
{
    [Fact]
    public void Tracked_fixture_installs_mods_and_config_and_strips_client_jar()
    {
        var path = FixturePath("manual-server.zip");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        Assert.True(analysis.Succeeded, analysis.Error);
        var a = analysis.Value!;
        Assert.True(a.CanInstall);
        Assert.Equal(ManualServerPackKind.UnstructuredServer, a.Kind);
        Assert.Contains("mods/dummy-client.jar", a.ClientOnlyPaths);
        Assert.Contains("mods/dummy-server.jar", a.ServerSidePaths);
        Assert.DoesNotContain("api.modrinth.com", a.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);

        var dest = NewTempDir();
        var data = NewTempDir();
        try
        {
            var result = ManualServerPackInstaller.Install(path, dest, data);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "dummy-server.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "dummy-client.jar")));
            Assert.Equal("enabled = true", File.ReadAllText(Path.Combine(dest, "config", "example.toml")).Trim());
            Assert.NotNull(result.Value!.RetainedArchivePath);
            Assert.True(File.Exists(result.Value.RetainedArchivePath));
            Assert.Equal(".zip", Path.GetExtension(result.Value.RetainedArchivePath));
            Assert.True(File.Exists(Path.Combine(
                Path.GetDirectoryName(result.Value.RetainedArchivePath!)!,
                ImportedPackArchiveStore.SidecarFileName)));
            Assert.Contains(MrpackInstallResult.ClientPackReminder, result.Value.Summary, StringComparison.Ordinal);
            Assert.Contains("mods/dummy-client.jar", result.Value.SkippedClientOnlyPaths);
        }
        finally
        {
            TryDeleteDir(dest);
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void Tracked_jar_root_fixture_installs_into_mods_and_skips_exclude_list()
    {
        var path = FixturePath("jar-root.zip");
        Assert.True(File.Exists(path), $"Fixture missing at {path}");

        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        Assert.True(analysis.Succeeded, analysis.Error);
        var a = analysis.Value!;
        Assert.True(a.CanInstall);
        Assert.Equal(ManualServerPackKind.UnstructuredServer, a.Kind);
        Assert.True(a.MapRootJarsToMods);
        Assert.Contains("dummy-server.jar", a.ServerSidePaths);
        Assert.Contains("embeddium-dummy.jar", a.OverrideListSkipPaths);
        Assert.Contains("dummy-client.jar", a.InJarMetadataSkipPaths);
        Assert.Equal(MrpackAnalyzer.LoaderForge, a.Loader);
        Assert.Equal("1.20.1", a.MinecraftVersion);
        Assert.Equal(17, a.JavaMajor);
        Assert.Contains("Override list: 1", a.ConfirmableSummary, StringComparison.Ordinal);
        Assert.DoesNotContain("api.curseforge.com", a.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);

        var dest = NewTempDir();
        try
        {
            var result = ManualServerPackInstaller.Install(path, dest, retainDataDirectory: null);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "dummy-server.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "embeddium-dummy.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "dummy-client.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "dummy-server.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "embeddium-dummy.jar")));
            Assert.Contains("embeddium-dummy.jar", result.Value!.SkippedClientOnlyPaths);
            Assert.Contains("dummy-client.jar", result.Value.SkippedClientOnlyPaths);
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void CurseForge_zip_with_jars_applies_exclude_list()
    {
        var keep = Encoding.UTF8.GetBytes("keep-mod");
        using var zip = MakeZipBytes(
            ("manifest.json", Encoding.UTF8.GetBytes(CfManifestJson(includeFiles: true, extraFile: true))),
            ("libraries/net/minecraftforge/example.jar", Encoding.UTF8.GetBytes("lib")),
            ("mods/content.jar", keep),
            ("mods/embeddium-1.20.1.jar", Encoding.UTF8.GetBytes("client-mod")),
            ("run.sh", Encoding.UTF8.GetBytes("#!/bin/sh")));
        var path = WriteTemp("cf-with-jars.zip", zip);
        var dest = NewTempDir();
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            var a = analysis.Value!;
            Assert.True(a.CanInstall);
            Assert.Equal(ManualServerPackKind.CurseForgeServerFiles, a.Kind);
            Assert.Contains("mods/content.jar", a.ServerSidePaths);
            Assert.Contains("mods/embeddium-1.20.1.jar", a.OverrideListSkipPaths);
            Assert.Equal(1, a.OverrideListSkipCount);

            var result = ManualServerPackInstaller.Install(path, dest, null);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "content.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "embeddium-1.20.1.jar")));
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void Mixed_curseforge_jars_and_id_only_files_are_refused()
    {
        using var zip = MakeZip(
            ("manifest.json", CfManifestJson(includeFiles: true, extraFile: true)),
            ("mods/only-one.jar", "mod"),
            ("libraries/net/minecraftforge/example.jar", "lib"));
        var path = WriteTemp("cf-mixed.zip", zip);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            Assert.False(result.Value!.CanInstall);
            Assert.Equal(ManualServerPackKind.CurseForgeClientExport, result.Value.Kind);
            Assert.Equal(ManualServerPackAnalyzer.CurseForgeMixedRefusal, result.Value.RefusalReason);
            Assert.Contains("1:1", string.Join(" ", result.Value.Warnings), StringComparison.Ordinal);
            Assert.DoesNotContain("api.curseforge.com", result.Value.RefusalReason, StringComparison.OrdinalIgnoreCase);

            var dest = NewTempDir();
            try
            {
                var install = ManualServerPackInstaller.Install(path, dest, null);
                Assert.False(install.Succeeded);
                Assert.Contains("not in the archive", install.Error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                TryDeleteDir(dest);
            }
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Force_include_keeps_in_jar_client()
    {
        var lists = ExcludeIncludeLists.Parse("""
            {
              "globalExcludes": [],
              "globalForceIncludes": ["iris"],
              "modpacks": {}
            }
            """);
        var matcher = new ExcludeIncludeMatcher(lists);
        var clientJar = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"iris","version":"0","environment":"client"}"""));
        using var zip = MakeZipBytes(("mods/iris.jar", clientJar));
        var path = WriteTemp("force-keep.zip", zip);
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path, matcher);
            Assert.True(analysis.Succeeded, analysis.Error);
            Assert.True(analysis.Value!.CanInstall);
            Assert.Contains("mods/iris.jar", analysis.Value.ServerSidePaths);
            Assert.Contains("mods/iris.jar", analysis.Value.ForceIncludedPaths);
            Assert.Empty(analysis.Value.InJarMetadataSkipPaths);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Refuses_mrpack_client_pack_and_curseforge_client_export()
    {
        using var mrpack = MakeZip((MrpackAnalyzer.IndexEntryName, "{\"formatVersion\":1}"));
        var mrpackPath = WriteTemp("looks.mrpack", mrpack);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(mrpackPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(ManualServerPackKind.Mrpack, result.Value!.Kind);
            Assert.False(result.Value.CanInstall);
            Assert.Contains("modrinth.index.json", result.Value.RefusalReason, StringComparison.Ordinal);
            var dest = NewTempDir();
            try
            {
                var install = ManualServerPackInstaller.Install(mrpackPath, dest, null);
                Assert.False(install.Succeeded);
                Assert.Contains("modrinth.index.json", install.Error, StringComparison.Ordinal);
            }
            finally
            {
                TryDeleteDir(dest);
            }
        }
        finally
        {
            TryDelete(mrpackPath);
        }

        using var client = MakeZip(
            ("options.txt", "fov:1"),
            ("shaderpacks/pack.zip", "x"),
            ("mods/iris.jar", "dummy"));
        var clientPath = WriteTemp("client-instance.zip", client);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(clientPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(ManualServerPackKind.ClientPack, result.Value!.Kind);
            Assert.False(result.Value.CanInstall);
            Assert.Contains("client pack", result.Value.RefusalReason, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("server-pack download", result.Value.RefusalReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(clientPath);
        }

        using var cf = MakeZip(
            ("manifest.json", CfManifestJson(includeFiles: true)),
            ("overrides/config/hello.txt", "hi"));
        var cfPath = WriteTemp("cf-client.zip", cf);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(cfPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(ManualServerPackKind.CurseForgeClientExport, result.Value!.Kind);
            Assert.False(result.Value.CanInstall);
            Assert.Contains("CurseForge client export", result.Value.RefusalReason, StringComparison.Ordinal);
            Assert.Contains("1:1", string.Join(" ", result.Value.Warnings), StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(cfPath);
        }
    }

    [Fact]
    public void Refuses_curseforge_zip_with_libraries_or_installer_but_no_mod_jars()
    {
        using var libs = MakeZip(
            ("manifest.json", CfManifestJson(includeFiles: true)),
            ("libraries/net/neoforged/example.jar", "lib"),
            ("run.sh", "#!/bin/sh"));
        var libsPath = WriteTemp("cf-libs-only.zip", libs);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(libsPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.Equal(ManualServerPackKind.CurseForgeClientExport, result.Value!.Kind);
            Assert.False(result.Value.CanInstall);
            Assert.Equal(ManualServerPackAnalyzer.CurseForgeIncompleteRefusal, result.Value.RefusalReason);
            Assert.Contains("Server Files", result.Value.RefusalReason, StringComparison.Ordinal);
            Assert.DoesNotContain("api.curseforge.com", result.Value.RefusalReason, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(libsPath);
        }

        using var installer = MakeZip(
            ("manifest.json", CfManifestJson(includeFiles: true)),
            ("neoforge-21.1.0-installer.jar", "installer"));
        var installerPath = WriteTemp("cf-installer-only.zip", installer);
        try
        {
            var result = ManualServerPackAnalyzer.AnalyzeFile(installerPath);
            Assert.True(result.Succeeded, result.Error);
            Assert.False(result.Value!.CanInstall);
            Assert.Equal(ManualServerPackAnalyzer.CurseForgeIncompleteRefusal, result.Value.RefusalReason);
        }
        finally
        {
            TryDelete(installerPath);
        }
    }

    [Fact]
    public void Installs_unstructured_zip_strips_fabric_client_jar_flattens_overrides_and_keeps_wrapper()
    {
        var serverJar = Encoding.UTF8.GetBytes("server-bytes");
        var clientJar = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"iris","version":"0","environment":"client"}"""));
        var bothJar = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"lithium","version":"0","environment":"*"}"""));

        using var zip = MakeZipBytes(
            ("Pack Name/mods/server.jar", serverJar),
            ("Pack Name/mods/iris.jar", clientJar),
            ("Pack Name/mods/lithium.jar", bothJar),
            ("Pack Name/config/settings.toml", Encoding.UTF8.GetBytes("a = 1")),
            ("Pack Name/overrides/config/from-overrides.txt", Encoding.UTF8.GetBytes("ov")),
            ("Pack Name/shaderpacks/fancy.zip", Encoding.UTF8.GetBytes("nope")));
        var path = WriteTemp("wrapped.zip", zip);
        var dest = NewTempDir();
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            Assert.Equal(ManualServerPackKind.UnstructuredServer, analysis.Value!.Kind);
            Assert.Equal("Pack Name/", analysis.Value.WrapperPrefix);
            Assert.Contains("mods/iris.jar", analysis.Value.ClientOnlyPaths);
            Assert.Contains("mods/lithium.jar", analysis.Value.ServerSidePaths);

            var result = ManualServerPackInstaller.Install(path, dest, retainDataDirectory: null);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "server.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "lithium.jar")));
            Assert.False(File.Exists(Path.Combine(dest, "mods", "iris.jar")));
            Assert.Equal("a = 1", File.ReadAllText(Path.Combine(dest, "config", "settings.toml")));
            Assert.Equal("ov", File.ReadAllText(Path.Combine(dest, "config", "from-overrides.txt")));
            Assert.False(Directory.Exists(Path.Combine(dest, "shaderpacks")));
            Assert.False(Directory.Exists(Path.Combine(dest, "Pack Name")));
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void CurseForge_server_files_shape_installs_without_http()
    {
        using var zip = MakeZip(
            ("manifest.json", CfManifestJson(includeFiles: true, loaderId: "neoforge-21.1.0")),
            ("libraries/net/neoforged/example.jar", "lib"),
            ("mods/content.jar", "mod"),
            ("run.sh", "#!/bin/sh"));
        var path = WriteTemp("cf-server.zip", zip);
        var dest = NewTempDir();
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            Assert.Equal(ManualServerPackKind.CurseForgeServerFiles, analysis.Value!.Kind);
            Assert.True(analysis.Value.CanInstall);
            Assert.Equal(MrpackAnalyzer.LoaderNeoForge, analysis.Value.Loader);
            Assert.Equal("21.1.0", analysis.Value.LoaderVersion);
            Assert.Equal("1.21.1", analysis.Value.MinecraftVersion);
            Assert.Equal(21, analysis.Value.JavaMajor);

            var result = ManualServerPackInstaller.Install(path, dest, null);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "content.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "libraries", "net", "neoforged", "example.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "run.sh")));
            Assert.True(File.Exists(Path.Combine(dest, "manifest.json")));
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void Refuses_path_escape_and_unknown_layout()
    {
        var dest = NewTempDir();
        try
        {
            var escape = MrpackInstaller.ResolveUnderDest(dest, "mods/../outside.jar");
            Assert.False(escape.Succeeded);

            using var empty = MakeZip(("readme.txt", "hello"));
            var emptyPath = WriteTemp("readme-only.zip", empty);
            try
            {
                var analysis = ManualServerPackAnalyzer.AnalyzeFile(emptyPath);
                Assert.True(analysis.Succeeded, analysis.Error);
                Assert.Equal(ManualServerPackKind.Unknown, analysis.Value!.Kind);
                Assert.False(analysis.Value.CanInstall);
                var install = ManualServerPackInstaller.Install(emptyPath, dest, null);
                Assert.False(install.Succeeded);
            }
            finally
            {
                TryDelete(emptyPath);
            }

            using var zipEscape = MakeZipBytes(("mods/../outside.jar", "x"u8.ToArray()));
            var escapePath = WriteTemp("escape.zip", zipEscape);
            try
            {
                var install = ManualServerPackInstaller.Install(escapePath, dest, null);
                Assert.False(install.Succeeded);
                Assert.Contains("unsafe", install.Error, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                TryDelete(escapePath);
            }
        }
        finally
        {
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void Homemade_manual_server_zip_installs_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleZip = Path.Combine(repoRoot, "data", "sample-packs", "homemade", "manual-server.zip");
        if (!File.Exists(sampleZip))
            return;

        var dest = NewTempDir();
        var data = NewTempDir();
        try
        {
            var result = ManualServerPackInstaller.Install(sampleZip, dest, data);
            Assert.True(result.Succeeded, result.Error);
            Assert.True(File.Exists(Path.Combine(dest, "mods", "dummy-server-a.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "dummy-server-b.jar")));
            Assert.Equal("enabled = true", File.ReadAllText(Path.Combine(dest, "config", "example.toml")).Trim());
            Assert.NotNull(result.Value!.RetainedArchivePath);
        }
        finally
        {
            TryDeleteDir(dest);
            TryDeleteDir(data);
        }
    }

    [Fact]
    public void Homemade_curseforge_synthetic_is_refused_as_client_export_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleZip = Path.Combine(repoRoot, "data", "sample-packs", "homemade", "curseforge-synthetic.zip");
        if (!File.Exists(sampleZip))
            return;

        var result = ManualServerPackAnalyzer.AnalyzeFile(sampleZip);
        Assert.True(result.Succeeded, result.Error);
        Assert.Equal(ManualServerPackKind.CurseForgeClientExport, result.Value!.Kind);
        Assert.False(result.Value.CanInstall);
        Assert.Contains("Listed CurseForge file IDs", string.Join(" ", result.Value.Warnings), StringComparison.Ordinal);
    }

    [Fact]
    public void Unstructured_zip_strips_clientSideOnly_keeps_server_toml_and_unclear()
    {
        var clientJar = MakeJar(("META-INF/mods.toml", """
            [[mods]]
            modId="fancyui"
            clientSideOnly=true
            """));
        var serverJar = MakeJar(("META-INF/mods.toml", """
            [[mods]]
            modId="apisupport"
            side="SERVER"
            """));
        var unclearJar = Encoding.UTF8.GetBytes("not-a-jar");
        using var zip = MakeZipBytes(
            ("mods/fancyui.jar", clientJar),
            ("mods/apisupport.jar", serverJar),
            ("mods/mystery.jar", unclearJar));
        var path = WriteTemp("in-jar-side.zip", zip);
        var dest = NewTempDir();
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            var a = analysis.Value!;
            Assert.True(a.CanInstall);
            Assert.Contains("mods/fancyui.jar", a.InJarMetadataSkipPaths);
            Assert.Contains("mods/apisupport.jar", a.ServerSidePaths);
            Assert.DoesNotContain("mods/apisupport.jar", a.ClientOnlyPaths);
            Assert.DoesNotContain("mods/apisupport.jar", a.UnclearSidePaths);
            Assert.Contains("mods/mystery.jar", a.UnclearSidePaths);
            Assert.Contains("mods/mystery.jar", a.ServerSidePaths);
            Assert.DoesNotContain("holdmyitems", a.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);

            var result = ManualServerPackInstaller.Install(path, dest, null);
            Assert.True(result.Succeeded, result.Error);
            Assert.False(File.Exists(Path.Combine(dest, "mods", "fancyui.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "apisupport.jar")));
            Assert.True(File.Exists(Path.Combine(dest, "mods", "mystery.jar")));
        }
        finally
        {
            TryDelete(path);
            TryDeleteDir(dest);
        }
    }

    [Fact]
    public void Unstructured_zip_strips_common_client_mixin_and_keeps_client_gated_mixin()
    {
        var killer = MakeJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":["HeldItemMixin"]}"""),
            ("example.refmap.json", """{"mappings":{"com/example/mixin/HeldItemMixin":{"net/minecraft/client/renderer/ItemInHandRenderer":"Lnet/minecraft/client/renderer/ItemInHandRenderer;"}}}"""));
        var gated = MakeJar(
            ("example.mixins.json", """{"package":"com.example.mixin","mixins":[],"client":["GuiMixin"]}"""),
            ("example.refmap.json", """{"mappings":{"com/example/mixin/GuiMixin":{"net/minecraft/client/gui/screens/Screen":"Lnet/minecraft/client/gui/screens/Screen;"}}}"""));
        using var zip = MakeZipBytes(
            ("mods/client-mixin-killer.jar", killer),
            ("mods/client-gated-mixin.jar", gated));
        var path = WriteTemp("mixin-side.zip", zip);
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            var a = analysis.Value!;
            Assert.Contains("mods/client-mixin-killer.jar", a.InJarMetadataSkipPaths);
            Assert.Contains("mods/client-gated-mixin.jar", a.UnclearSidePaths);
            Assert.DoesNotContain("mods/client-gated-mixin.jar", a.ClientOnlyPaths);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void MilesPack_jar_root_analyzes_when_present()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var sampleZip = Path.Combine(repoRoot, "data", "sample-packs", "real", "custom-forge-1.20.1-MilesPack.zip");
        if (!File.Exists(sampleZip))
            return;

        var result = ManualServerPackAnalyzer.AnalyzeFile(sampleZip);
        Assert.True(result.Succeeded, result.Error);
        var a = result.Value!;
        Assert.True(a.CanInstall);
        Assert.Equal(ManualServerPackKind.UnstructuredServer, a.Kind);
        Assert.True(a.MapRootJarsToMods);
        Assert.NotEqual(ManualServerPackKind.Unknown, a.Kind);
        Assert.True(a.OverrideListSkipCount > 0, "MilesPack should skip known client jars via the CF list.");
        Assert.Contains(a.OverrideListSkipPaths, p => p.Contains("embeddium", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(a.OverrideListSkipPaths, p => p.Contains("entityculling", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("api.curseforge.com", a.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
    }

    private static string CfManifestJson(bool includeFiles, string loaderId = "neoforge-21.1.0", bool extraFile = false)
    {
        var files = includeFiles
            ? extraFile
                ? "[{ \"projectID\": 1, \"fileID\": 1, \"required\": true }, { \"projectID\": 2, \"fileID\": 2, \"required\": true }]"
                : "[{ \"projectID\": 1, \"fileID\": 1, \"required\": true }]"
            : "[]";
        return $$"""
        {
          "minecraft": {
            "version": "1.21.1",
            "modLoaders": [{ "id": "{{loaderId}}", "primary": true }]
          },
          "manifestType": "minecraftModpack",
          "manifestVersion": 1,
          "name": "MCMGR Synthetic CF Export",
          "version": "0.1.0",
          "files": {{files}},
          "overrides": "overrides"
        }
        """;
    }

    private static string FixturePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", fileName);

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

    private static string WriteTemp(string fileName, MemoryStream zip)
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-manual-" + Guid.NewGuid().ToString("N") + "-" + fileName);
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-manual-" + Guid.NewGuid().ToString("N"));
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
}
