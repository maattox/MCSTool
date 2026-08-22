using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ExcludeIncludeMatcherTests
{
    [Fact]
    public void Global_exclude_skips_filename_stem()
    {
        var matcher = Layer1Only();
        var match = matcher.Match(null, "mods/Xaeros_Minimap_23.8.4_Forge_1.20.1.jar");
        Assert.Equal(ExcludeIncludeDecision.Exclude, match.Decision);
        Assert.Equal(PackFileSkipReason.OverrideList, match.Reason);
        Assert.Equal("Xaeros_Minimap", match.MatchedTerm);
    }

    [Fact]
    public void Display_name_exclude_matches_kebab_filename()
    {
        var matcher = Layer1Only();
        var match = matcher.Match(null, "mods/cull-less-leaves-1.0.jar");
        Assert.Equal(ExcludeIncludeDecision.Exclude, match.Decision);
        Assert.Equal("Cull Less Leaves", match.MatchedTerm);
    }

    [Fact]
    public void Global_force_include_beats_exclude_same_layer()
    {
        var lists = ExcludeIncludeLists.Parse("""
            {
              "globalExcludes": ["sodium"],
              "globalForceIncludes": ["sodium"],
              "modpacks": {}
            }
            """);
        var matcher = new ExcludeIncludeMatcher(lists);
        var match = matcher.Match(null, "mods/sodium-fabric-0.5.8.jar");
        Assert.Equal(ExcludeIncludeDecision.Keep, match.Decision);
        Assert.Equal(PackFileSkipReason.OverrideList, match.Reason);
    }

    [Fact]
    public void Per_pack_exclude_applies_only_when_slug_matches()
    {
        var matcher = Layer1Only();
        var hit = matcher.Match("cobbleverse", "mods/cloth-config-11.jar");
        Assert.Equal(ExcludeIncludeDecision.Exclude, hit.Decision);
        Assert.Equal("cloth-config", hit.MatchedTerm);

        var miss = matcher.Match("simply-optimized", "mods/cloth-config-11.jar");
        Assert.Equal(ExcludeIncludeDecision.NoMatch, miss.Decision);
        Assert.Equal(PackFileSkipReason.None, miss.Reason);

        var unknownPack = matcher.Match("no-such-pack", "mods/lithium.jar");
        Assert.Equal(ExcludeIncludeDecision.NoMatch, unknownPack.Decision);
    }

    [Fact]
    public void Per_pack_force_include_keeps_file()
    {
        var matcher = Layer1Only();
        var match = matcher.Match("COBBLEVERSE", "mods/configurable-1.jar");
        Assert.Equal(ExcludeIncludeDecision.Keep, match.Decision);
        Assert.Equal("configurable", match.MatchedTerm);
    }

    [Fact]
    public void Layer2_force_include_wins_over_layer1_exclude()
    {
        var matcher = BothLayers();
        var match = matcher.Match(null, "mods/sodium-fabric-0.5.8.jar");
        Assert.Equal(ExcludeIncludeDecision.Keep, match.Decision);
        Assert.Equal("sodium", match.MatchedTerm);
    }

    [Fact]
    public void Layer2_exclude_wins_over_layer1_no_force()
    {
        var matcher = BothLayers();
        var match = matcher.Match(null, "mods/iris-1.7.jar", projectSlug: "iris");
        Assert.Equal(ExcludeIncludeDecision.Exclude, match.Decision);
        Assert.Equal("iris", match.MatchedTerm);
    }

    [Fact]
    public void No_match_falls_through()
    {
        var matcher = Layer1Only();
        var match = matcher.Match(null, "mods/lithium-fabric.jar", projectSlug: "lithium");
        Assert.Equal(ExcludeIncludeDecision.NoMatch, match.Decision);
        Assert.False(match.Exclude);
        Assert.False(match.Keep);
    }

    [Fact]
    public void Slug_only_match_without_filename_hit()
    {
        var lists = ExcludeIncludeLists.Parse("""
            {
              "globalExcludes": ["better-advancements"],
              "globalForceIncludes": [],
              "modpacks": {}
            }
            """);
        var matcher = new ExcludeIncludeMatcher(lists);
        var bySlug = matcher.Match(null, "mods/BA-1.20.1.jar", "better-advancements");
        Assert.Equal(ExcludeIncludeDecision.Exclude, bySlug.Decision);

        var noSlug = matcher.Match(null, "mods/BA-1.20.1.jar");
        Assert.Equal(ExcludeIncludeDecision.NoMatch, noSlug.Decision);
    }

    [Fact]
    public void Regex_term_uses_find_semantics()
    {
        var lists = ExcludeIncludeLists.Parse("""
            {
              "globalExcludes": ["/sodium/"],
              "globalForceIncludes": [],
              "modpacks": {}
            }
            """);
        var matcher = new ExcludeIncludeMatcher(lists);
        Assert.Equal(ExcludeIncludeDecision.Exclude, matcher.Match(null, "mods/sodium-extra.jar").Decision);
        Assert.Equal(ExcludeIncludeDecision.NoMatch, matcher.Match(null, "mods/lithium.jar").Decision);
    }

    [Fact]
    public void Vendored_itzg_json_files_parse()
    {
        var modrinth = ExcludeIncludeMatcher.LoadEmbedded(ExcludeIncludeMatcher.ModrinthEmbeddedName);
        Assert.Contains(modrinth.GlobalExcludes, s => string.Equals(s, "sodium", StringComparison.OrdinalIgnoreCase));
        Assert.True(modrinth.TryGetPack("cobbleverse", out var cobble));
        Assert.Contains(cobble.Excludes, s => s.Contains("cloth-config", StringComparison.OrdinalIgnoreCase));

        var cf = ExcludeIncludeMatcher.LoadEmbedded(ExcludeIncludeMatcher.CurseForgeEmbeddedName);
        Assert.Contains(cf.GlobalExcludes, s => string.Equals(s, "embeddium", StringComparison.OrdinalIgnoreCase));
        Assert.NotEmpty(cf.GlobalExcludes);

        var overlay = ExcludeIncludeMatcher.LoadEmbedded(ExcludeIncludeMatcher.ProductOverlayEmbeddedName);
        Assert.Contains(overlay.GlobalExcludes, s => s.Equals("loading-screen", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overlay.GlobalExcludes, s => s.Equals("konkrete", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overlay.GlobalExcludes, s => s.Equals("titlebar", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(overlay.GlobalExcludes, s => s.Equals("flatlaf", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(overlay.GlobalForceIncludes);
        Assert.Empty(overlay.Modpacks);

        var bundled = ExcludeIncludeMatcher.ForModrinth();
        Assert.Equal(ExcludeIncludeDecision.Exclude, bundled.Match(null, "mods/sodium-0.5.jar").Decision);
        Assert.Equal(
            ExcludeIncludeDecision.Exclude,
            bundled.Match(null, "mods/example-loading-screen-1.0.jar").Decision);
        Assert.Equal(ExcludeIncludeDecision.Exclude, bundled.Match(null, "mods/konkrete-1.9.9.jar").Decision);
        Assert.Equal(ExcludeIncludeDecision.NoMatch, bundled.Match(null, "mods/lithium-fabric.jar").Decision);
    }

    private static ExcludeIncludeMatcher Layer1Only() =>
        new(ExcludeIncludeLists.Parse(Read("layer1-fixture.json")), ExcludeIncludeLists.Empty);

    private static ExcludeIncludeMatcher BothLayers() =>
        new(
            ExcludeIncludeLists.Parse(Read("layer1-fixture.json")),
            ExcludeIncludeLists.Parse(Read("layer2-fixture.json")));

    private static string Read(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "pack-lists", fileName);
        Assert.True(File.Exists(path), $"Fixture missing at {path}");
        return File.ReadAllText(path);
    }
}
