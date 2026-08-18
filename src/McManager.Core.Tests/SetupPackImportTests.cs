using System.IO.Compression;
using System.Text;
using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupPackImportTests
{
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
        Assert.DoesNotContain("modrinth.com/search", preview.ConfirmableSummary, StringComparison.OrdinalIgnoreCase);
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
}
