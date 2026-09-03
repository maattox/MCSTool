using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class WorldSeedTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("  abc  ", "abc")]
    [InlineData("hello\r\nworld", "helloworld")]
    public void Normalize_trims_and_strips_breaks(string? raw, string expected)
    {
        Assert.Equal(expected, WorldSeed.Normalize(raw));
    }

    [Fact]
    public void Normalize_caps_length()
    {
        var raw = new string('a', WorldSeed.MaxLength + 20);
        var got = WorldSeed.Normalize(raw);
        Assert.Equal(WorldSeed.MaxLength, got.Length);
        Assert.Equal(new string('a', WorldSeed.MaxLength), got);
    }

    [Fact]
    public void Seed_patch_set_script_writes_level_seed()
    {
        var script = WorldSeedPatch.BuildRemoteScript("MySeed");
        Assert.Contains("python3 -c", script, StringComparison.Ordinal);
        Assert.Contains("level-seed=", script, StringComparison.Ordinal);
        Assert.Contains("'set'", script, StringComparison.Ordinal);
        Assert.Contains("'MySeed'", script, StringComparison.Ordinal);
        Assert.Contains(WorldSeedPatch.PropertiesPath, script, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", script, StringComparison.Ordinal);
    }

    [Fact]
    public void Seed_patch_blank_clears_key()
    {
        var script = WorldSeedPatch.BuildRemoteScript("  ");
        Assert.Contains("'clear'", script, StringComparison.Ordinal);
        Assert.Contains("python3 -c", script, StringComparison.Ordinal);
    }
}

public sealed class JvmExtraFlagsTests
{
    [Fact]
    public void Parse_splits_and_strips_heap()
    {
        var flags = JvmExtraFlags.Parse("-XX:+UseG1GC\n-Xms8G -Xmx8G  -XX:G1HeapRegionSize=8M");
        Assert.Equal(["-XX:+UseG1GC", "-XX:G1HeapRegionSize=8M"], flags);
    }

    [Fact]
    public void ContainedHeapTokens_detects_xms()
    {
        Assert.True(JvmExtraFlags.ContainedHeapTokens("-Xms4G -XX:+UseG1GC"));
        Assert.False(JvmExtraFlags.ContainedHeapTokens("-XX:+UseG1GC"));
    }
}
