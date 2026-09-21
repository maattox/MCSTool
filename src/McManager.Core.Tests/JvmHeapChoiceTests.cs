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
    [InlineData("10G", "10G")]
    [InlineData("12g", "12G")]
    [InlineData("16G", "4G")]
    [InlineData("2G", "4G")]
    [InlineData("24G", "4G")]
    public void Normalize_snaps_to_presets(string? input, string expected)
    {
        Assert.Equal(expected, JvmHeapChoice.Normalize(input));
    }

    [Fact]
    public void Host_cap_is_8G_on_12GB_and_12G_on_24GB()
    {
        Assert.Equal("8G", JvmHeapChoice.MaxForHostMemoryGb(12));
        Assert.Equal("12G", JvmHeapChoice.MaxForHostMemoryGb(24));
        Assert.Equal(["4G", "6G", "8G"], JvmHeapChoice.PresetsForHostMemoryGb(12));
        Assert.Equal(["4G", "6G", "8G", "10G", "12G"], JvmHeapChoice.PresetsForHostMemoryGb(24));
        Assert.False(JvmHeapChoice.OffersExtraPresets(12));
        Assert.True(JvmHeapChoice.OffersExtraPresets(24));
        foreach (var p in JvmHeapChoice.PresetsForHostMemoryGb(12))
            Assert.True(JvmHeapChoice.FitsHost(p, 12));
        foreach (var p in JvmHeapChoice.Presets)
            Assert.True(JvmHeapChoice.FitsHost(p, 24));
        Assert.False(JvmHeapChoice.FitsHost("10G", 12));
        Assert.False(JvmHeapChoice.FitsHost("12G", 12));
        Assert.True(JvmHeapChoice.FitsHost("8G", 12));
        Assert.True(JvmHeapChoice.FitsHost("12G", 24));
    }

    [Theory]
    [InlineData("12G", 12, "8G")]
    [InlineData("10G", 12, "8G")]
    [InlineData("8G", 12, "8G")]
    [InlineData("12G", 24, "12G")]
    [InlineData("10G", 24, "10G")]
    [InlineData("16G", 24, "4G")]
    public void ClampToHost_keeps_legal_presets(string token, int hostGb, string expected)
    {
        Assert.Equal(expected, JvmHeapChoice.ClampToHost(token, hostGb));
    }

    [Fact]
    public void Allows_12G_heap_token_but_not_full_vm_ram()
    {
        Assert.True(JvmHeapChoice.IsAllowed("10G"));
        Assert.True(JvmHeapChoice.IsAllowed("12G"));
        Assert.False(JvmHeapChoice.IsAllowed("16G"));
        Assert.False(JvmHeapChoice.IsAllowed("24G"));
    }
}
