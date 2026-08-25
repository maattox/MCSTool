using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace McManager.Core.Services;

/// <summary>
/// Minecraft list MOTD helpers: paste normalize, selection wrap, line metrics, <c>§</c> preview.
/// Hex / <c>§x</c> is best-effort (Paper/Spigot 1.16+); Vanilla/Forge/Fabric ignore it.
/// </summary>
public static class MotdFormatting
{
    public const char Section = '\u00A7';

    public readonly record struct MotdColor(char Code, string Name, string Hex);

    public readonly record struct MotdFormat(char Code, string Name, string IconClass);

    public readonly record struct MotdRun(
        string Text,
        string ColorHex,
        bool Bold,
        bool Italic,
        bool Underline,
        bool Strikethrough,
        bool Obfuscated);

    /// <summary>Result of wrapping a <c>[start, end)</c> span with a code and <c>§r</c>.</summary>
    public readonly record struct MotdWrapResult(string Text, int InnerStart, int InnerEnd);

    /// <summary>Per-line visible length vs the Java server-list cap (59).</summary>
    public readonly record struct MotdLineMetric(
        int LineNumber,
        int Used,
        int Limit,
        bool TooLong,
        string Label);

    /// <summary>Java Edition server-list visible characters per MOTD line.</summary>
    public const int ListLineVisibleLimit = 59;

    /// <summary>Zero-width caret target for an empty wrap hole (<c>§code§r</c>).</summary>
    public const char EditorHole = '\u200B';

    public static readonly IReadOnlyList<MotdColor> VanillaColors =
    [
        new('0', "Black", "#000000"),
        new('1', "Dark Blue", "#0000AA"),
        new('2', "Dark Green", "#00AA00"),
        new('3', "Dark Aqua", "#00AAAA"),
        new('4', "Dark Red", "#AA0000"),
        new('5', "Dark Purple", "#AA00AA"),
        new('6', "Gold", "#FFAA00"),
        new('7', "Gray", "#AAAAAA"),
        new('8', "Dark Gray", "#555555"),
        new('9', "Blue", "#5555FF"),
        new('a', "Green", "#55FF55"),
        new('b', "Aqua", "#55FFFF"),
        new('c', "Red", "#FF5555"),
        new('d', "Light Purple", "#FF55FF"),
        new('e', "Yellow", "#FFFF55"),
        new('f', "White", "#FFFFFF"),
    ];

    public static readonly IReadOnlyList<MotdFormat> Formats =
    [
        new('l', "Bold", "ti-bold"),
        new('o', "Italic", "ti-italic"),
        new('n', "Underline", "ti-underline"),
        new('m', "Strikethrough", "ti-strikethrough"),
        new('k', "Obfuscated", "ti-shuffle"),
        new('r', "Reset", "ti-clear-all"),
    ];

    private static readonly Dictionary<char, string> ColorByCode = VanillaColors
        .ToDictionary(c => c.Code, c => c.Hex);

    private static readonly Dictionary<string, char> CodeByHex = VanillaColors
        .ToDictionary(c => c.Hex, c => c.Code, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex UnicodeSection = new(
        @"\\u00a7",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HexHash = new(
        @"&#([0-9A-Fa-f]{6})",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex AmpCode = new(
        @"(?<!&)[&]([0-9a-fk-orxA-FK-ORX])",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Accept a fadehost / <c>server.properties</c> dump: strip <c>motd=</c>, decode
    /// <c>\u00a7</c> and <c>\n</c>, map <c>&amp;</c> codes and <c>&amp;#RRGGBB</c> to <c>§</c>.
    /// </summary>
    public static string NormalizePaste(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "";

        var s = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var look = s.TrimStart();
        var hadMotdPrefix = look.StartsWith("motd=", StringComparison.OrdinalIgnoreCase);
        if (hadMotdPrefix)
        {
            var idx = s.IndexOf("motd=", StringComparison.OrdinalIgnoreCase);
            s = idx >= 0 ? s[(idx + 5)..] : s;
        }

        var hadUnicode = UnicodeSection.IsMatch(s);
        var hadSection = s.Contains(Section);
        s = UnicodeSection.Replace(s, Section.ToString());
        s = HexHash.Replace(s, m => ToSectionHex(m.Groups[1].Value));
        if (hadMotdPrefix || hadUnicode || hadSection || LooksLikeAmpMotd(s.TrimStart()))
            s = AmpCode.Replace(s, m => Section + m.Groups[1].Value.ToLowerInvariant());
        s = s.Replace("\\n", "\n", StringComparison.Ordinal);
        return s;
    }

    private static bool LooksLikeAmpMotd(string s) =>
        s.Length >= 2 && s[0] == '&' && "0123456789abcdefklmnorx".Contains(char.ToLowerInvariant(s[1]));

    public static string CodePrefix(char code) => Section + char.ToLowerInvariant(code).ToString();

    public static string HexPrefix(string hex6)
    {
        var h = (hex6 ?? "").Trim().TrimStart('#');
        if (h.Length != 6 || !h.All(IsHexDigit))
            return "";
        return ToSectionHex(h);
    }

    /// <summary>
    /// Wrap <c>[start, end)</c> with a vanilla color/format code and close with <c>§r</c>,
    /// restoring the outer color/format so following text is unchanged. Empty range:
    /// insert <c>§code</c> + <c>§r</c> (plus restore) with the caret between them
    /// (<see cref="MotdWrapResult.InnerStart"/>).
    /// </summary>
    public static MotdWrapResult WrapSpan(string? text, int start, int end, char code)
    {
        var s = text ?? "";
        var lo = Math.Clamp(Math.Min(start, end), 0, s.Length);
        var hi = Math.Clamp(Math.Max(start, end), 0, s.Length);
        var c = char.ToLowerInvariant(code);
        if (!IsWrapCode(c))
            return new MotdWrapResult(s, lo, hi);

        var prefix = CodePrefix(c);
        var suffix = Section + "r" + RestoreCodes(StyleAt(s, lo));
        var inner = s[lo..hi];
        var result = s[..lo] + prefix + inner + suffix + s[hi..];
        var innerStart = lo + prefix.Length;
        return new MotdWrapResult(result, innerStart, innerStart + inner.Length);
    }

    /// <summary>
    /// Visible characters on one MOTD line. Ignores <c>§</c> / <c>&amp;</c> codes and
    /// <c>§x</c> hex runs.
    /// </summary>
    public static int VisibleLength(string? line)
    {
        if (string.IsNullOrEmpty(line))
            return 0;
        var n = 0;
        foreach (var run in ParseLine(line))
            n += run.Text.Length;
        return n;
    }

    /// <summary>
    /// Visible characters only (codes and hex runs stripped). Newlines are kept.
    /// Empty wrap holes are not included.
    /// </summary>
    public static string VisibleText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var sb = new StringBuilder();
        foreach (var line in SplitFieldLines(text))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            foreach (var run in ParseLine(line))
                sb.Append(run.Text);
        }

        return sb.ToString();
    }

    /// <summary>
    /// Map a visible caret/selection index (codes skipped; empty <c>§code§r</c>
    /// holes count as one slot) onto a raw <c>§</c> string index.
    /// </summary>
    public static int VisibleToRaw(string? text, int visibleIndex)
    {
        var s = text ?? "";
        var vis = 0;
        var i = 0;
        var target = Math.Max(0, visibleIndex);
        var style = MotdStyle.Default;
        while (i < s.Length)
        {
            var isCode = TryApplyCode(s, i, ref style, out var consumed);
            var next = i + consumed;
            var isHole = isCode && !IsResetCodeAt(s, i) && IsResetCodeAt(s, next);

            if (vis == target)
            {
                if (isHole)
                    return next;
                if (isCode && !IsResetCodeAt(s, i))
                {
                    i = next;
                    continue;
                }

                return i;
            }

            if (isHole)
            {
                vis++;
                i = next;
                if (IsResetCodeAt(s, i))
                    i += 2;
                continue;
            }

            if (isCode)
            {
                i = next;
                continue;
            }

            vis++;
            i++;
        }

        return s.Length;
    }

    /// <summary>
    /// Map a raw <c>§</c> index onto a visible caret index (codes skipped;
    /// empty wrap holes count as one slot).
    /// </summary>
    public static int RawToVisible(string? text, int rawIndex)
    {
        var s = text ?? "";
        var vis = 0;
        var i = 0;
        var limit = Math.Clamp(rawIndex, 0, s.Length);
        var style = MotdStyle.Default;
        while (i < s.Length && i < limit)
        {
            if (TryApplyCode(s, i, ref style, out var consumed))
            {
                var next = i + consumed;
                if (!IsResetCodeAt(s, i) && IsResetCodeAt(s, next) && next < limit)
                    vis++;
                i = next;
                continue;
            }

            vis++;
            i++;
        }

        return vis;
    }

    public static string FormatLineCounter(int lineNumber, int used)
    {
        var label = $"line {lineNumber}: {used}/{ListLineVisibleLimit}";
        if (used > ListLineVisibleLimit)
            label += " — too long";
        return label;
    }

    public static IReadOnlyList<string> SplitListLines(string? motdPropertiesValue)
    {
        var text = motdPropertiesValue ?? "";
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n');
    }

    public static IReadOnlyList<MotdLineMetric> MeasureListLines(string? motdPropertiesValue)
    {
        var lines = SplitListLines(motdPropertiesValue);
        var metrics = new MotdLineMetric[lines.Count];
        for (var i = 0; i < lines.Count; i++)
        {
            var used = VisibleLength(lines[i]);
            metrics[i] = new MotdLineMetric(
                i + 1,
                used,
                ListLineVisibleLimit,
                used > ListLineVisibleLimit,
                FormatLineCounter(i + 1, used));
        }

        return metrics;
    }

    /// <summary>
    /// Per-line counters for the combined list MOTD from
    /// <see cref="ServerIdentityUx.BuildMotd"/>.
    /// </summary>
    public static IReadOnlyList<MotdLineMetric> MeasureIdentityLines(
        string? serverName,
        string? description,
        bool omitName = false) =>
        MeasureListLines(ServerIdentityUx.BuildMotd(serverName, description, omitName));

    /// <summary>True when the MOTD can be written as a single <c>server.properties</c> line.</summary>
    public static bool IsSafePropertiesValue(string motd) =>
        motd.IndexOf('\n') < 0 && motd.IndexOf('\r') < 0;

    public static IReadOnlyList<IReadOnlyList<MotdRun>> ToPreviewLines(string? motdPropertiesValue)
    {
        var text = motdPropertiesValue ?? "";
        var lines = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Split('\n');
        var result = new IReadOnlyList<MotdRun>[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            result[i] = ParseLine(lines[i]);
        return result;
    }

    public static string ToPreviewHtml(string? motdPropertiesValue)
    {
        var sb = new StringBuilder();
        foreach (var line in ToPreviewLines(motdPropertiesValue))
        {
            sb.Append("<span class=\"mcm-motd-line\">");
            if (line.Count == 0)
            {
                sb.Append("&nbsp;");
            }
            else
            {
                foreach (var run in line)
                    AppendRun(sb, run);
            }

            sb.Append("</span>");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Contenteditable HTML for a stored MOTD field. Codes are not shown; runs
    /// carry <c>data-motd-*</c> so the browser can serialize back to <c>§</c>.
    /// Empty wrap holes emit a zero-width caret target.
    /// </summary>
    public static string ToEditorHtml(string? fieldText)
    {
        var sb = new StringBuilder();
        var lines = SplitFieldLines(fieldText);
        foreach (var line in lines)
        {
            sb.Append("<span class=\"mcm-motd-line\">");
            var runs = ParseEditorLine(line);
            if (runs.Count == 0)
            {
                sb.Append("<br>");
            }
            else
            {
                foreach (var run in runs)
                    AppendEditorRun(sb, run);
            }

            sb.Append("</span>");
        }

        return sb.ToString();
    }

    private static IReadOnlyList<string> SplitFieldLines(string? text)
    {
        var s = text ?? "";
        return s
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
    }

    public static IReadOnlyList<MotdRun> ParseLine(string line)
    {
        var runs = new List<MotdRun>();
        var buf = new StringBuilder();
        var style = MotdStyle.Default;

        void Flush()
        {
            if (buf.Length == 0)
                return;
            runs.Add(new MotdRun(
                buf.ToString(),
                style.ColorHex,
                style.Bold,
                style.Italic,
                style.Underline,
                style.Strike,
                style.Obf));
            buf.Clear();
        }

        for (var i = 0; i < line.Length; i++)
        {
            if (TryApplyCode(line, i, ref style, out var consumed))
            {
                Flush();
                i += consumed - 1;
                continue;
            }

            buf.Append(line[i]);
        }

        Flush();
        return runs;
    }

    private static IReadOnlyList<MotdRun> ParseEditorLine(string line)
    {
        var runs = new List<MotdRun>();
        var buf = new StringBuilder();
        var style = MotdStyle.Default;

        void Flush()
        {
            if (buf.Length == 0)
                return;
            runs.Add(ToRun(buf.ToString(), style));
            buf.Clear();
        }

        for (var i = 0; i < line.Length; i++)
        {
            if (TryApplyCode(line, i, ref style, out var consumed))
            {
                Flush();
                var next = i + consumed;
                if (!IsResetCodeAt(line, i) && IsResetCodeAt(line, next))
                    runs.Add(ToRun(EditorHole.ToString(), style));
                i += consumed - 1;
                continue;
            }

            buf.Append(line[i]);
        }

        Flush();
        return runs;
    }

    private static MotdRun ToRun(string text, MotdStyle style) =>
        new(text, style.ColorHex, style.Bold, style.Italic, style.Underline, style.Strike, style.Obf);

    private static bool IsResetCodeAt(string text, int i) =>
        i + 1 < text.Length && IsSection(text[i]) && char.ToLowerInvariant(text[i + 1]) == 'r';

    private static bool IsWrapCode(char code) =>
        ColorByCode.ContainsKey(code) || code is 'l' or 'o' or 'n' or 'm' or 'k' or 'r';

    private static MotdStyle StyleAt(string text, int index)
    {
        var style = MotdStyle.Default;
        var limit = Math.Clamp(index, 0, text.Length);
        for (var i = 0; i < limit; i++)
        {
            if (text[i] == '\n')
            {
                style = MotdStyle.Default;
                continue;
            }

            if (TryApplyCode(text, i, ref style, out var consumed))
                i += consumed - 1;
        }

        return style;
    }

    private static string RestoreCodes(MotdStyle style)
    {
        if (style.IsDefault)
            return "";

        var sb = new StringBuilder();
        if (!style.ColorHex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase))
        {
            if (CodeByHex.TryGetValue(style.ColorHex, out var code))
                sb.Append(Section).Append(code);
            else
                sb.Append(HexPrefix(style.ColorHex.TrimStart('#')));
        }

        if (style.Bold)
            sb.Append(Section).Append('l');
        if (style.Italic)
            sb.Append(Section).Append('o');
        if (style.Underline)
            sb.Append(Section).Append('n');
        if (style.Strike)
            sb.Append(Section).Append('m');
        if (style.Obf)
            sb.Append(Section).Append('k');
        return sb.ToString();
    }

    private static bool TryApplyCode(string line, int i, ref MotdStyle style, out int consumed)
    {
        consumed = 0;
        if (i + 1 >= line.Length)
            return false;
        var ch = line[i];
        if (ch != Section && ch != '&')
            return false;

        var code = char.ToLowerInvariant(line[i + 1]);
        if (code == 'x' && TryReadHexColor(line, i, out var hex, out consumed))
        {
            style.ColorHex = hex;
            style.ResetFormats();
            return true;
        }

        if (ColorByCode.TryGetValue(code, out var nextColor))
        {
            style.ColorHex = nextColor;
            style.ResetFormats();
            consumed = 2;
            return true;
        }

        if (code is 'l' or 'o' or 'n' or 'm' or 'k' or 'r')
        {
            switch (code)
            {
                case 'l': style.Bold = true; break;
                case 'o': style.Italic = true; break;
                case 'n': style.Underline = true; break;
                case 'm': style.Strike = true; break;
                case 'k': style.Obf = true; break;
                case 'r':
                    style = MotdStyle.Default;
                    break;
            }

            consumed = 2;
            return true;
        }

        return false;
    }

    private struct MotdStyle
    {
        public string ColorHex;
        public bool Bold;
        public bool Italic;
        public bool Underline;
        public bool Strike;
        public bool Obf;

        public static MotdStyle Default => new() { ColorHex = "#FFFFFF" };

        public readonly bool IsDefault =>
            ColorHex.Equals("#FFFFFF", StringComparison.OrdinalIgnoreCase)
            && !Bold && !Italic && !Underline && !Strike && !Obf;

        public void ResetFormats()
        {
            Bold = false;
            Italic = false;
            Underline = false;
            Strike = false;
            Obf = false;
        }
    }

    private static bool TryReadHexColor(string line, int at, out string hex, out int consumed)
    {
        hex = "#FFFFFF";
        consumed = 0;
        // §x§R§R§G§G§B§B  (14 chars) or §xRRGGBB (8 chars)
        if (at + 13 < line.Length && IsSection(line[at + 2]))
        {
            var nibbles = new char[6];
            var p = at + 2;
            for (var n = 0; n < 6; n++)
            {
                if (p + 1 >= line.Length || !IsSection(line[p]) || !IsHexDigit(line[p + 1]))
                    return false;
                nibbles[n] = line[p + 1];
                p += 2;
            }

            hex = "#" + new string(nibbles);
            consumed = p - at;
            return true;
        }

        if (at + 7 < line.Length)
        {
            var compact = line.Substring(at + 2, 6);
            if (compact.All(IsHexDigit))
            {
                hex = "#" + compact;
                consumed = 8;
                return true;
            }
        }

        return false;
    }

    private static bool IsSection(char ch) => ch == Section || ch == '&';

    private static bool IsHexDigit(char ch) =>
        (uint)ch <= 127 && char.IsAsciiHexDigit(ch);

    private static string ToSectionHex(string hex6)
    {
        var h = hex6.ToLowerInvariant();
        var sb = new StringBuilder(14);
        sb.Append(Section).Append('x');
        foreach (var nibble in h)
            sb.Append(Section).Append(nibble);
        return sb.ToString();
    }

    private static void AppendRun(StringBuilder sb, MotdRun run) =>
        AppendRun(sb, run, editor: false);

    private static void AppendEditorRun(StringBuilder sb, MotdRun run) =>
        AppendRun(sb, run, editor: true);

    private static void AppendRun(StringBuilder sb, MotdRun run, bool editor)
    {
        sb.Append("<span class=\"mcm-motd-run");
        if (run.Obfuscated)
            sb.Append(" mcm-motd-obf");
        sb.Append("\" style=\"color:");
        sb.Append(WebUtility.HtmlEncode(run.ColorHex));
        sb.Append(';');
        if (run.Bold)
            sb.Append("font-weight:700;");
        if (run.Italic)
            sb.Append("font-style:italic;");
        var deco = Decorations(run);
        if (deco.Length > 0)
        {
            sb.Append("text-decoration:");
            sb.Append(deco);
            sb.Append(';');
        }

        if (editor)
        {
            sb.Append("\" data-motd-color=\"");
            sb.Append(WebUtility.HtmlEncode(run.ColorHex));
            sb.Append('"');
            if (run.Bold)
                sb.Append(" data-motd-b=\"1\"");
            if (run.Italic)
                sb.Append(" data-motd-i=\"1\"");
            if (run.Underline)
                sb.Append(" data-motd-u=\"1\"");
            if (run.Strikethrough)
                sb.Append(" data-motd-s=\"1\"");
            if (run.Obfuscated)
                sb.Append(" data-motd-k=\"1\"");
            sb.Append('>');
        }
        else
        {
            sb.Append("\">");
        }

        sb.Append(WebUtility.HtmlEncode(run.Text));
        sb.Append("</span>");
    }

    private static string Decorations(MotdRun run)
    {
        if (run.Underline && run.Strikethrough)
            return "underline line-through";
        if (run.Underline)
            return "underline";
        if (run.Strikethrough)
            return "line-through";
        return "";
    }
}
