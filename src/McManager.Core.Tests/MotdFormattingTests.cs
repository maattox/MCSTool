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

    [Fact]
    public void Wrap_bold_closes_selection_with_reset()
    {
        const string text = "test MOTD message";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var result = MotdFormatting.WrapSpan(text, start, start + 4, 'l');
        Assert.Equal("test §lMOTD§r message", result.Text);
        Assert.Equal("MOTD", result.Text[result.InnerStart..result.InnerEnd]);
    }

    [Fact]
    public void Wrap_empty_selection_puts_caret_between_code_and_reset()
    {
        const string text = "test MOTD message";
        var at = text.IndexOf("MOTD", StringComparison.Ordinal);
        var result = MotdFormatting.WrapSpan(text, at, at, 'l');
        Assert.Equal("test §l§rMOTD message", result.Text);
        Assert.Equal(at + 2, result.InnerStart);
        Assert.Equal(at + 2, result.InnerEnd);
    }

    [Fact]
    public void Wrap_inside_color_restores_outer_run()
    {
        const string text = "§ctest MOTD message";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var result = MotdFormatting.WrapSpan(text, start, start + 4, 'l');
        Assert.Equal("§ctest §lMOTD§r§c message", result.Text);

        var empty = MotdFormatting.WrapSpan(text, start, start, 'l');
        Assert.Equal("§ctest §l§r§cMOTD message", empty.Text);
    }

    [Fact]
    public void Wrap_inside_color_and_bold_restores_both()
    {
        const string text = "§c§ltest MOTD message";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var result = MotdFormatting.WrapSpan(text, start, start + 4, 'o');
        Assert.Equal("§c§ltest §oMOTD§r§c§l message", result.Text);
    }

    [Fact]
    public void Wrap_inside_hex_color_restores_hex()
    {
        var prefix = MotdFormatting.HexPrefix("123456");
        var text = prefix + "test MOTD message";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var result = MotdFormatting.WrapSpan(text, start, start + 4, 'l');
        Assert.Equal(prefix + "test §lMOTD§r" + prefix + " message", result.Text);
    }

    [Fact]
    public void Visible_length_ignores_section_and_hex_runs()
    {
        Assert.Equal(0, MotdFormatting.VisibleLength(""));
        Assert.Equal(5, MotdFormatting.VisibleLength("§cHello"));
        Assert.Equal(2, MotdFormatting.VisibleLength("§l§oHi"));
        Assert.Equal(4, MotdFormatting.VisibleLength(MotdFormatting.HexPrefix("ffaa00") + "Gold"));
    }

    [Fact]
    public void Line_counters_use_59_limit_and_too_long_suffix()
    {
        var motd = new string('a', 41) + "\\n" + new string('b', 60);
        var metrics = MotdFormatting.MeasureListLines(motd);
        Assert.Equal(2, metrics.Count);
        Assert.Equal("line 1: 41/59", metrics[0].Label);
        Assert.False(metrics[0].TooLong);
        Assert.Equal("line 2: 60/59 — too long", metrics[1].Label);
        Assert.True(metrics[1].TooLong);
        Assert.Equal(MotdFormatting.ListLineVisibleLimit, metrics[0].Limit);
    }

    [Fact]
    public void Visible_text_strips_codes_and_keeps_newlines()
    {
        Assert.Equal("", MotdFormatting.VisibleText(""));
        Assert.Equal("Hello", MotdFormatting.VisibleText("§cHello"));
        Assert.Equal("Hi", MotdFormatting.VisibleText("§l§oHi"));
        Assert.Equal("Gold", MotdFormatting.VisibleText(MotdFormatting.HexPrefix("ffaa00") + "Gold"));
        Assert.Equal("Hello\nWorld", MotdFormatting.VisibleText("§cHello\n§aWorld"));
    }

    [Fact]
    public void Visible_raw_mapping_round_trips_bold_selection()
    {
        const string text = "test MOTD message";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var wrapped = MotdFormatting.WrapSpan(text, start, start + 4, 'l');
        Assert.Equal("test §lMOTD§r message", wrapped.Text);
        Assert.Equal(start, MotdFormatting.RawToVisible(wrapped.Text, wrapped.InnerStart));
        Assert.Equal(start + 4, MotdFormatting.RawToVisible(wrapped.Text, wrapped.InnerEnd));
        Assert.Equal(wrapped.InnerStart, MotdFormatting.VisibleToRaw(wrapped.Text, start));
        Assert.Equal(wrapped.InnerEnd, MotdFormatting.VisibleToRaw(wrapped.Text, start + 4));
    }

    [Fact]
    public void Visible_raw_mapping_empty_wrap_lands_in_hole()
    {
        const string text = "test MOTD message";
        var at = text.IndexOf("MOTD", StringComparison.Ordinal);
        var empty = MotdFormatting.WrapSpan(text, at, at, 'l');
        var vis = MotdFormatting.RawToVisible(empty.Text, empty.InnerStart);
        Assert.Equal(at, vis);
        Assert.Equal(empty.InnerStart, MotdFormatting.VisibleToRaw(empty.Text, vis));
        Assert.Equal(empty.InnerEnd, MotdFormatting.VisibleToRaw(empty.Text, vis));
    }

    [Fact]
    public void Editor_html_hides_codes_and_emits_hole_and_data_attrs()
    {
        var html = MotdFormatting.ToEditorHtml("test §lMOTD§r message");
        Assert.DoesNotContain("§", html, StringComparison.Ordinal);
        Assert.Contains("MOTD", html, StringComparison.Ordinal);
        Assert.Contains("data-motd-b=\"1\"", html, StringComparison.Ordinal);

        var hole = MotdFormatting.ToEditorHtml("test §l§rMOTD message");
        Assert.Contains(MotdFormatting.EditorHole, hole);
        Assert.Contains("data-motd-b=\"1\"", hole, StringComparison.Ordinal);
    }

    [Fact]
    public void Line_counters_follow_BuildMotd_and_omit_name_split()
    {
        var named = MotdFormatting.MeasureIdentityLines("Friends SMP", "Weekend world");
        Assert.Equal(2, named.Count);
        Assert.Equal(11, named[0].Used);
        Assert.Equal(13, named[1].Used);

        var omitted = MotdFormatting.MeasureIdentityLines("Ignored", "§cHello\n§aWorld", omitName: true);
        Assert.Equal(2, omitted.Count);
        Assert.Equal("line 1: 5/59", omitted[0].Label);
        Assert.Equal("line 2: 5/59", omitted[1].Label);
    }
}
