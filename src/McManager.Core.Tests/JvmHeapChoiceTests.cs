using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class JvmHeapChoiceTests
{
    [Theory]
    [InlineData(null, "4G")]
    [InlineData("", "4G")]
    [InlineData("4g", "4G")]
    [InlineData("6G", "6G")]
    [InlineData("8G", "8G")]
    [InlineData("12G", "4G")]
    [InlineData("2G", "4G")]
    public void Normalize_snaps_to_presets(string? input, string expected)
    {
        Assert.Equal(expected, JvmHeapChoice.Normalize(input));
    }

    [Fact]
    public void All_presets_fit_both_product_hosts()
    {
        Assert.Equal("8G", JvmHeapChoice.MaxForHostMemoryGb(12));
        Assert.Equal("8G", JvmHeapChoice.MaxForHostMemoryGb(24));
        foreach (var p in JvmHeapChoice.Presets)
        {
            Assert.True(JvmHeapChoice.FitsHost(p, 12));
            Assert.True(JvmHeapChoice.FitsHost(p, 24));
        }
    }

    [Fact]
    public void Does_not_allow_host_ram_as_heap()
    {
        Assert.False(JvmHeapChoice.IsAllowed("12G"));
        Assert.False(JvmHeapChoice.IsAllowed("24G"));
    }
}
