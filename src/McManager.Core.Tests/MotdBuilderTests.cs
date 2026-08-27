using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MotdBuilderTests
{
    [Fact]
    public void Spec_example_blue_then_bold_keeps_blue_on_unbolded_letters()
    {
        var builder = new MotdBuilder("test MOTD text");
        for (var i = 5; i <= 8; i++)
            builder.InputEffects[i].Color = "§9";
        Assert.Equal("test §9MOTD§r text", builder.GenerateCode());

        for (var i = 6; i <= 7; i++)
            builder.InputEffects[i].Bold = true;
        // Re-emit §9 on D instead of §r§9: a color code resets bold and keeps D blue.
        // Bare §r before D would also clear the blue in Java Edition.
        Assert.Equal("test §9M§lOT§9D§r text", builder.GenerateCode());
        MotdFormattingTests.AssertMotdLooksLike(
            "test §9M§lOT§r§9D§r text",
            builder.GenerateCode());
    }

    [Theory]
    [InlineData('0')]
    [InlineData('1')]
    [InlineData('2')]
    [InlineData('3')]
    [InlineData('4')]
    [InlineData('5')]
    [InlineData('6')]
    [InlineData('7')]
    [InlineData('8')]
    [InlineData('9')]
    [InlineData('a')]
    [InlineData('b')]
    [InlineData('c')]
    [InlineData('d')]
    [InlineData('e')]
    [InlineData('f')]
    public void Toggle_applies_and_removes_every_vanilla_color(char code)
    {
        const string text = "test MOTD text";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var wrapped = MotdFormatting.ToggleSpan(text, start, start + 4, code);
        Assert.Equal($"test §{code}MOTD§r text", wrapped.Text);

        var again = MotdFormatting.ToggleSpan(wrapped.Text, wrapped.InnerStart, wrapped.InnerEnd, code);
        Assert.Equal(text, again.Text);
    }

    [Theory]
    [InlineData('k')]
    [InlineData('l')]
    [InlineData('m')]
    [InlineData('n')]
    [InlineData('o')]
    public void Toggle_applies_and_removes_every_format(char code)
    {
        const string text = "test MOTD text";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var wrapped = MotdFormatting.ToggleSpan(text, start, start + 4, code);
        Assert.Equal($"test §{code}MOTD§r text", wrapped.Text);

        var again = MotdFormatting.ToggleSpan(wrapped.Text, wrapped.InnerStart, wrapped.InnerEnd, code);
        Assert.Equal(text, again.Text);
    }

    [Fact]
    public void Removing_one_format_keeps_the_others_and_the_color()
    {
        var builder = new MotdBuilder("MOTD");
        foreach (var fx in builder.InputEffects)
        {
            fx.Color = "§c";
            fx.Bold = true;
            fx.Italic = true;
            fx.Underline = true;
        }

        builder.InputEffects[1].Bold = false;
        builder.InputEffects[2].Bold = false;

        // §c§l§n§o M, drop bold on OT by re-emitting color + remaining formats, then bold back on D.
        Assert.Equal("§c§l§n§oM§c§n§oOT§lD§r", builder.GenerateCode());
    }

    [Fact]
    public void Color_change_reapplies_formats_because_color_codes_reset_them()
    {
        var builder = new MotdBuilder("AB");
        builder.InputEffects[0].Color = "§c";
        builder.InputEffects[0].Bold = true;
        builder.InputEffects[1].Color = "§9";
        builder.InputEffects[1].Bold = true;
        Assert.Equal("§c§lA§9§lB§r", builder.GenerateCode());
    }

    [Fact]
    public void Hex_color_is_preserved_and_resets_formats_like_vanilla()
    {
        var hex = MotdFormatting.HexPrefix("123456");
        var builder = MotdBuilder.Parse(hex + "MOTD");
        Assert.Equal(hex, builder.InputEffects[0].Color);
        builder.InputEffects[1].Bold = true;
        builder.InputEffects[2].Bold = true;
        Assert.Equal(hex + "M§lOT" + hex + "D§r", builder.GenerateCode());
    }

    [Fact]
    public void Hex_matching_vanilla_is_emitted_as_the_short_code()
    {
        var hex = MotdFormatting.HexPrefix("FF5555");
        var builder = MotdBuilder.Parse(hex + "Hi");
        Assert.Equal("§c", builder.InputEffects[0].Color);
        Assert.Equal("§cHi§r", builder.GenerateCode());
    }

    [Fact]
    public void Newlines_reset_style_so_each_list_line_starts_default()
    {
        var builder = MotdBuilder.Parse("§cHello\nWorld");
        Assert.Equal("§c", builder.InputEffects[0].Color);
        Assert.Equal("", builder.InputEffects[^1].Color);
        Assert.Equal("§cHello\nWorld", builder.GenerateCode());
    }

    [Fact]
    public void SetInputText_keeps_effects_on_characters_that_still_exist()
    {
        var builder = new MotdBuilder("test MOTD text");
        for (var i = 5; i <= 8; i++)
            builder.InputEffects[i].Color = "§9";
        builder.SetInputText("test MOTD text v2");
        Assert.Equal("§9", builder.InputEffects[5].Color);
        Assert.True(builder.InputEffects[^1].IsDefault);
        Assert.Equal("test §9MOTD§r text v2", builder.GenerateCode());
    }

    [Fact]
    public void Reset_clears_color_and_all_formats_on_the_range()
    {
        const string text = "test MOTD text";
        var start = text.IndexOf("MOTD", StringComparison.Ordinal);
        var colored = MotdFormatting.ToggleSpan(text, start, start + 4, 'c');
        colored = MotdFormatting.ToggleSpan(colored.Text, colored.InnerStart, colored.InnerEnd, 'l');
        var reset = MotdFormatting.ToggleSpan(colored.Text, colored.InnerStart, colored.InnerEnd, 'r');
        Assert.Equal(text, reset.Text);
    }

    [Fact]
    public void Generate_is_stable_after_parse()
    {
        const string motd = "test §9M§lOT§9D§r text";
        var once = MotdBuilder.Parse(motd).GenerateCode();
        var twice = MotdBuilder.Parse(once).GenerateCode();
        Assert.Equal(once, twice);
        MotdFormattingTests.AssertMotdLooksLike(motd, once);
    }

    [Fact]
    public void Messy_stacked_codes_compact_on_toggle()
    {
        const string messy = "test §9§9MOTD§r message";
        var start = messy.IndexOf("MOTD", StringComparison.Ordinal);
        var cleaned = MotdFormatting.ToggleSpan(messy, start, start + 4, 'l');
        Assert.Equal("test §9§lMOTD§r message", cleaned.Text);
    }
}
