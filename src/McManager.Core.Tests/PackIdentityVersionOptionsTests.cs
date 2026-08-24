using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackIdentityVersionOptionsTests
{
    [Fact]
    public void WithCurrent_prepends_detected_value_missing_from_catalog()
    {
        var options = PackIdentityVersionOptions.WithCurrent(["1.21.1", "1.20.1"], "1.16.5-detected");
        Assert.Equal(["1.16.5-detected", "1.21.1", "1.20.1"], options);
    }

    [Fact]
    public void WithCurrent_does_not_duplicate_detected_value_already_in_catalog()
    {
        var options = PackIdentityVersionOptions.WithCurrent(["1.21.1", "1.20.1"], "1.21.1");
        Assert.Equal(["1.21.1", "1.20.1"], options);
    }

    [Fact]
    public void MinecraftIds_include_detected_snapshot_not_in_release_filter()
    {
        var manifest = MojangVersionCatalog.LoadEmbeddedFixture();
        var options = PackIdentityVersionOptions.MinecraftIds(manifest, "25w14a", includeSnapshots: false);
        Assert.Equal("25w14a", options[0]);
        Assert.Contains(options, id => id.StartsWith("1.", StringComparison.Ordinal));
        Assert.Equal(1, options.Count(id => string.Equals(id, "25w14a", StringComparison.Ordinal)));
    }

    [Fact]
    public void FabricLoaderVersions_keep_detected_loader_not_in_meta_list()
    {
        var loaders = FabricMetaClient.ParseGameLoaders(Read("fabric-meta-loader-1.21.8.json"));
        Assert.NotNull(loaders);
        var options = PackIdentityVersionOptions.FabricLoaderVersions(loaders, "0.99.0-detected");
        Assert.Equal("0.99.0-detected", options[0]);
        Assert.Contains("0.17.2", options);
    }

    [Fact]
    public void ForgeVersions_include_recommended_latest_and_detected()
    {
        var promos = ForgePromotionsClient.ParsePromos(Read("forge-promotions-slim.json"));
        Assert.NotNull(promos);
        var options = PackIdentityVersionOptions.ForgeVersions(promos, "1.12.2", "14.23.5.2860");
        Assert.Equal("14.23.5.2860", options[0]);
        Assert.Contains("14.23.5.2854", options);
    }

    [Fact]
    public void NeoForgeVersions_keep_detected_build_missing_from_maven()
    {
        var versions = NeoForgeMavenClient.ParseVersions(Read("neoforge-maven-metadata.xml"));
        Assert.NotNull(versions);
        var options = PackIdentityVersionOptions.NeoForgeVersions(versions, "1.21.1", "21.1.0-detected");
        Assert.Equal("21.1.0-detected", options[0]);
        Assert.Contains("21.1.98", options);
        Assert.DoesNotContain("21.1.200-beta", options);
        Assert.DoesNotContain("21.10.1", options);
    }

    [Fact]
    public void JavaMajors_follow_minecraft_floor_and_keep_detected()
    {
        var options = PackIdentityVersionOptions.JavaMajors("1.21.1", "16");
        Assert.Equal("16", options[0]);
        Assert.Contains("21", options);
        Assert.Contains("25", options);
        Assert.DoesNotContain("8", options);
    }

    [Fact]
    public void LoaderVersions_empty_catalog_still_returns_detected()
    {
        var options = PackIdentityVersionOptions.LoaderVersions(
            MrpackAnalyzer.LoaderForge,
            "1.20.1",
            "47.2.0",
            fabricLoaders: null,
            forgePromos: null,
            neoForgeVersions: null);
        Assert.Equal(["47.2.0"], options);
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
}
