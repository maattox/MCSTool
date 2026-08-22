using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class DerivedPackArchiveTests
{
    [Fact]
    public void Jar_root_fixture_builds_derived_with_user_override()
    {
        var path = FixturePath("jar-root.zip");
        if (!File.Exists(path))
            return;

        var sourceLen = new FileInfo(path).Length;
        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        Assert.True(analysis.Succeeded, analysis.Error);
        var a = analysis.Value!;
        Assert.True(a.MapRootJarsToMods);

        var dest = Path.Combine(NewTempDir(), "derived.zip");
        var fields = new DerivedPackFields("1.21.1", MrpackAnalyzer.LoaderFabric, "0.16.9", 21);
        var build = DerivedPackArchive.Build(path, a, fields, dest, "jar-root.zip");
        Assert.True(build.Succeeded, build.Error);
        Assert.Equal(sourceLen, new FileInfo(path).Length);

        using var zip = new ZipArchive(File.OpenRead(dest), ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry(DerivedPackIdentity.SidecarEntryName));
        Assert.NotNull(zip.GetEntry(MrpackAnalyzer.IndexEntryName));
        Assert.NotNull(zip.GetEntry("overrides/mods/dummy-server.jar"));
        Assert.Null(zip.GetEntry("manifest.json"));

        var sidecarJson = ReadEntryText(zip, DerivedPackIdentity.SidecarEntryName);
        var sidecar = JsonSerializer.Deserialize<DerivedPackSidecar>(sidecarJson);
        Assert.NotNull(sidecar);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, sidecar!.Loader);
        Assert.Equal("1.21.1", sidecar.MinecraftVersion);
        Assert.Equal(21, sidecar.JavaMajor);

        var indexJson = ReadEntryText(zip, MrpackAnalyzer.IndexEntryName);
        using var doc = JsonDocument.Parse(indexJson);
        var root = doc.RootElement;
        Assert.Equal("1.21.1", root.GetProperty("dependencies").GetProperty("minecraft").GetString());
        Assert.Equal("0.16.9", root.GetProperty("dependencies").GetProperty("fabric-loader").GetString());
        var files = root.GetProperty("files");
        Assert.True(files.GetArrayLength() > 0);
        var first = files[0];
        Assert.True(first.TryGetProperty("hashes", out var hashes));
        Assert.True(hashes.TryGetProperty("sha512", out _));
        Assert.Equal(0, first.GetProperty("downloads").GetArrayLength());
    }

    [Fact]
    public void Reanalyze_derived_uses_sidecar_identity()
    {
        var path = FixturePath("jar-root.zip");
        if (!File.Exists(path))
            return;

        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        Assert.True(analysis.Succeeded);
        var dest = Path.Combine(NewTempDir(), "derived.zip");
        var fields = new DerivedPackFields("1.21.1", MrpackAnalyzer.LoaderFabric, "0.16.9", 21);
        var build = DerivedPackArchive.Build(path, analysis.Value!, fields, dest);
        Assert.True(build.Succeeded, build.Error);

        var preview = SetupPackImport.AnalyzeFile(dest);
        Assert.True(preview.Succeeded, preview.Error);
        var p = preview.Value!;
        Assert.Equal(SetupPackImport.KindManualZip, p.Kind);
        Assert.True(p.CanContinue);
        Assert.True(p.NeedsIdentityConfirm);
        Assert.Equal(MrpackAnalyzer.LoaderFabric, p.Loader);
        Assert.Equal("1.21.1", p.MinecraftVersion);
        Assert.Equal(21, p.JavaMajor);
        Assert.Equal("0.16.9", p.LoaderVersion);
    }

    [Fact]
    public void Install_derived_skips_client_and_exclude_jars()
    {
        var path = FixturePath("jar-root.zip");
        if (!File.Exists(path))
            return;

        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        var destZip = Path.Combine(NewTempDir(), "derived.zip");
        var fields = new DerivedPackFields("1.20.1", MrpackAnalyzer.LoaderForge, "47.0.0", 17);
        var build = DerivedPackArchive.Build(path, analysis.Value!, fields, destZip);
        Assert.True(build.Succeeded, build.Error);

        var installDest = NewTempDir();
        var install = ManualServerPackInstaller.Install(destZip, installDest, retainDataDirectory: null);
        Assert.True(install.Succeeded, install.Error);
        Assert.True(File.Exists(Path.Combine(installDest, "mods", "dummy-server.jar")));
        Assert.False(File.Exists(Path.Combine(installDest, "mods", "dummy-client.jar")));
        Assert.False(File.Exists(Path.Combine(installDest, "mods", "embeddium-dummy.jar")));
    }

    [Fact]
    public void Identity_validation_rules()
    {
        Assert.False(DerivedPackIdentity.IsComplete("(unknown)", "fabric", "0.16.9", "17"));
        Assert.False(DerivedPackIdentity.IsComplete("1.21.1", "quilt", "0.16.9", "17"));
        Assert.False(DerivedPackIdentity.IsComplete("1.21.1", "fabric", "", "17"));
        Assert.False(DerivedPackIdentity.IsComplete("1.21.1", "fabric", "0.16.9", "3"));
        Assert.True(DerivedPackIdentity.IsComplete("1.21.1", "fabric", "0.16.9", "17"));
    }

    [Fact]
    public void DisagreesWithDetection_when_user_overrides_peek()
    {
        Assert.True(DerivedPackIdentity.DisagreesWithDetection(
            "1.20.1", MrpackAnalyzer.LoaderForge, "1.21.1", MrpackAnalyzer.LoaderFabric));
        Assert.False(DerivedPackIdentity.DisagreesWithDetection(
            "unknown", "unknown", "1.21.1", MrpackAnalyzer.LoaderFabric));
    }

    [Fact]
    public void Unstructured_unknown_loader_can_continue_at_analyze()
    {
        var serverJar = Encoding.UTF8.GetBytes("not-a-jar");
        using var zip = MakeZipBytes(("mystery.jar", serverJar));
        var path = WriteTemp("unknown-loader.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            var preview = result.Value!;
            Assert.True(preview.CanContinue);
            Assert.True(preview.NeedsIdentityConfirm);
            Assert.NotEqual(SetupPackImport.LoaderRefusal, preview.BlockReason);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void ShouldUseManualInstaller_true_for_sidecar_zip()
    {
        var path = FixturePath("jar-root.zip");
        if (!File.Exists(path))
            return;

        var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
        var dest = Path.Combine(NewTempDir(), "derived.zip");
        var fields = new DerivedPackFields("1.21.1", MrpackAnalyzer.LoaderFabric, "0.16.9", 21);
        Assert.True(DerivedPackArchive.Build(path, analysis.Value!, fields, dest).Succeeded);

        Assert.True(SetupBootstrapService.ShouldUseManualInstaller(dest, SetupPackImport.KindManualZip));
    }

    private static string ReadEntryText(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name) ?? throw new InvalidOperationException($"Missing {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static string FixturePath(string name)
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "fixtures", "packs"));
        return Path.Combine(dir, name);
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-derived-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
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
                output.Write(content, 0, content.Length);
            }
        }

        ms.Position = 0;
        return ms;
    }

    private static string WriteTemp(string name, MemoryStream zip)
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-derived-" + Guid.NewGuid().ToString("N") + "-" + name);
        using (var file = File.Create(path))
            zip.CopyTo(file);
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
