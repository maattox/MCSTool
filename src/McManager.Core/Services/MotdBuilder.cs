using System.Text;

namespace McManager.Core.Services;

/// <summary>
/// Effects on a single MOTD character. Empty <see cref="Color"/> means default
/// (no color code / after <c>§r</c>). Format flags only ever turn on in Minecraft;
/// turning one off requires a reset (or a new color code, which also clears formats).
/// </summary>
public sealed class MotdCharEffects
{
    /// <summary>Vanilla <c>§c</c> / hex <c>§x…</c> prefix, or empty for default.</summary>
    public string Color = "";

    public bool Bold;
    public bool Italic;
    public bool Underline;
    public bool Strikethrough;
    public bool Obfuscated;

    public MotdCharEffects Clone() => new()
    {
        Color = Color,
        Bold = Bold,
        Italic = Italic,
        Underline = Underline,
        Strikethrough = Strikethrough,
        Obfuscated = Obfuscated,
    };

    public bool IsDefault =>
        Color.Length == 0 && !Bold && !Italic && !Underline && !Strikethrough && !Obfuscated;

    public bool HasColor => Color.Length > 0;

    public bool SameAs(MotdCharEffects other) =>
        Color.Equals(other.Color, StringComparison.OrdinalIgnoreCase)
        && Bold == other.Bold
        && Italic == other.Italic
        && Underline == other.Underline
        && Strikethrough == other.Strikethrough
        && Obfuscated == other.Obfuscated;

    public bool HasCode(char code) =>
        char.ToLowerInvariant(code) switch
        {
            'l' => Bold,
            'o' => Italic,
            'n' => Underline,
            'm' => Strikethrough,
            'k' => Obfuscated,
            _ => Color.Equals(MotdFormatting.CodePrefix(code), StringComparison.OrdinalIgnoreCase),
        };

    public void SetCode(char code)
    {
        switch (char.ToLowerInvariant(code))
        {
            case 'l': Bold = true; break;
            case 'o': Italic = true; break;
            case 'n': Underline = true; break;
            case 'm': Strikethrough = true; break;
            case 'k': Obfuscated = true; break;
            case 'r':
                Clear();
                break;
            default:
                Color = MotdFormatting.CodePrefix(code);
                break;
        }
    }

    public void ClearCode(char code)
    {
        switch (char.ToLowerInvariant(code))
        {
            case 'l': Bold = false; break;
            case 'o': Italic = false; break;
            case 'n': Underline = false; break;
            case 'm': Strikethrough = false; break;
            case 'k': Obfuscated = false; break;
            case 'r':
                Clear();
                break;
            default:
                if (HasCode(code))
                    Color = "";
                break;
        }
    }

    public void Clear()
    {
        Color = "";
        Bold = false;
        Italic = false;
        Underline = false;
        Strikethrough = false;
        Obfuscated = false;
    }

    /// <summary>Canonical Java order: obfuscated, bold, strike, underline, italic.</summary>
    public string FormatCodes()
    {
        var sb = new StringBuilder(10);
        if (Obfuscated)
            sb.Append(MotdFormatting.Section).Append('k');
        if (Bold)
            sb.Append(MotdFormatting.Section).Append('l');
        if (Strikethrough)
            sb.Append(MotdFormatting.Section).Append('m');
        if (Underline)
            sb.Append(MotdFormatting.Section).Append('n');
        if (Italic)
            sb.Append(MotdFormatting.Section).Append('o');
        return sb.ToString();
    }
}

/// <summary>
/// Builds a Minecraft MOTD string from plain text plus per-character effects,
/// using as few <c>§</c> codes as Java Edition's rules allow:
/// <list type="bullet">
/// <item><c>§r</c> resets color and all formatting.</item>
/// <item>A color code (vanilla or <c>§x</c> hex) also resets bold/italic/underline/strike/obfuscated.</item>
/// <item><c>§k/l/m/n/o</c> only turn formatting on; the only way to turn one off
/// is to reset (or re-emit a color, which resets formats) and reapply the rest.</item>
/// </list>
/// </summary>
public sealed class MotdBuilder
{
    public string InputText { get; private set; }

    public MotdCharEffects[] InputEffects { get; private set; }

    public MotdBuilder(string? inputText)
    {
        InputText = "";
        InputEffects = [];
        SetInputText(inputText);
    }

    private MotdBuilder(string inputText, MotdCharEffects[] effects)
    {
        InputText = inputText;
        InputEffects = effects;
    }

    /// <summary>
    /// Parse a stored <c>§</c> MOTD field into visible characters (including empty
    /// wrap holes) and the style active on each character.
    /// </summary>
    public static MotdBuilder Parse(string? formatted)
    {
        var s = formatted ?? "";
        var plain = new StringBuilder(s.Length);
        var effects = new List<MotdCharEffects>(s.Length);
        var style = MotdFormatting.MotdStyle.Default;

        for (var i = 0; i < s.Length;)
        {
            if (s[i] == '\r')
            {
                i++;
                continue;
            }

            var next = style;
            if (MotdFormatting.TryApplyCode(s, i, ref next, out var consumed))
            {
                var after = i + consumed;
                if (!MotdFormatting.IsResetCodeAt(s, i) && MotdFormatting.IsResetCodeAt(s, after))
                {
                    plain.Append(MotdFormatting.EditorHole);
                    effects.Add(FromStyle(next));
                    MotdFormatting.TryApplyCode(s, after, ref next, out var resetLen);
                    style = next;
                    i = after + resetLen;
                    continue;
                }

                style = next;
                i = after;
                continue;
            }

            if (s[i] == '\n')
            {
                plain.Append('\n');
                effects.Add(new MotdCharEffects());
                style = MotdFormatting.MotdStyle.Default;
                i++;
                continue;
            }

            plain.Append(s[i]);
            effects.Add(FromStyle(style));
            i++;
        }

        return new MotdBuilder(plain.ToString(), effects.ToArray());
    }

    /// <summary>
    /// Updates the visible text, preserving effects for characters that still
    /// exist at the same index. New characters start unstyled. Newlines never
    /// keep a style (Java list MOTD treats each line independently).
    /// </summary>
    public void SetInputText(string? text)
    {
        text ??= "";
        var newEffects = new MotdCharEffects[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                newEffects[i] = new MotdCharEffects();
                continue;
            }

            newEffects[i] = InputEffects is { Length: > 0 } && i < InputEffects.Length
                ? InputEffects[i]
                : new MotdCharEffects();
        }

        InputText = text;
        InputEffects = newEffects;
    }

    /// <summary>
    /// Always apply <paramref name="code"/> to <c>[start, end)</c> (visible indices).
    /// Empty range inserts a wrap hole so the caret can type with that style.
    /// </summary>
    public MotdBuilder Apply(int start, int end, char code)
    {
        NormalizeRange(ref start, ref end);
        code = char.ToLowerInvariant(code);
        if (!IsEffectCode(code))
            return this;
        if (code == 'r')
        {
            if (start != end)
                ApplyToRange(start, end, e => e.Clear());
            return this;
        }

        if (start == end)
        {
            InsertStyledHole(start, code);
            return this;
        }

        ApplyToRange(start, end, e => e.SetCode(code));
        return this;
    }

    /// <summary>
    /// Apply <paramref name="code"/> to <c>[start, end)</c>, or remove it when every
    /// styled character in the range already has that code. Reset always clears.
    /// Empty range toggles a wrap hole.
    /// </summary>
    public MotdBuilder Toggle(int start, int end, char code)
    {
        NormalizeRange(ref start, ref end);
        code = char.ToLowerInvariant(code);
        if (!IsEffectCode(code))
            return this;
        if (code == 'r')
        {
            if (start != end)
                ApplyToRange(start, end, e => e.Clear());
            return this;
        }

        if (start == end)
        {
            ToggleEmptyHole(start, code);
            return this;
        }

        var allHave = RangeFullyHas(start, end, code);
        ApplyToRange(start, end, e =>
        {
            if (allHave)
                e.ClearCode(code);
            else
                e.SetCode(code);
        });
        return this;
    }

    /// <summary>Compact <c>§</c> string for the current text and effects.</summary>
    public string GenerateCode()
    {
        var sb = new StringBuilder(InputText.Length + 16);
        var current = new MotdCharEffects();

        for (var i = 0; i < InputText.Length; i++)
        {
            var ch = InputText[i];
            if (ch == '\n')
            {
                sb.Append('\n');
                current = new MotdCharEffects();
                continue;
            }

            var target = InputEffects[i];
            if (ch == MotdFormatting.EditorHole)
            {
                AppendHole(sb, current, target);
                current = new MotdCharEffects();
                continue;
            }

            sb.Append(BuildTransition(current, target));
            sb.Append(ch);
            current = target.Clone();
        }

        if (!current.IsDefault)
            sb.Append(MotdFormatting.Section).Append('r');

        return sb.ToString();
    }

    /// <summary>
    /// Codes needed to move Minecraft's active style from <paramref name="current"/>
    /// to <paramref name="target"/>. Re-emitting the target color is preferred over
    /// <c>§r</c> + color when a format is turning off but a color should remain:
    /// a color code already resets formats, so <c>§9D</c> keeps D blue (bare <c>§r</c>
    /// before D would not).
    /// </summary>
    internal static string BuildTransition(MotdCharEffects current, MotdCharEffects target)
    {
        if (current.SameAs(target))
            return "";

        var formatOff =
            (current.Bold && !target.Bold)
            || (current.Italic && !target.Italic)
            || (current.Underline && !target.Underline)
            || (current.Strikethrough && !target.Strikethrough)
            || (current.Obfuscated && !target.Obfuscated);
        var colorChanged = !current.Color.Equals(target.Color, StringComparison.OrdinalIgnoreCase);

        if (formatOff || colorChanged)
        {
            if (target.HasColor)
                return target.Color + target.FormatCodes();
            return MotdFormatting.CodePrefix('r') + target.FormatCodes();
        }

        var added = new StringBuilder(10);
        if (target.Obfuscated && !current.Obfuscated)
            added.Append(MotdFormatting.Section).Append('k');
        if (target.Bold && !current.Bold)
            added.Append(MotdFormatting.Section).Append('l');
        if (target.Strikethrough && !current.Strikethrough)
            added.Append(MotdFormatting.Section).Append('m');
        if (target.Underline && !current.Underline)
            added.Append(MotdFormatting.Section).Append('n');
        if (target.Italic && !current.Italic)
            added.Append(MotdFormatting.Section).Append('o');
        return added.ToString();
    }

    private void ToggleEmptyHole(int index, char code)
    {
        if (index < InputText.Length && InputText[index] == MotdFormatting.EditorHole)
        {
            Toggle(index, index + 1, code);
            if (index < InputText.Length
                && InputText[index] == MotdFormatting.EditorHole
                && InputEffects[index].IsDefault)
            {
                RemoveAt(index);
            }

            return;
        }

        InsertStyledHole(index, code);
    }

    private void InsertStyledHole(int index, char code)
    {
        if (index < InputText.Length && InputText[index] == MotdFormatting.EditorHole)
        {
            InputEffects[index].SetCode(code);
            return;
        }

        var inserted = StyleAtCaret(index);
        inserted.SetCode(code);
        InsertHole(index, inserted);
    }

    /// <summary>
    /// Style a new empty wrap should inherit: the character at the caret, else
    /// the previous character on this line. Otherwise a Bold hole in red text
    /// would drop the color (format codes do not reset color; the hole must
    /// keep it).
    /// </summary>
    private MotdCharEffects StyleAtCaret(int index)
    {
        if (index < InputText.Length && InputText[index] != '\n')
            return InputEffects[index].Clone();
        if (index > 0 && InputText[index - 1] != '\n')
            return InputEffects[index - 1].Clone();
        return new MotdCharEffects();
    }

    /// <summary>
    /// Empty wraps are stored as <c>§codes§r</c> with no visible character so
    /// <see cref="MotdFormatting.ToEditorHtml"/> can place the caret. A hole
    /// must end with a non-reset code immediately before <c>§r</c>.
    /// </summary>
    private static void AppendHole(StringBuilder sb, MotdCharEffects current, MotdCharEffects target)
    {
        var trans = BuildTransition(current, target);
        sb.Append(trans);
        if (!EndsWithNonResetCode(trans))
            sb.Append(HoleMarker(target));
        sb.Append(MotdFormatting.Section).Append('r');
    }

    private static bool EndsWithNonResetCode(string trans)
    {
        if (trans.Length < 2)
            return false;
        var code = char.ToLowerInvariant(trans[^1]);
        return trans[^2] == MotdFormatting.Section && code != 'r';
    }

    private static string HoleMarker(MotdCharEffects target)
    {
        if (target.HasColor)
            return target.Color;
        var formats = target.FormatCodes();
        return formats.Length >= 2 ? formats[..2] : "";
    }

    private void ApplyToRange(int start, int end, Action<MotdCharEffects> edit)
    {
        for (var i = start; i < end; i++)
        {
            if (InputText[i] == '\n')
                continue;
            edit(InputEffects[i]);
        }
    }

    private bool RangeFullyHas(int start, int end, char code)
    {
        var any = false;
        for (var i = start; i < end; i++)
        {
            if (InputText[i] == '\n')
                continue;
            any = true;
            if (!InputEffects[i].HasCode(code))
                return false;
        }

        return any;
    }

    private void InsertHole(int index, MotdCharEffects effects)
    {
        index = Math.Clamp(index, 0, InputText.Length);
        var text = InputText.Insert(index, MotdFormatting.EditorHole.ToString());
        var next = new MotdCharEffects[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            if (i == index)
                next[i] = effects;
            else
                next[i] = InputEffects[i < index ? i : i - 1];
        }

        InputText = text;
        InputEffects = next;
    }

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= InputText.Length)
            return;
        InputText = InputText.Remove(index, 1);
        var next = new MotdCharEffects[InputText.Length];
        for (var i = 0; i < InputText.Length; i++)
            next[i] = InputEffects[i < index ? i : i + 1];
        InputEffects = next;
    }

    private void NormalizeRange(ref int start, ref int end)
    {
        var lo = Math.Clamp(Math.Min(start, end), 0, InputText.Length);
        var hi = Math.Clamp(Math.Max(start, end), 0, InputText.Length);
        start = lo;
        end = hi;
    }

    private static bool IsEffectCode(char code) =>
        code is >= '0' and <= '9'
        or >= 'a' and <= 'f'
        or 'k' or 'l' or 'm' or 'n' or 'o' or 'r';

    private static MotdCharEffects FromStyle(MotdFormatting.MotdStyle style) => new()
    {
        Color = style.ColorEmit ?? "",
        Bold = style.Bold,
        Italic = style.Italic,
        Underline = style.Underline,
        Strikethrough = style.Strike,
        Obfuscated = style.Obf,
    };
}
