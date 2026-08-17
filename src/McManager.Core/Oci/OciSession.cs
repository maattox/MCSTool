using System.Collections.ObjectModel;
using McManager.Core.Config;
using McManager.Core.Services;
using Oci.Common;
using Oci.Common.Auth;
using Oci.Common.Retry;
using Oci.Common.Waiters;
using Oci.ArtifactsService;
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
    public ArtifactsClient Artifacts { get; }

    /// <summary>Shared retry config for per-call overrides when needed.</summary>
    public RetryConfiguration RetryConfiguration { get; }

    private OciSession(
        ConfigFileAuthenticationDetailsProvider authProvider,
        ComputeClient compute,
        IdentityClient identity,
        VirtualNetworkClient virtualNetwork,
        ObjectStorageClient objectStorage,
        ArtifactsClient artifacts,
        RetryConfiguration retryConfiguration)
    {
        _authProvider = authProvider;
        Compute = compute;
        Identity = identity;
        VirtualNetwork = virtualNetwork;
        ObjectStorage = objectStorage;
        Artifacts = artifacts;
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
        var configPath = LocalConfigStore.ExpandPath(config.Oci.ConfigFile);
        var profile = string.IsNullOrWhiteSpace(config.Oci.Profile) ? "DEFAULT" : config.Oci.Profile;
        return TryCreate(configPath, profile, config.Oci.Region);
    }

    /// <summary>
    /// Build a session from an OCI config file + profile + region without a full
    /// <see cref="ManagerLocalConfig"/> (Connect-existing auto-detect).
    /// </summary>
    public static ServiceResult<OciSession> TryCreate(string configFile, string profile, string regionId)
    {
        try
        {
            var configPath = LocalConfigStore.ExpandPath(configFile);
            if (string.IsNullOrWhiteSpace(configPath))
                return ServiceResult<OciSession>.Fail("OCI config file path is empty.");

            if (!File.Exists(configPath))
                return ServiceResult<OciSession>.Fail($"OCI config file not found: {configPath}");

            var resolvedProfile = string.IsNullOrWhiteSpace(profile) ? "DEFAULT" : profile.Trim();

            ConfigFileAuthenticationDetailsProvider authProvider;
            try
            {
                authProvider = new ConfigFileAuthenticationDetailsProvider(configPath, resolvedProfile);
            }
            catch (Exception ex)
            {
                return ServiceResult<OciSession>.Fail(
                    $"Failed to load OCI profile '{resolvedProfile}' from {configPath}: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(regionId))
                return ServiceResult<OciSession>.Fail("OCI region is empty.");

            Region region;
            try
            {
                region = Region.FromRegionId(regionId.Trim());
            }
            catch (Exception ex)
            {
                return ServiceResult<OciSession>.Fail(
                    $"Invalid OCI region '{regionId}': {ex.Message}");
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
            var artifacts = new ArtifactsClient(authProvider, clientConfig);

            compute.SetRegion(region);
            identity.SetRegion(region);
            virtualNetwork.SetRegion(region);
            objectStorage.SetRegion(region);
            artifacts.SetRegion(region);

            return ServiceResult<OciSession>.Ok(
                new OciSession(authProvider, compute, identity, virtualNetwork, objectStorage, artifacts, retry));
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
        Artifacts.Dispose();
    }
}
