using McManager.Core.Setup;

namespace McManager.Hybrid;

/// <summary>
/// Shared Mojang / Fabric / Forge / NeoForge catalog cache so Setup vanilla
/// and pack identity dropdowns do not duplicate HTTP.
/// </summary>
public sealed class PackIdentityCatalogCache
{
    private readonly MojangVersionCatalog _mojang = new();
    private readonly FabricMetaClient _fabric = new();
    private readonly ForgePromotionsClient _forge = new();
    private readonly NeoForgeMavenClient _neo = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, IReadOnlyList<FabricGameLoaderEntry>?> _fabricByMc =
        new(StringComparer.OrdinalIgnoreCase);

    private MojangCatalogResult? _mojangResult;
    private IReadOnlyDictionary<string, string>? _forgePromos;
    private bool _forgeAttempted;
    private IReadOnlyList<string>? _neoVersions;
    private bool _neoAttempted;

    public async Task<MojangCatalogResult> GetMojangAsync(CancellationToken cancellationToken = default)
    {
        if (_mojangResult is not null)
            return _mojangResult;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _mojangResult ??= await _mojang.LoadAsync(cancellationToken).ConfigureAwait(false);
            return _mojangResult;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyDictionary<string, string>?> GetForgePromosAsync(
        CancellationToken cancellationToken = default)
    {
        if (_forgeAttempted)
            return _forgePromos;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_forgeAttempted)
                return _forgePromos;
            var result = await _forge.GetPromosAsync(cancellationToken).ConfigureAwait(false);
            _forgePromos = result.Succeeded ? result.Value : null;
            _forgeAttempted = true;
            return _forgePromos;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<string>?> GetNeoForgeVersionsAsync(
        CancellationToken cancellationToken = default)
    {
        if (_neoAttempted)
            return _neoVersions;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_neoAttempted)
                return _neoVersions;
            var result = await _neo.GetVersionsAsync(cancellationToken).ConfigureAwait(false);
            _neoVersions = result.Succeeded ? result.Value : null;
            _neoAttempted = true;
            return _neoVersions;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<FabricGameLoaderEntry>?> GetFabricLoadersAsync(
        string minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        var mc = (minecraftVersion ?? "").Trim();
        if (mc.Length == 0)
            return null;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fabricByMc.TryGetValue(mc, out var cached))
                return cached;
            var result = await _fabric.GetLoadersForGameAsync(mc, cancellationToken).ConfigureAwait(false);
            var list = result.Succeeded ? result.Value : null;
            _fabricByMc[mc] = list;
            return list;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool TryGetMojang(out MojangCatalogResult result)
    {
        var cached = _mojangResult;
        if (cached is null)
        {
            result = null!;
            return false;
        }

        result = cached;
        return true;
    }

    public bool TryGetForgePromos(out IReadOnlyDictionary<string, string>? promos)
    {
        if (!_forgeAttempted)
        {
            promos = null;
            return false;
        }

        promos = _forgePromos;
        return true;
    }

    public bool TryGetNeoForgeVersions(out IReadOnlyList<string>? versions)
    {
        if (!_neoAttempted)
        {
            versions = null;
            return false;
        }

        versions = _neoVersions;
        return true;
    }

    public bool TryGetFabricLoaders(string minecraftVersion, out IReadOnlyList<FabricGameLoaderEntry>? loaders)
    {
        var mc = (minecraftVersion ?? "").Trim();
        if (mc.Length == 0)
        {
            loaders = null;
            return false;
        }

        return _fabricByMc.TryGetValue(mc, out loaders);
    }

    /// <summary>
    /// Warm Mojang plus the loader catalog needed for unstructured pack identity dropdowns.
    /// Failures are swallowed; the identity form falls back to text inputs.
    /// </summary>
    public async Task PrefetchForIdentityAsync(
        string? loader,
        string? minecraftVersion,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await GetMojangAsync(cancellationToken).ConfigureAwait(false);
            var kind = (loader ?? "").Trim().ToLowerInvariant();
            var mc = (minecraftVersion ?? "").Trim();
            if (string.Equals(mc, "(unknown)", StringComparison.OrdinalIgnoreCase))
                mc = "";

            if (kind == MrpackAnalyzer.LoaderFabric)
                await GetFabricLoadersAsync(mc, cancellationToken).ConfigureAwait(false);
            else if (kind == MrpackAnalyzer.LoaderForge)
                await GetForgePromosAsync(cancellationToken).ConfigureAwait(false);
            else if (kind == MrpackAnalyzer.LoaderNeoForge)
                await GetNeoForgeVersionsAsync(cancellationToken).ConfigureAwait(false);
            else
                await PrefetchModdedLoaderCatalogsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Identity fields fall back to text if catalogs fail.
        }
    }

    /// <summary>Warm Forge and NeoForge lists when the operator picks Modded (Fabric needs a Minecraft version).</summary>
    public async Task PrefetchModdedLoaderCatalogsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await GetForgePromosAsync(cancellationToken).ConfigureAwait(false);
            await GetNeoForgeVersionsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Identity fields fall back to text if catalogs fail.
        }
    }
}
