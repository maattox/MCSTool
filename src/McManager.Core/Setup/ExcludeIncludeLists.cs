using System.Text.Json;

namespace McManager.Core.Setup;

/// <summary>
/// itzg exclude/include JSON schema
/// (https://github.com/itzg/mc-image-helper#excludeinclude-file-schema).
/// </summary>
public sealed class ExcludeIncludeLists
{
    public IReadOnlyList<string> GlobalExcludes { get; init; } = [];
    public IReadOnlyList<string> GlobalForceIncludes { get; init; } = [];
    public IReadOnlyDictionary<string, PackExcludeIncludes> Modpacks { get; init; } =
        new Dictionary<string, PackExcludeIncludes>(StringComparer.OrdinalIgnoreCase);

    public static ExcludeIncludeLists Empty { get; } = new();

    /// <summary>
    /// Overlay terms are concatenated in front of <paramref name="baseLists"/> so they
    /// match first. Pack keys are unioned; same-key lists concatenate the same way.
    /// </summary>
    public static ExcludeIncludeLists Merge(ExcludeIncludeLists baseLists, ExcludeIncludeLists overlay)
    {
        ArgumentNullException.ThrowIfNull(baseLists);
        ArgumentNullException.ThrowIfNull(overlay);
        if (ReferenceEquals(overlay, Empty)
            && overlay.GlobalExcludes.Count == 0
            && overlay.GlobalForceIncludes.Count == 0
            && overlay.Modpacks.Count == 0)
        {
            return baseLists;
        }

        var packs = new Dictionary<string, PackExcludeIncludes>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, pack) in baseLists.Modpacks)
            packs[key] = pack;
        foreach (var (key, pack) in overlay.Modpacks)
        {
            if (packs.TryGetValue(key, out var existing))
            {
                packs[key] = new PackExcludeIncludes
                {
                    Excludes = Concat(pack.Excludes, existing.Excludes),
                    ForceIncludes = Concat(pack.ForceIncludes, existing.ForceIncludes),
                };
            }
            else
            {
                packs[key] = pack;
            }
        }

        return new ExcludeIncludeLists
        {
            GlobalExcludes = Concat(overlay.GlobalExcludes, baseLists.GlobalExcludes),
            GlobalForceIncludes = Concat(overlay.GlobalForceIncludes, baseLists.GlobalForceIncludes),
            Modpacks = packs,
        };
    }

    private static IReadOnlyList<string> Concat(IReadOnlyList<string> first, IReadOnlyList<string> second)
    {
        if (first.Count == 0)
            return second;
        if (second.Count == 0)
            return first;
        var list = new List<string>(first.Count + second.Count);
        list.AddRange(first);
        list.AddRange(second);
        return list;
    }

    public static ExcludeIncludeLists Parse(string json)
    {
        var dto = JsonSerializer.Deserialize<ExcludeIncludeFileDto>(json, JsonOptions)
            ?? throw new InvalidOperationException("Exclude/include JSON deserialized to null.");
        var packs = new Dictionary<string, PackExcludeIncludes>(StringComparer.OrdinalIgnoreCase);
        if (dto.Modpacks is not null)
        {
            foreach (var (slug, entry) in dto.Modpacks)
            {
                if (string.IsNullOrWhiteSpace(slug) || entry is null)
                    continue;
                packs[slug.Trim()] = new PackExcludeIncludes
                {
                    Excludes = entry.Excludes ?? [],
                    ForceIncludes = entry.ForceIncludes ?? [],
                };
            }
        }

        return new ExcludeIncludeLists
        {
            GlobalExcludes = dto.GlobalExcludes ?? [],
            GlobalForceIncludes = dto.GlobalForceIncludes ?? [],
            Modpacks = packs,
        };
    }

    public bool TryGetPack(string? packSlug, out PackExcludeIncludes entry)
    {
        if (string.IsNullOrWhiteSpace(packSlug))
        {
            entry = PackExcludeIncludes.Empty;
            return false;
        }

        if (Modpacks.TryGetValue(packSlug.Trim(), out entry!))
            return true;

        entry = PackExcludeIncludes.Empty;
        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private sealed class ExcludeIncludeFileDto
    {
        public List<string>? GlobalExcludes { get; set; }
        public List<string>? GlobalForceIncludes { get; set; }
        public Dictionary<string, PackExcludeIncludesDto>? Modpacks { get; set; }
    }

    private sealed class PackExcludeIncludesDto
    {
        public List<string>? Excludes { get; set; }
        public List<string>? ForceIncludes { get; set; }
    }
}

public sealed class PackExcludeIncludes
{
    public static PackExcludeIncludes Empty { get; } = new();

    public IReadOnlyList<string> Excludes { get; init; } = [];
    public IReadOnlyList<string> ForceIncludes { get; init; } = [];
}

public enum ExcludeIncludeDecision
{
    /// <summary>No list term matched; caller applies pack <c>env.server</c> / in-jar side.</summary>
    NoMatch,
    Keep,
    Exclude,
}

/// <summary>
/// Why a file was skipped (or force-kept). Matcher emits <see cref="OverrideList"/>;
/// .mrpack analyze/install also emit <see cref="PackDeclared"/>.
/// </summary>
public enum PackFileSkipReason
{
    None,
    OverrideList,
    PackDeclared,
    InJarMetadata,
    OperatorSkip,
}

public readonly record struct ExcludeIncludeMatch(
    ExcludeIncludeDecision Decision,
    PackFileSkipReason Reason,
    string? MatchedTerm)
{
    public static ExcludeIncludeMatch NoMatch { get; } =
        new(ExcludeIncludeDecision.NoMatch, PackFileSkipReason.None, null);

    public bool Exclude => Decision == ExcludeIncludeDecision.Exclude;
    public bool Keep => Decision == ExcludeIncludeDecision.Keep;
}
