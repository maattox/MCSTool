using System.Text;

namespace McManager.PackTestHarness;

internal static class YamlText
{
    public static string Unquote(string raw)
    {
        var t = (raw ?? "").Trim();
        if (t.Length >= 2)
        {
            if (t[0] == '"' && t[^1] == '"')
                return UnescapeDouble(t[1..^1]);
            if (t[0] == '\'' && t[^1] == '\'')
                return t[1..^1].Replace("''", "'", StringComparison.Ordinal);
        }

        return t;
    }

    public static string Quote(string? value)
    {
        var s = value ?? "";
        if (s.Length == 0)
            return "\"\"";
        if (NeedsDoubleQuotes(s))
        {
            var sb = new StringBuilder(s.Length + 8);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: sb.Append(c); break;
                }
            }

            sb.Append('"');
            return sb.ToString();
        }

        return s;
    }

    public static string OneLine(string? message, int maxChars = 240)
    {
        var t = (message ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (t.Contains("  ", StringComparison.Ordinal))
            t = t.Replace("  ", " ", StringComparison.Ordinal);
        if (t.Length > maxChars)
            t = t[..maxChars].TrimEnd() + "…";
        return t;
    }

    private static bool NeedsDoubleQuotes(string s)
    {
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1]))
            return true;
        foreach (var c in s)
        {
            if (c is ':' or '#' or '"' or '\'' or '[' or ']' or '{' or '}' or ',' or '\n' or '\r'
                || c == '&' || c == '*' || c == '!' || c == '|' || c == '>' || c == '%' || c == '@' || c == '`')
                return true;
        }

        return false;
    }

    private static string UnescapeDouble(string inner)
    {
        var sb = new StringBuilder(inner.Length);
        for (var i = 0; i < inner.Length; i++)
        {
            if (inner[i] == '\\' && i + 1 < inner.Length)
            {
                sb.Append(inner[i + 1] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    '"' => '"',
                    '\\' => '\\',
                    var c => c,
                });
                i++;
                continue;
            }

            sb.Append(inner[i]);
        }

        return sb.ToString();
    }
}
