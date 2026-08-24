using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MotdFormattingTests
{
    [Fact]
    public void Normalize_paste_strips_motd_prefix_and_unicode_escapes()
    {
        var got = MotdFormatting.NormalizePaste("motd=\\u00a7cHello\\n\\u00a7aWorld");
        Assert.Equal("§cHello\n§aWorld", got);
    }

    [Fact]
    public void Normalize_paste_maps_amp_and_hash_hex()
    {
        Assert.Equal("§cRed", MotdFormatting.NormalizePaste("&cRed"));
        var hex = MotdFormatting.NormalizePaste("&#ffaa00Gold");
        Assert.StartsWith("§x", hex, StringComparison.Ordinal);
        Assert.Contains("Gold", hex, StringComparison.Ordinal);
    }

    [Fact]
    public void Preview_applies_vanilla_colors_and_formats()
    {
        var lines = MotdFormatting.ToPreviewLines("§c§lHello\\n§aWorld");
        Assert.Equal(2, lines.Count);
        Assert.Equal("Hello", lines[0][0].Text);
        Assert.Equal("#FF5555", lines[0][0].ColorHex);
        Assert.True(lines[0][0].Bold);
        Assert.Equal("World", lines[1][0].Text);
        Assert.Equal("#55FF55", lines[1][0].ColorHex);
        Assert.False(lines[1][0].Bold);
    }

    [Fact]
    public void Preview_parses_section_x_hex()
    {
        var hex = MotdFormatting.HexPrefix("ffaa00");
        var lines = MotdFormatting.ToPreviewLines(hex + "Hi");
        Assert.Equal("Hi", lines[0][0].Text);
        Assert.Equal("#ffaa00", lines[0][0].ColorHex);
    }

    [Fact]
    public void Preview_html_encodes_text()
    {
        var html = MotdFormatting.ToPreviewHtml("<script>");
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Properties_value_rejects_raw_newlines()
    {
        Assert.True(MotdFormatting.IsSafePropertiesValue("§cHi\\n§aThere"));
        Assert.False(MotdFormatting.IsSafePropertiesValue("Hi\nThere"));
    }
}
