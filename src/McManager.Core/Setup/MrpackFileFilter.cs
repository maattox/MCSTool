using System.Text;

namespace McManager.Core.Setup;

/// <summary>
/// Combine pack <c>env.server</c> with the itzg/product exclude lists (blueprint §22.1 + §24.3).
/// Matcher runs after reading the pack declaration (robustness R2).
/// </summary>
/// <remarks>
/// Precedence:
/// <list type="number">
/// <item>Force-include (Layer 2 then Layer 1) keeps the file even when <c>env.server == unsupported</c>.</item>
/// <item>Pack <c>unsupported</c> skips as <see cref="PackFileSkipReason.PackDeclared"/> (even if the list would also exclude it).</item>
/// <item>List exclude skips required/optional/unclear as <see cref="PackFileSkipReason.OverrideList"/>.</item>
/// <item>Required/optional with no list match install.</item>
/// <item>Still-unclear <c>env.server</c> stays unclear (install must fail; do not guess).</item>
/// </list>
/// </remarks>
internal static class MrpackFileFilter
{
    public enum Action
    {
        Install,
        SkipPackDeclared,
        SkipOverrideList,
        Unclear,
    }

    public static Action Decide(string? serverEnv, ExcludeIncludeMatch match)
    {
        var env = (serverEnv ?? "").Trim();
        if (match.Keep)
            return Action.Install;

        if (env.Equals(MrpackAnalyzer.EnvUnsupported, StringComparison.OrdinalIgnoreCase))
            return Action.SkipPackDeclared;

        if (match.Exclude)
            return Action.SkipOverrideList;

        if (env.Equals(MrpackAnalyzer.EnvRequired, StringComparison.OrdinalIgnoreCase)
            || env.Equals(MrpackAnalyzer.EnvOptional, StringComparison.OrdinalIgnoreCase))
        {
            return Action.Install;
        }

        return Action.Unclear;
    }

    /// <summary>
    /// First itzg/product <c>modpacks</c> key that matches a guessed slug; otherwise null (silent).
    /// </summary>
    public static string? ResolvePackSlug(
        ExcludeIncludeMatcher matcher,
        string? packName,
        string? versionId,
        string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        foreach (var candidate in SlugCandidates(packName, versionId, sourceName))
        {
            if (matcher.Layer1.TryGetPack(candidate, out _)
                || matcher.Layer2.TryGetPack(candidate, out _))
            {
                return candidate;
            }
        }

        return null;
    }

    internal static IEnumerable<string> SlugCandidates(
        string? packName,
        string? versionId,
        string? sourceName)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in new[] { packName, versionId, FileStem(sourceName) })
        {
            foreach (var candidate in Expand(raw))
            {
                if (seen.Add(candidate))
                    yield return candidate;
            }
        }
    }

    public static bool IsJarPath(string relativePath)
    {
        var n = (relativePath ?? "").Replace('\\', '/').Trim();
        return n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldSkipOverrideJar(
        string relativePath,
        ExcludeIncludeMatcher matcher,
        string? packSlug,
        ISet<string> skippedIndexPaths)
    {
        if (!IsJarPath(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/').Trim();
        if (skippedIndexPaths.Contains(normalized))
            return true;

        var fileName = Path.GetFileName(normalized);
        foreach (var skipped in skippedIndexPaths)
        {
            if (string.Equals(Path.GetFileName(skipped.Replace('\\', '/')), fileName, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return matcher.Match(packSlug, normalized).Exclude;
    }

    private static string? FileStem(string? sourceName)
    {
        if (string.IsNullOrWhiteSpace(sourceName))
            return null;
        var name = Path.GetFileName(sourceName.Trim());
        if (name.EndsWith(".mrpack", StringComparison.OrdinalIgnoreCase))
            name = name[..^".mrpack".Length];
        else if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            name = name[..^".zip".Length];
        return string.IsNullOrWhiteSpace(name) ? null : name;
    }

    private static IEnumerable<string> Expand(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            yield break;

        var trimmed = raw.Trim();
        yield return trimmed;

        var kebab = ToKebab(trimmed);
        if (!string.IsNullOrEmpty(kebab) && !kebab.Equals(trimmed, StringComparison.OrdinalIgnoreCase))
            yield return kebab;
    }

    internal static string ToKebab(string value)
    {
        var sb = new StringBuilder(value.Length);
        var pendingHyphen = false;
        foreach (var c in value.Trim())
        {
            if (char.IsLetterOrDigit(c))
            {
                if (pendingHyphen && sb.Length > 0)
                    sb.Append('-');
                pendingHyphen = false;
                sb.Append(char.ToLowerInvariant(c));
            }
            else if (sb.Length > 0)
            {
                pendingHyphen = true;
            }
        }

        return sb.ToString();
    }
}
