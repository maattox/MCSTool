using System.Collections.ObjectModel;
using McManager.Core.Config;
using McManager.Core.Services;
using Oci.Common;
using Oci.Common.Auth;
using Oci.Common.Retry;
using Oci.Common.Waiters;
using Oci.CoreService;
using Oci.IdentityService;
using Oci.ObjectstorageService;

namespace McManager.Core.Oci;

/// <summary>
/// OCI API session built from operator local config (~/.oci config file + profile + region).
/// </summary>
public sealed class OciSession : IDisposable
{
    private readonly ConfigFileAuthenticationDetailsProvider _authProvider;

    public ComputeClient Compute { get; }
    public IdentityClient Identity { get; }
    public VirtualNetworkClient VirtualNetwork { get; }
    public ObjectStorageClient ObjectStorage { get; }

    /// <summary>Shared retry config for per-call overrides when needed.</summary>
    public RetryConfiguration RetryConfiguration { get; }

    private OciSession(
        ConfigFileAuthenticationDetailsProvider authProvider,
        ComputeClient compute,
        IdentityClient identity,
        VirtualNetworkClient virtualNetwork,
        ObjectStorageClient objectStorage,
        RetryConfiguration retryConfiguration)
    {
        _authProvider = authProvider;
        Compute = compute;
        Identity = identity;
        VirtualNetwork = virtualNetwork;
        ObjectStorage = objectStorage;
        RetryConfiguration = retryConfiguration;
    }

    public static RetryConfiguration CreateDefaultRetryConfiguration() =>
        new()
        {
            MaxAttempts = 6,
            TotalElapsedTimeInSecs = 60,
            GetNextDelayInSeconds = DelayStrategy.GetExponentialDelayInSeconds,
            RetryableStatusCodeFamilies = new List<int> { 5 },
            RetryableErrors = new Collection<Tuple<int, string>>
            {
                Tuple.Create(429, "TooManyRequests"),
            },
        };

    public static ServiceResult<OciSession> TryCreate(ManagerLocalConfig config)
    {
        try
        {
            var configPath = LocalConfigStore.ExpandPath(config.Oci.ConfigFile);
            if (string.IsNullOrWhiteSpace(configPath))
                return ServiceResult<OciSession>.Fail("OCI config file path is empty.");

            if (!File.Exists(configPath))
                return ServiceResult<OciSession>.Fail($"OCI config file not found: {configPath}");

            var profile = string.IsNullOrWhiteSpace(config.Oci.Profile) ? "DEFAULT" : config.Oci.Profile;

            ConfigFileAuthenticationDetailsProvider authProvider;
            try
            {
                authProvider = new ConfigFileAuthenticationDetailsProvider(configPath, profile);
            }
            catch (Exception ex)
            {
                return ServiceResult<OciSession>.Fail(
                    $"Failed to load OCI profile '{profile}' from {configPath}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(config.Oci.Region))
                return ServiceResult<OciSession>.Fail("oci.region is empty in local config.");

            Region region;
            try
            {
                region = Region.FromRegionId(config.Oci.Region);
            }
            catch (Exception ex)
            {
                return ServiceResult<OciSession>.Fail(
                    $"Invalid oci.region '{config.Oci.Region}': {ex.Message}");
            }

            var retry = CreateDefaultRetryConfiguration();
            var clientConfig = new ClientConfiguration
            {
                RetryConfiguration = retry,
            };

            var compute = new ComputeClient(authProvider, clientConfig);
            var identity = new IdentityClient(authProvider, clientConfig);
            var virtualNetwork = new VirtualNetworkClient(authProvider, clientConfig);
            var objectStorage = new ObjectStorageClient(authProvider, clientConfig);

            compute.SetRegion(region);
            identity.SetRegion(region);
            virtualNetwork.SetRegion(region);
            objectStorage.SetRegion(region);

            return ServiceResult<OciSession>.Ok(
                new OciSession(authProvider, compute, identity, virtualNetwork, objectStorage, retry));
        }
        catch (Exception ex)
        {
            return ServiceResult<OciSession>.Fail($"OCI session error: {ex.Message}");
        }
    }

    public void Dispose()
    {
        Compute.Dispose();
        Identity.Dispose();
        VirtualNetwork.Dispose();
        ObjectStorage.Dispose();
    }
}
