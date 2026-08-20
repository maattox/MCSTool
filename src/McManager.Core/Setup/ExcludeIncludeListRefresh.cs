namespace McManager.Core.Setup;

/// <summary>
/// Optional GitHub raw refresh of itzg Layer 1 lists on the admin PC at analyze time (R4).
/// Timeout, non-JSON, empty excludes, or any other error → bundled embedded copy.
/// Never throws to the caller; Setup must not fail because GitHub was down.
/// </summary>
public sealed class ExcludeIncludeListRefresh
{
    public const string ModrinthRawUrl =
        "https://raw.githubusercontent.com/itzg/docker-minecraft-server/master/files/modrinth-exclude-include.json";

    public const string CurseForgeRawUrl =
        "https://raw.githubusercontent.com/itzg/docker-minecraft-server/master/files/cf-exclude-include.json";

    public const int TimeoutSeconds = 5;

    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, ExcludeIncludeLists> _remote = new(StringComparer.Ordinal);
    private readonly HashSet<string> _attempted = new(StringComparer.Ordinal);

    public ExcludeIncludeListRefresh(HttpClient? http = null)
    {
        if (http is null)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(TimeoutSeconds) };
        }
        else
        {
            _http = http;
        }

        FabricMetaClient.EnsureUserAgent(_http);
    }

    /// <summary>Process-wide refresh used by Setup analyze. Tests should construct their own.</summary>
    public static ExcludeIncludeListRefresh Shared { get; } = new();

    public ExcludeIncludeMatcher ModrinthMatcher() =>
        new(
            Layer1OrEmbedded(ModrinthRawUrl, ExcludeIncludeMatcher.ModrinthEmbeddedName),
            ExcludeIncludeMatcher.LoadEmbedded(ExcludeIncludeMatcher.ProductOverlayEmbeddedName));

    public ExcludeIncludeMatcher CurseForgeMatcher() =>
        new(
            Layer1OrEmbedded(CurseForgeRawUrl, ExcludeIncludeMatcher.CurseForgeEmbeddedName),
            ExcludeIncludeMatcher.LoadEmbedded(ExcludeIncludeMatcher.ProductOverlayEmbeddedName));

    /// <summary>True when the last Layer 1 load for <paramref name="url"/> came from GitHub, not the embed.</summary>
    public bool UsedRemote(string url)
    {
        lock (_gate)
            return _remote.ContainsKey(url);
    }

    private ExcludeIncludeLists Layer1OrEmbedded(string url, string embeddedName)
    {
        lock (_gate)
        {
            if (_remote.TryGetValue(url, out var cached))
                return cached;

            if (!_attempted.Add(url))
                return ExcludeIncludeMatcher.LoadEmbedded(embeddedName);

            var fetched = TryFetch(url);
            if (fetched is not null)
            {
                _remote[url] = fetched;
                return fetched;
            }

            return ExcludeIncludeMatcher.LoadEmbedded(embeddedName);
        }
    }

    private ExcludeIncludeLists? TryFetch(string url)
    {
        try
        {
            var json = _http.GetStringAsync(url).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var lists = ExcludeIncludeLists.Parse(json);
            if (lists.GlobalExcludes.Count == 0)
                return null;

            return lists;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
