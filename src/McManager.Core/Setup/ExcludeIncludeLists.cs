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
