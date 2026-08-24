using System.IO.Compression;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackDependencyFreezeTests
{
    [Fact]
    public void Skipping_required_cofh_while_keeping_thermal_is_refused()
    {
        var records = new[]
        {
            Record("mods/thermal.jar", ["thermal"], ["cofh_core"], skip: PackFileSkipReason.None),
            Record("mods/cofh_core.jar", ["cofh_core"], [], skip: PackFileSkipReason.OverrideList),
        };

        var classified = PackDependencyFreeze.Classify(records);
        Assert.Contains("mods/cofh_core.jar", classified.MustKeepPaths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("mods/cofh_core.jar", classified.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("mods/thermal.jar", classified.ServerSidePaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            classified.Review.MustKeep,
            i => i.Path.Contains("cofh_core", StringComparison.OrdinalIgnoreCase)
                 && i.Reason.Contains("thermal", StringComparison.OrdinalIgnoreCase));

        var blocked = PackDependencyFreeze.Classify(records, ["mods/cofh_core.jar"]);
        Assert.False(string.IsNullOrWhiteSpace(blocked.FreezeBlockReason));
        Assert.Contains("thermal", blocked.FreezeBlockReason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("mods/cofh_core.jar", blocked.MustKeepPaths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("mods/cofh_core.jar", blocked.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Optional_or_embedded_does_not_unskip_client_sibling()
    {
        var records = new[]
        {
            Record("mods/lib.jar", ["lib"], [], skip: PackFileSkipReason.None),
            Record("mods/iris.jar", ["iris"], [], skip: PackFileSkipReason.InJarMetadata),
        };

        var classified = PackDependencyFreeze.Classify(records);
        Assert.Contains("mods/iris.jar", classified.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("mods/iris.jar", classified.MustKeepPaths, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unreadable_metadata_is_needs_your_call_with_no_must_keep_edge()
    {
        var records = new[]
        {
            Record("mods/thermal.jar", ["thermal"], ["cofh_core"], skip: PackFileSkipReason.None),
            new PackJarRecord(
                "mods/mystery.jar",
                [],
                [],
                unclearSide: true,
                forceIncluded: false,
                automaticSkipReason: PackFileSkipReason.None),
        };

        var classified = PackDependencyFreeze.Classify(records);
        Assert.Contains("mods/mystery.jar", classified.Review.NeedsYourCall.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("mods/mystery.jar", classified.MustKeepPaths, StringComparer.OrdinalIgnoreCase);
        Assert.True(classified.NeedsAssistedReview);
    }

    [Fact]
    public void Operator_skip_of_unknown_moves_to_will_skip()
    {
        var records = new[]
        {
            Record("mods/ok.jar", ["ok"], [], skip: PackFileSkipReason.None),
            new PackJarRecord(
                "mods/mystery.jar",
                ["mystery"],
                [],
                unclearSide: true,
                forceIncluded: false,
                automaticSkipReason: PackFileSkipReason.None),
        };

        var classified = PackDependencyFreeze.Classify(records, ["mystery.jar"]);
        Assert.Contains("mods/mystery.jar", classified.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            classified.Review.WillSkip,
            i => i.Path.Contains("mystery", StringComparison.OrdinalIgnoreCase)
                 && i.SkipReason == PackFileSkipReason.OperatorSkip);
        Assert.Null(classified.FreezeBlockReason);
        Assert.False(classified.NeedsAssistedReview);
    }

    [Fact]
    public void Manual_zip_exclude_list_wins_over_in_jar_client()
    {
        var clientJar = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"embeddium","version":"0","environment":"client"}"""));
        using var zip = MakeZipBytes(("mods/embeddium-dummy.jar", clientJar));
        var path = WriteTemp("exclude-before-injar.zip", zip);
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path);
            Assert.True(analysis.Succeeded, analysis.Error);
            var a = analysis.Value!;
            Assert.Contains("mods/embeddium-dummy.jar", a.OverrideListSkipPaths, StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("mods/embeddium-dummy.jar", a.InJarMetadataSkipPaths, StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Manual_zip_thermal_style_freeze_unskips_required_cofh()
    {
        var thermal = MakeJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"thermal","version":"0","environment":"*","depends":{"cofh_core":"*","minecraft":"1.20.1"}}
            """));
        var cofh = MakeJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"cofh_core","version":"0","environment":"client"}
            """));
        var lists = ExcludeIncludeLists.Parse("""
            {"globalExcludes":["cofh_core"],"globalForceIncludes":[],"modpacks":{}}
            """);
        var matcher = new ExcludeIncludeMatcher(lists);
        using var zip = MakeZipBytes(
            ("mods/thermal.jar", thermal),
            ("mods/cofh_core.jar", cofh));
        var path = WriteTemp("thermal-freeze.zip", zip);
        try
        {
            var analysis = ManualServerPackAnalyzer.AnalyzeFile(path, matcher);
            Assert.True(analysis.Succeeded, analysis.Error);
            var a = analysis.Value!;
            Assert.Contains("mods/cofh_core.jar", a.AssistedReview.MustKeep.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            Assert.DoesNotContain("mods/cofh_core.jar", a.ClientOnlyPaths, StringComparer.OrdinalIgnoreCase);
            Assert.Contains("mods/thermal.jar", a.ServerSidePaths, StringComparer.OrdinalIgnoreCase);

            var blocked = a.ApplyOperatorSkips(["cofh_core"]);
            Assert.False(string.IsNullOrWhiteSpace(blocked.FreezeBlockReason));
            Assert.True(blocked.CanInstall);
            Assert.Contains("mods/cofh_core.jar", blocked.AssistedReview.MustKeep.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Fabric_id_and_required_depends_are_read()
    {
        var peek = PeekJar(("fabric.mod.json", """
            {"schemaVersion":1,"id":"thermal","version":"0","depends":{"minecraft":"1.20.1","java":">=17","fabricloader":">=0.15","cofh_core":"*"}}
            """));
        Assert.Equal("thermal", peek.ModId);
        Assert.Contains("cofh_core", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("minecraft", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("java", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("fabricloader", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Forge_mandatory_dependency_is_required_optional_is_not()
    {
        var peek = PeekJar(("META-INF/mods.toml", """
            [[mods]]
            modId="thermal"
            [[dependencies.thermal]]
            modId="cofh_core"
            mandatory=true
            [[dependencies.thermal]]
            modId="iris"
            mandatory=false
            [[dependencies.thermal]]
            modId="minecraft"
            mandatory=true
            """));
        Assert.Equal("thermal", peek.ModId);
        Assert.Contains("cofh_core", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("iris", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("minecraft", peek.AllRequiredModIds, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Overlay_unskip_removes_term()
    {
        var data = Path.Combine(Path.GetTempPath(), "mcmgr-unskip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        try
        {
            var pack = Path.Combine(data, "pack.zip");
            File.WriteAllBytes(pack, [1, 2, 3, 4]);
            var hash = Layer2LocalOverlay.TryHashFile(pack);
            Assert.False(string.IsNullOrWhiteSpace(hash));
            Layer2LocalOverlay.PromoteExclude(data, hash!, "mystery.jar");
            Layer2LocalOverlay.RemoveExclude(data, hash!, "mystery.jar");
            var lists = Layer2LocalOverlay.Load(data);
            Assert.True(lists.TryGetPack(Layer2LocalOverlay.IdentityKey(hash!), out var packEntry));
            Assert.DoesNotContain(packEntry.Excludes, s => s.Equals("mystery.jar", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try { Directory.Delete(data, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Setup_preview_needs_assisted_review_but_manual_unclear_can_continue()
    {
        var ok = MakeJar(("fabric.mod.json", """{"schemaVersion":1,"id":"ok","version":"0","environment":"*"}"""));
        using var zip = MakeZipBytes(
            ("mods/ok.jar", ok),
            ("mods/mystery.jar", Encoding.UTF8.GetBytes("not-a-jar")));
        var path = WriteTemp("preview-assisted.zip", zip);
        try
        {
            var result = SetupPackImport.AnalyzeFile(path);
            Assert.True(result.Succeeded, result.Error);
            var preview = result.Value!;
            Assert.True(preview.CanContinue);
            Assert.True(preview.NeedsAssistedReview);
            Assert.Single(preview.AssistedReview.NeedsYourCall);
            Assert.Null(preview.FreezeBlockReason);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Mrpack_unclear_still_fails_after_freeze()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", "fabric-strip.mrpack");
        if (!File.Exists(path))
            return;

        var result = SetupPackImport.AnalyzeFile(path);
        Assert.True(result.Succeeded, result.Error);
        Assert.False(result.Value!.CanContinue);
        Assert.Equal(SetupPackImport.UnclearSideRefusal, result.Value.BlockReason);
        Assert.False(result.Value.NeedsAssistedReview);
    }

    private static PackJarRecord Record(
        string path,
        string[] provided,
        string[] required,
        PackFileSkipReason skip) =>
        new(path, provided, required, unclearSide: false, forceIncluded: false, skip);

    private static InJarSideDetector.PeekResult PeekJar(params (string Name, string Content)[] entries)
    {
        using var zip = MakeZip(entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content))).ToArray());
        return InJarSideDetector.Peek(zip);
    }

    private static MemoryStream MakeZip(params (string Name, byte[] Content)[] entries)
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

    private static MemoryStream MakeZipBytes(params (string Name, byte[] Content)[] entries) =>
        MakeZip(entries);

    private static byte[] MakeJar(params (string Name, string Content)[] entries)
    {
        using var zip = MakeZip(entries.Select(e => (e.Name, Encoding.UTF8.GetBytes(e.Content))).ToArray());
        return zip.ToArray();
    }

    private static string WriteTemp(string fileName, MemoryStream zip)
    {
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-freeze-" + Guid.NewGuid().ToString("N") + "-" + fileName);
        File.WriteAllBytes(path, zip.ToArray());
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { }
    }
}
