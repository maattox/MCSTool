using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.Hybrid;

/// <summary>
/// Optional OCI / door clients for the manage chrome. Built from local config only —
/// constructing an <see cref="OciSession"/> does not probe the API.
/// </summary>
public sealed class ManageCloudServices
{
    public OciSession? Session { get; }
    public ComputeService? Compute { get; }
    public DoorClient? Door { get; }
    public UsageBudgetStore? UsageStore { get; }
    public SshService Ssh { get; } = new();

    public string? SessionError { get; }
    public string? DoorError { get; }

    public ManageCloudServices(LocalConfigHost configHost)
    {
        var config = configHost.Config;
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
}
