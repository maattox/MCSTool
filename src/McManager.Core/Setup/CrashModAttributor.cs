using System.Text.RegularExpressions;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Blueprint §24.3 Layer 3: only the loader's own "problem mod" report counts.
/// Mixin / stack-trace guesses are not enough to quarantine. Zero or several
/// implicated mods → no automatic action.
/// </summary>
public static class CrashModAttributor
{
    public const string Reason = "crash_attributed_by_loader_report";

    private static readonly HashSet<string> IgnoredIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "minecraft", "java", "mcp", "fml", "forge", "neoforge",
        "fabricloader", "fabric-loader", "fabric", "quilt_loader", "quilt-loader",
        "mixin", "mixins", "unspecified",
    };

    private static readonly Regex CausedHeader = new(
        @"the following mods? caused the (?:server|game) to crash\s*:",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ListItem = new(
        @"^[ \t]*[-–—*•]*[ \t]*([A-Za-z][A-Za-z0-9_.-]{1,80})(?:[ \t]+[^:\r\n]*)?$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProvidedBy = new(
        @"provided by ['""]([^'""]+)['""]",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModFile = new(
        @"Mod File:\s*(?:[A-Za-z]:)?[^\r\n]*[\\/]mods[\\/]([^\\/\r\n]+\.jar)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AffectedHeader = new(
        @"--\s*Affected mods\s*--",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AffectedItem = new(
        @"^[ \t]+([A-Za-z][A-Za-z0-9_.-]{1,80})\s*:",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// Returns a single blamed mod when the loader report names exactly one
    /// (plus an optional jar filename from a Mod File line). Otherwise null.
    /// </summary>
    public static CrashModBlame? TryExactlyOne(string? journal, string? crashReport = null)
    {
        var text = Join(journal, crashReport);
        if (text.Length == 0)
            return null;

        var fromList = CollectCausedList(text);
        var fromProvided = CollectProvidedBy(text);
        var fromAffected = CollectAffectedMods(text);
        var jarNames = CollectModFileNames(text);

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        UnionIfSingleSource(ids, fromList);
        if (ids.Count == 0)
            UnionIfSingleSource(ids, fromProvided);
        else if (fromProvided.Count > 0 && !SetsEqual(ids, fromProvided))
            return null;

        if (ids.Count == 0)
            UnionIfSingleSource(ids, fromAffected);
        else if (fromAffected.Count > 0 && !SetsEqual(ids, fromAffected))
            return null;

        if (ids.Count == 0 && jarNames.Count == 1)
        {
            var jar = jarNames.First();
            if (!ServerModsInspect.IsSafeFileName(jar))
                return null;
            return new CrashModBlame(Stem(jar), jar);
        }

        if (ids.Count != 1)
            return null;

        var modId = ids.First();
        string? jarHint = null;
        if (jarNames.Count == 1)
        {
            var jar = jarNames.First();
            if (ServerModsInspect.IsSafeFileName(jar))
                jarHint = jar;
        }

        return new CrashModBlame(modId, jarHint);
    }

    public static string? TryFindUniqueJar(
        CrashModBlame blame,
        IReadOnlyList<string> fileNames)
    {
        ArgumentNullException.ThrowIfNull(blame);
        var names = fileNames ?? [];
        if (!string.IsNullOrWhiteSpace(blame.JarFileName))
        {
            foreach (var name in names)
            {
                if (name.Equals(blame.JarFileName, StringComparison.OrdinalIgnoreCase)
                    && ServerModsInspect.IsSafeFileName(name))
                {
                    return name;
                }
            }
        }

        var hits = new List<string>();
        foreach (var name in names)
        {
            if (!ServerModsInspect.IsSafeFileName(name))
                continue;
            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                continue;
            var candidate = ExcludeIncludeMatcher.Candidate.From("mods/" + name, projectSlug: null);
            if (ExcludeIncludeMatcher.TermMatches(blame.ModId, candidate))
                hits.Add(name);
        }

        return hits.Count == 1 ? hits[0] : null;
    }

    private static void UnionIfSingleSource(HashSet<string> dest, HashSet<string> source)
    {
        if (source.Count == 0)
            return;
        foreach (var id in source)
            dest.Add(id);
    }

    private static bool SetsEqual(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var id in a)
        {
            if (!b.Contains(id))
                return false;
        }

        return true;
    }

    private static HashSet<string> CollectCausedList(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var match = CausedHeader.Match(text);
        if (!match.Success)
            return ids;

        var rest = text[match.Index..];
        var lines = rest.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
            if (line.Length == 0)
                break;
            if (line.StartsWith('[')
                || line.Contains("Exception", StringComparison.Ordinal)
                || line.Contains("Mod File:", StringComparison.OrdinalIgnoreCase)
                || line.TrimStart().StartsWith("at ", StringComparison.Ordinal)
                || line.StartsWith("Caused by", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var item = ListItem.Match(line);
            if (!item.Success)
                break;
            AddId(ids, item.Groups[1].Value);
        }

        return ids;
    }

    private static HashSet<string> CollectProvidedBy(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ProvidedBy.Matches(text))
            AddId(ids, match.Groups[1].Value);
        return ids;
    }

    private static HashSet<string> CollectAffectedMods(string text)
    {
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var header = AffectedHeader.Match(text);
        if (!header.Success)
            return ids;

        var rest = text[header.Index..];
        var lines = rest.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length == 0)
                break;
            if (line.StartsWith("--", StringComparison.Ordinal))
                break;
            var item = AffectedItem.Match(line);
            if (!item.Success)
                continue;
            AddId(ids, item.Groups[1].Value);
        }

        return ids;
    }

    private static HashSet<string> CollectModFileNames(string text)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in ModFile.Matches(text))
        {
            var name = Path.GetFileName(match.Groups[1].Value.Replace('\\', '/'));
            if (ServerModsInspect.IsSafeFileName(name))
                names.Add(name);
        }

        return names;
    }

    private static void AddId(HashSet<string> ids, string raw)
    {
        var id = (raw ?? "").Trim();
        if (id.Length == 0 || IgnoredIds.Contains(id))
            return;
        ids.Add(id);
    }

    private static string Stem(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var cut = name.IndexOf('-');
        return cut > 0 ? name[..cut] : name;
    }

    private static string Join(string? journal, string? crashReport)
    {
        var a = journal ?? "";
        var b = crashReport ?? "";
        if (a.Length == 0)
            return b;
        if (b.Length == 0)
            return a;
        return a + "\n" + b;
    }
}

public sealed record CrashModBlame(string ModId, string? JarFileName);
