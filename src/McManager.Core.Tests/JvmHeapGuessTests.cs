using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class JvmHeapGuessTests
{
    [Theory]
    [InlineData(SetupServerType.Vanilla, 0, 24, "4G")]
    [InlineData(SetupServerType.Vanilla, 200, 24, "4G")]
    [InlineData("paper", 50, 24, "4G")]
    [InlineData(null, 50, 24, "4G")]
    public void Vanilla_or_Paper_stays_4G(string? serverType, int jars, int hostGb, string expected)
    {
        Assert.Equal(expected, JvmHeapGuess.Suggest(serverType, jars, hostGb));
        Assert.False(JvmHeapGuess.IsCappedByHost(serverType, jars, hostGb));
    }

    [Theory]
    [InlineData(0, "6G")]
    [InlineData(20, "6G")]
    [InlineData(39, "6G")]
    public void Modded_under_40_jars_is_6G(int jars, string expected)
    {
        Assert.Equal(expected, JvmHeapGuess.Suggest(SetupServerType.Modded, jars, 24));
        Assert.Equal(expected, JvmHeapGuess.Suggest(SetupServerType.Modded, jars, 12));
    }

    [Theory]
    [InlineData(40, "8G")]
    [InlineData(50, "8G")]
    [InlineData(99, "8G")]
    public void Modded_40_to_99_jars_is_8G(int jars, string expected)
    {
        Assert.Equal(expected, JvmHeapGuess.Suggest(SetupServerType.Modded, jars, 24));
        Assert.Equal(expected, JvmHeapGuess.Suggest(SetupServerType.Modded, jars, 12));
    }

    [Fact]
    public void Modded_120_jars_is_10G_on_24GB()
    {
        Assert.Equal("10G", JvmHeapGuess.Suggest(SetupServerType.Modded, 120, 24));
        Assert.Equal("10G", JvmHeapGuess.UncappedToken(isModded: true, 120));
        Assert.False(JvmHeapGuess.IsCappedByHost(SetupServerType.Modded, 120, 24));
    }

    [Fact]
    public void Modded_120_jars_is_8G_on_12GB()
    {
        Assert.Equal("8G", JvmHeapGuess.Suggest(SetupServerType.Modded, 120, 12));
        Assert.True(JvmHeapGuess.IsCappedByHost(SetupServerType.Modded, 120, 12));
    }

    [Fact]
    public void Never_suggests_12G()
    {
        Assert.NotEqual("12G", JvmHeapGuess.UncappedToken(isModded: true, 10_000));
        Assert.Equal("10G", JvmHeapGuess.Suggest(SetupServerType.Modded, 10_000, 24));
    }

    [Fact]
    public void Negative_jar_count_counts_as_empty_modded()
    {
        Assert.Equal("6G", JvmHeapGuess.Suggest(isModded: true, -3, 24));
    }
}
