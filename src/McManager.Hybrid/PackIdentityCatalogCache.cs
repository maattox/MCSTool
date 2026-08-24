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
            _forgeAttempted = true;
            _forgePromos = result.Succeeded ? result.Value : null;
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
            _neoAttempted = true;
            _neoVersions = result.Succeeded ? result.Value : null;
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
}
