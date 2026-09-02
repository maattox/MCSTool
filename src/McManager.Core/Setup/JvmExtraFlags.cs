namespace McManager.Core.Setup;

/// <summary>
/// Non-heap JVM flag textarea: whitespace or newline separated. Heap stays on the Danger radios.
/// </summary>
public static class JvmExtraFlags
{
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var outList = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var s = raw.Trim();
            if (s.Length == 0 || IsHeapToken(s))
                continue;
            if (seen.Add(s))
                outList.Add(s);
        }

        return outList;
    }

    public static string Format(IEnumerable<string> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        return string.Join("\n", Parse(string.Join('\n', flags)));
    }

    public static bool ContainedHeapTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        foreach (var raw in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (IsHeapToken(raw.Trim()))
                return true;
        }

        return false;
    }

    public static bool IsHeapToken(string token) =>
        token.StartsWith("-Xms", StringComparison.Ordinal)
        || token.StartsWith("-Xmx", StringComparison.Ordinal);
}
