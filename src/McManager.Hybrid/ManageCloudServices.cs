using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.Hybrid;

/// <summary>
/// Optional OCI / door clients for the manage chrome. Built from local config only —
/// constructing an <see cref="OciSession"/> does not probe the API.
/// Call <see cref="Rebuild"/> after <see cref="LocalConfigHost.Reload"/> so Setup
/// Close / Connect-existing do not keep a first-run empty session.
/// </summary>
public sealed class ManageCloudServices : IDisposable
{
    private readonly LocalConfigHost _configHost;
    private bool _disposed;

    public OciSession? Session { get; private set; }
    public ComputeService? Compute { get; private set; }
    public DoorClient? Door { get; private set; }
    public UsageBudgetStore? UsageStore { get; private set; }
    public SpendBrakeLockStore? SpendBrakeLock { get; private set; }
    public OversizedWorldBackupStore? OversizedWorldBackup { get; private set; }
    public SshService Ssh { get; private set; } = new();

    public string? SessionError { get; private set; }
    public string? DoorError { get; private set; }

    public ManageCloudServices(LocalConfigHost configHost)
    {
        _configHost = configHost;
        Rebuild();
    }

    /// <summary>
    /// Drop the previous session/door clients and build new ones from
    /// <see cref="LocalConfigHost.Config"/> (or clear them when config is gone).
    /// </summary>
    public void Rebuild()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DisposeClients();
        SessionError = null;
        DoorError = null;
        Ssh = new();

        var config = _configHost.Config;
        if (config is null)
            return;

        var sessionResult = OciSession.TryCreate(config);
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            SessionError = sessionResult.Error ?? "Cloud session failed.";
        }
        else
        {
            Session = sessionResult.Value;
            Compute = new ComputeService(Session);
            var os = new ObjectStorageService(Session, config.ObjectStorage);
            UsageStore = new UsageBudgetStore(os, config.ObjectStorage.Prefixes);
            SpendBrakeLock = new SpendBrakeLockStore(os, config.ObjectStorage.Prefixes);
            OversizedWorldBackup = new OversizedWorldBackupStore(os, config.ObjectStorage.Prefixes);
        }

        try
        {
            Door = new DoorClient(config.DoorAdminBaseUrl);
        }
        catch (Exception ex)
        {
            DoorError = ex.Message;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        DisposeClients();
    }

    private void DisposeClients()
    {
        Door?.Dispose();
        Door = null;
        Session?.Dispose();
        Session = null;
        Compute = null;
        UsageStore = null;
        SpendBrakeLock = null;
        OversizedWorldBackup = null;
    }
}
