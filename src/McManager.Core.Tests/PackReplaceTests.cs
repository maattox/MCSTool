using System.IO.Compression;
using System.Text;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackReplaceTests
{
    [Fact]
    public void Warns_when_minecraft_version_changes_and_world_is_kept()
    {
        var warning = PackReplaceSaveCompatibility.Warn("1.21.1", "fabric", "1.20.1", "fabric");
        Assert.NotNull(warning);
        Assert.Contains("Minecraft 1.20.1", warning, StringComparison.Ordinal);
        Assert.Contains("1.21.1", warning, StringComparison.Ordinal);
        Assert.Contains("world may not load", warning, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Download a world save", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void Warns_more_strongly_when_leaving_a_modded_loader_for_vanilla()
    {
        var warning = PackReplaceSaveCompatibility.Warn("1.21.1", "fabric", "1.21.1", "vanilla");
        Assert.NotNull(warning);
        Assert.Contains("Vanilla", warning, StringComparison.Ordinal);
        Assert.Contains("Fabric", warning, StringComparison.Ordinal);
        Assert.Contains("missing from the world", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void No_warning_when_identity_matches_or_world_is_wiped()
    {
        Assert.Null(PackReplaceSaveCompatibility.Warn("1.21.1", "fabric", "1.21.1", "fabric"));
        Assert.Null(PackReplaceSaveCompatibility.Warn("1.21.1", "modded", "1.21.1", "fabric"));
        Assert.Null(PackReplaceSaveCompatibility.Warn("1.21.1", null, "1.21.1", "forge"));
        var path = WriteManualFabricZip();
        try
        {
            var wipePlan = PackReplacePlanner.TryCreate(path, wipeWorld: true, "1.20.1", "forge");
            Assert.True(wipePlan.Succeeded, wipePlan.Error);
            Assert.Null(wipePlan.Value!.SaveCompatibilityWarning);
            Assert.True(wipePlan.Value.WipeWorld);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Planner_keeps_world_and_warns_on_loader_change()
    {
        var path = WriteManualFabricZip();
        try
        {
            var plan = PackReplacePlanner.TryCreate(path, wipeWorld: false, "1.21.1", "forge");
            Assert.True(plan.Succeeded, plan.Error);
            Assert.False(plan.Value!.WipeWorld);
            Assert.NotNull(plan.Value.SaveCompatibilityWarning);
            Assert.Contains("Fabric", plan.Value.SaveCompatibilityWarning, StringComparison.Ordinal);
            Assert.Contains("Forge", plan.Value.SaveCompatibilityWarning, StringComparison.Ordinal);

            var state = PackReplacePlanner.ToWizardState(plan.Value.Preview);
            Assert.Equal(SetupServerType.Modded, state.ServerType);
            Assert.True(state.EulaAccepted);
            Assert.Equal(path, state.PackPath);
            Assert.Equal(MrpackAnalyzer.LoaderFabric, state.PackLoader);
            Assert.True(SetupPackImport.IsOnboxDistribution(SetupPackImport.ToDistribution(state)));
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Fact]
    public void Planner_blocks_quilt()
    {
        var path = WriteQuiltMrpack();
        try
        {
            var plan = PackReplacePlanner.TryCreate(path, wipeWorld: false, "1.21.1", "fabric");
            Assert.False(plan.Succeeded);
            Assert.Equal(SetupPackImport.QuiltRefusal, plan.Error);
        }
        finally
        {
            TryDelete(path);
        }
    }

    [Theory]
    [InlineData("world/level.dat", true)]
    [InlineData("worlds/foo/region/r.0.0.mca", true)]
    [InlineData("saves/world/level.dat", true)]
    [InlineData("world_nether/DIM-1", true)]
    [InlineData("mods/lithium.jar", false)]
    [InlineData("config/foo.toml", false)]
    [InlineData("eula.txt", false)]
    public void World_overlay_skip_matches_save_trees_only(string relative, bool skip)
    {
        Assert.Equal(skip, PackReplaceSaveCompatibility.IsWorldOverlayRelative(relative));
    }

    [Fact]
    public void Onbox_prepare_script_keeps_rcon_and_world_by_default()
    {
        var onbox = ProductPaths.FindOnboxDirectory();
        Assert.False(string.IsNullOrWhiteSpace(onbox), "onbox/mcmgr not found");
        var scriptPath = Path.Combine(onbox!, "prepare-pack-replace.sh");
        Assert.True(File.Exists(scriptPath), scriptPath);
        var text = File.ReadAllText(scriptPath);
        Assert.Contains("KEEP_WORLD", text, StringComparison.Ordinal);
        Assert.Contains("WIPE_WORLD", text, StringComparison.Ordinal);
        Assert.Contains("rcon.secret", text, StringComparison.Ordinal);
        Assert.Contains("Leave rcon.secret", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -f \"${RCON_SECRET}\"", text, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf /opt/mcmgr\n", text, StringComparison.Ordinal);
        Assert.Contains("BOOTSTRAP_STATE", text, StringComparison.Ordinal);
        Assert.Contains("systemctl stop", text, StringComparison.Ordinal);
    }

    private static string WriteManualFabricZip()
    {
        var serverJar = MakeJar(
            ("fabric.mod.json",
                """{"schemaVersion":1,"id":"content","version":"0","environment":"*","depends":{"minecraft":"1.21.1","fabricloader":">=0.16.0"}}"""));
        using var zip = MakeZipBytes(("mods/content.jar", serverJar));
        return WriteTemp("pack-replace-fabric.zip", zip);
    }

    private static string WriteQuiltMrpack()
    {
        var index =
            """
            {
              "formatVersion": 1,
              "game": "minecraft",
              "name": "Quilt pack",
              "dependencies": {
                "minecraft": "1.21.1",
                "quilt-loader": "0.27.0"
              },
              "files": []
            }
            """;
        using var zip = MakeZip(("modrinth.index.json", index));
        return WriteTemp("pack-replace-quilt.mrpack", zip);
    }

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
        var path = Path.Combine(Path.GetTempPath(), "mcmgr-pack-replace-" + Guid.NewGuid().ToString("N") + "-" + name);
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
}
