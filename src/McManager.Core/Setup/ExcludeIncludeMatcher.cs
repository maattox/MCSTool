using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>
/// Offline keep/exclude from itzg Layer 1 lists plus the product Layer 2 overlay.
/// Does not apply pack <c>env.server</c> or in-jar side (R2/R3). No HTTP.
/// </summary>
/// <remarks>
/// Matching rule (itzg <c>FileInclusionCalculator</c> + <c>MultiMatcher</c>,
/// https://github.com/itzg/mc-image-helper main), with a product token-boundary
/// guard so short slugs cannot strip unrelated jars:
/// <list type="bullet">
/// <item>Case-insensitive. Path backslashes become slashes when the path has no forward slash.</item>
/// <item>A term wrapped in <c>/.../</c> is a regex <c>Matcher.find()</c> against the lowered path.</item>
/// <item>Any other term is a case-insensitive substring that is not a suffix of a longer
/// alphabetic word (so <c>ding</c> matches <c>ding-1.20.jar</c> but not <c>mob_grinding_utils</c>;
/// prefix class names like <c>titlebar</c> still match <c>titlebarchanger</c>).</item>
/// <item>Within a layer, global and per-pack terms are merged; force-include is checked before exclude
/// (itzg <c>includeModFile</c>).</item>
/// </list>
/// Product extras (lists mix slugs, filename stems, and display names):
/// also test the filename, optional project slug, and a collapsed form that strips
/// spaces / hyphens / underscores so <c>Cull Less Leaves</c> matches <c>cull-less-leaves-*.jar</c>.
/// Per-pack <c>modpacks</c> keys are case-insensitive; missing slug is a silent no-op.
/// Layer 2 is evaluated first and wins when it matches; otherwise Layer 1.
/// </remarks>
public sealed class ExcludeIncludeMatcher
{
    public const string ModrinthEmbeddedName = "McManager.Core.Setup.modrinth-exclude-include.json";
    public const string CurseForgeEmbeddedName = "McManager.Core.Setup.cf-exclude-include.json";
    public const string ProductOverlayEmbeddedName = "McManager.Core.Setup.mcmgr-exclude-include.json";

    private readonly ExcludeIncludeLists _layer1;
    private readonly ExcludeIncludeLists _layer2;

    public ExcludeIncludeMatcher(ExcludeIncludeLists layer1, ExcludeIncludeLists? layer2 = null)
    {
        _layer1 = layer1 ?? throw new ArgumentNullException(nameof(layer1));
        _layer2 = layer2 ?? ExcludeIncludeLists.Empty;
    }

    public ExcludeIncludeLists Layer1 => _layer1;
    public ExcludeIncludeLists Layer2 => _layer2;

    public static ExcludeIncludeMatcher ForModrinth() =>
        new(LoadEmbedded(ModrinthEmbeddedName), LoadEmbedded(ProductOverlayEmbeddedName));

    public static ExcludeIncludeMatcher ForCurseForge() =>
        new(LoadEmbedded(CurseForgeEmbeddedName), LoadEmbedded(ProductOverlayEmbeddedName));

    public static ExcludeIncludeLists LoadEmbedded(string resourceName)
    {
        var assembly = typeof(ExcludeIncludeMatcher).Assembly;
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded exclude/include list missing: {resourceName}");
        using var reader = new StreamReader(stream);
        return ExcludeIncludeLists.Parse(reader.ReadToEnd());
    }

    /// <param name="packSlug">Modpack slug for <c>modpacks</c>; unmatched slugs are ignored.</param>
    /// <param name="relativePath">Index path or archive path (e.g. <c>mods/sodium-1.jar</c>).</param>
    /// <param name="projectSlug">Optional Modrinth/CF project slug when known without HTTP.</param>
    public ExcludeIncludeMatch Match(string? packSlug, string? relativePath, string? projectSlug = null)
    {
        var candidate = Candidate.From(relativePath, projectSlug);
        var overlay = MatchLayer(_layer2, packSlug, candidate);
        if (overlay.Decision != ExcludeIncludeDecision.NoMatch)
            return overlay;
        return MatchLayer(_layer1, packSlug, candidate);
    }

    private static ExcludeIncludeMatch MatchLayer(
        ExcludeIncludeLists lists,
        string? packSlug,
        Candidate candidate)
    {
        lists.TryGetPack(packSlug, out var pack);
        var force = Concat(lists.GlobalForceIncludes, pack.ForceIncludes);
        var excludes = Concat(lists.GlobalExcludes, pack.Excludes);

        if (TryFirstMatch(force, candidate, out var keepTerm))
            return new ExcludeIncludeMatch(ExcludeIncludeDecision.Keep, PackFileSkipReason.OverrideList, keepTerm);
        if (TryFirstMatch(excludes, candidate, out var skipTerm))
            return new ExcludeIncludeMatch(ExcludeIncludeDecision.Exclude, PackFileSkipReason.OverrideList, skipTerm);
        return ExcludeIncludeMatch.NoMatch;
    }

    private static IEnumerable<string> Concat(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        foreach (var item in a)
            yield return item;
        foreach (var item in b)
            yield return item;
    }

    private static bool TryFirstMatch(IEnumerable<string> terms, Candidate candidate, out string? matched)
    {
        foreach (var term in terms)
        {
            if (string.IsNullOrWhiteSpace(term))
                continue;
            if (TermMatches(term, candidate))
            {
                matched = term;
                return true;
            }
        }

        matched = null;
        return false;
    }

    internal static bool TermMatches(string term, Candidate candidate)
    {
        var trimmed = term.Trim();
        if (trimmed.Length >= 2 && trimmed.StartsWith('/') && trimmed.EndsWith('/'))
        {
            var body = trimmed[1..^1];
            try
            {
                var regex = new Regex(body, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                return regex.IsMatch(candidate.PathLower) || regex.IsMatch(candidate.FileNameLower)
                    || (!string.IsNullOrEmpty(candidate.SlugLower) && regex.IsMatch(candidate.SlugLower));
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        var needle = trimmed.ToLowerInvariant();
        if (candidate.ContainsLiteral(needle))
            return true;

        var collapsedNeedle = Collapse(needle);
        return collapsedNeedle.Length > 0 && candidate.ContainsCollapsed(collapsedNeedle);
    }

    /// <summary>
    /// Substring match that is not a suffix/infix of a longer alphabetic word.
    /// The start of the haystack and non-letters are left boundaries, so
    /// <c>titlebar</c> matches <c>titlebarchanger</c> but <c>ding</c> does not
    /// match <c>mob_grinding_utils</c>.
    /// </summary>
    internal static bool ContainsAsToken(string haystack, string needle)
    {
        if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle) || needle.Length > haystack.Length)
            return false;

        var start = 0;
        while (start <= haystack.Length - needle.Length)
        {
            var idx = haystack.IndexOf(needle, start, StringComparison.Ordinal);
            if (idx < 0)
                return false;
            if (idx == 0 || !IsAsciiLetter(haystack[idx - 1]))
                return true;
            start = idx + 1;
        }

        return false;
    }

    private static bool IsAsciiLetter(char c) =>
        (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    internal static string SanitizePath(string path)
    {
        if (path.Contains('\\') && !path.Contains('/'))
            return path.Replace('\\', '/');
        return path;
    }

    internal static string Collapse(string value)
    {
        Span<char> buffer = value.Length <= 256 ? stackalloc char[value.Length] : new char[value.Length];
        var n = 0;
        foreach (var c in value)
        {
            if (c is ' ' or '-' or '_')
                continue;
            buffer[n++] = c;
        }

        return n == 0 ? string.Empty : new string(buffer[..n]);
    }

    internal readonly record struct Candidate(
        string PathLower,
        string FileNameLower,
        string SlugLower,
        string PathCollapsed,
        string FileNameCollapsed,
        string SlugCollapsed)
    {
        public static Candidate From(string? relativePath, string? projectSlug)
        {
            var path = SanitizePath(relativePath ?? string.Empty).ToLowerInvariant();
            var fileName = path.Length == 0
                ? string.Empty
                : Path.GetFileName(path.Replace('/', Path.DirectorySeparatorChar));
            var slug = (projectSlug ?? string.Empty).Trim().ToLowerInvariant();
            return new Candidate(
                path,
                fileName,
                slug,
                Collapse(path),
                Collapse(fileName),
                Collapse(slug));
        }

        public bool ContainsLiteral(string needle) =>
            ContainsAsToken(PathLower, needle)
            || ContainsAsToken(FileNameLower, needle)
            || (!string.IsNullOrEmpty(SlugLower) && ContainsAsToken(SlugLower, needle));

        public bool ContainsCollapsed(string needle) =>
            ContainsAsToken(PathCollapsed, needle)
            || ContainsAsToken(FileNameCollapsed, needle)
            || (!string.IsNullOrEmpty(SlugCollapsed) && ContainsAsToken(SlugCollapsed, needle));
    }
}
