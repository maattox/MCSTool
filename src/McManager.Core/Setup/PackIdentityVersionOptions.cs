namespace McManager.Core.Setup;

/// <summary>
/// Builds Setup / Change pack identity dropdown lists from existing catalogs.
/// Detected values that are missing from a catalog are prepended, never dropped.
/// </summary>
public static class PackIdentityVersionOptions
{
    public static readonly int[] KnownJavaMajors = [8, 16, 17, 21, 25];

    public static IReadOnlyList<string> WithCurrent(IEnumerable<string> catalog, string? current)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        var cur = (current ?? "").Trim();
        if (cur.Length > 0)
        {
            list.Add(cur);
            seen.Add(cur);
        }

        foreach (var item in catalog)
        {
            var value = (item ?? "").Trim();
            if (value.Length == 0 || !seen.Add(value))
                continue;
            list.Add(value);
        }

        return list;
    }

    public static IReadOnlyList<string> MinecraftIds(
        MojangVersionManifest? manifest,
        string? current,
        bool includeSnapshots = false)
    {
        if (manifest is null)
            return WithCurrent([], current);

        var ids = MojangVersionCatalog.Filter(manifest, includeSnapshots).Select(v => v.Id);
        return WithCurrent(ids, current);
    }

    public static IReadOnlyList<string> FabricLoaderVersions(
        IEnumerable<FabricGameLoaderEntry>? loaders,
        string? current)
    {
        if (loaders is null)
            return WithCurrent([], current);

        var ids = loaders
            .Select(e => (e.Loader.Version ?? "").Trim())
            .Where(v => v.Length > 0);
        return WithCurrent(ids, current);
    }

    public static IReadOnlyList<string> ForgeVersions(
        IReadOnlyDictionary<string, string>? promos,
        string minecraftVersion,
        string? current)
    {
        var catalog = new List<string>();
        var mc = (minecraftVersion ?? "").Trim();
        if (promos is not null && mc.Length > 0)
        {
            AddPromo(catalog, promos, $"{mc}-recommended");
            AddPromo(catalog, promos, $"{mc}-latest");
        }

        return WithCurrent(catalog, current);
    }

    public static IReadOnlyList<string> NeoForgeVersions(
        IEnumerable<string>? versions,
        string minecraftVersion,
        string? current)
    {
        if (versions is null
            || !NeoForgeMavenClient.TryMinecraftTarget(minecraftVersion, out var minor, out var patch))
        {
            return WithCurrent([], current);
        }

        var ids = versions
            .Select(NeoForgeMavenClient.ParseNeoForgeVersion)
            .Where(v => v is not null && v.McMinor == minor && v.McPatch == patch && !v.IsPrerelease)
            .Cast<NeoForgeVersionId>()
            .OrderByDescending(v => v)
            .Select(v => v.Raw);
        return WithCurrent(ids, current);
    }

    public static IReadOnlyList<string> JavaMajors(string? minecraftVersion, string? current)
    {
        var floor = DerivedPackIdentity.JavaMajorForMinecraftOrNull(minecraftVersion);
        IEnumerable<int> majors = KnownJavaMajors;
        if (floor is int f)
            majors = KnownJavaMajors.Where(m => m >= f);

        return WithCurrent(majors.Select(m => m.ToString()), current);
    }

    public static IReadOnlyList<string> LoaderVersions(
        string loader,
        string minecraftVersion,
        string? current,
        IEnumerable<FabricGameLoaderEntry>? fabricLoaders,
        IReadOnlyDictionary<string, string>? forgePromos,
        IEnumerable<string>? neoForgeVersions)
    {
        var kind = (loader ?? "").Trim().ToLowerInvariant();
        if (kind == MrpackAnalyzer.LoaderFabric)
            return FabricLoaderVersions(fabricLoaders, current);
        if (kind == MrpackAnalyzer.LoaderForge)
            return ForgeVersions(forgePromos, minecraftVersion, current);
        if (kind == MrpackAnalyzer.LoaderNeoForge)
            return NeoForgeVersions(neoForgeVersions, minecraftVersion, current);
        return WithCurrent([], current);
    }

    private static void AddPromo(
        List<string> catalog,
        IReadOnlyDictionary<string, string> promos,
        string key)
    {
        if (!promos.TryGetValue(key, out var raw))
            return;
        var value = (raw ?? "").Trim();
        if (value.Length == 0)
            return;
        if (catalog.Exists(v => string.Equals(v, value, StringComparison.OrdinalIgnoreCase)))
            return;
        catalog.Add(value);
    }
}
