using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Setup;
using McManager.Core.Usage;
using Oci.IdentityService.Models;
using Oci.IdentityService.Requests;
using Oci.ObjectstorageService.Requests;

namespace McManager.Core.Services;

/// <summary>
/// Button-gated Connect-existing discovery. Never called from app startup.
/// Always Free: list/get only — no create, start, or Security List writes.
/// </summary>
public static class ConnectExistingService
{
    public const string ProductCompartmentName = "mcmgr";
    public const string DomainTagKey = "mcmgr-domain";
    public const string DomainTagValue = "mc-server-compartment";
    public const string GreenfieldBucketName = "mcmgr-shared-data";
    public const string InfraObjectName = "meta/infra.json";
    public const string DefaultOciConfigFile = "%USERPROFILE%\\.oci\\config";

    public static bool IsProductCompartment(string? displayName, IDictionary<string, string>? freeformTags)
    {
        if (CompartmentNamer.IsProductName(displayName))
            return true;

        return freeformTags is not null
               && freeformTags.TryGetValue(DomainTagKey, out var value)
               && string.Equals(value, DomainTagValue, StringComparison.Ordinal);
    }

    public static async Task<ServiceResult<ConnectExistingScanResult>> ScanAsync(
        IProgress<string>? progress = null,
        string? ociConfigPath = null,
        CancellationToken cancellationToken = default)
    {
        var configPath = ociConfigPath ?? OciConfigProfiles.DefaultConfigPath();
        progress?.Report("Reading OCI config (no API calls yet)…");

        if (!File.Exists(LocalConfigStore.ExpandPath(configPath)))
        {
            return ServiceResult<ConnectExistingScanResult>.Ok(new ConnectExistingScanResult
            {
                Notes =
                [
                    $"No OCI config at {configPath}. Add an API key profile under %USERPROFILE%\\.oci\\config, or use Setup.",
                ],
            });
        }

        var profiles = OciConfigProfiles.List(configPath);
        var candidates = new List<ConnectExistingCandidate>();
        var notes = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var profile in profiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(profile.Region))
            {
                notes.Add($"Skipped profile '{profile.Name}': no region in ~/.oci/config.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(profile.Tenancy))
            {
                notes.Add($"Skipped profile '{profile.Name}': no tenancy OCID in ~/.oci/config.");
                continue;
            }

            progress?.Report($"Trying profile {profile.Name} ({profile.Region})…");
            var sessionResult = OciSession.TryCreate(configPath, profile.Name, profile.Region);
            if (!sessionResult.Succeeded || sessionResult.Value is null)
            {
                notes.Add($"Skipped profile '{profile.Name}': {sessionResult.Error}");
                continue;
            }

            using var session = sessionResult.Value;
            try
            {
                var found = await ScanProfileAsync(
                    session,
                    profile,
                    configPath,
                    progress,
                    cancellationToken).ConfigureAwait(false);
                notes.AddRange(found.Notes);
                foreach (var candidate in found.Candidates)
                {
                    if (seen.Add(candidate.DedupeKey))
                        candidates.Add(candidate);
                    else
                        notes.Add(
                            $"Skipped duplicate stack already found "
                            + $"(tenancy/compartment/bucket) via profile {candidate.ProfileName}.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                notes.Add($"Profile '{profile.Name}' failed: {OciErrorFormatter.Format("Connect-existing scan", ex)}");
            }
        }

        progress?.Report(
            candidates.Count == 0
                ? "No product stacks found."
                : $"Found {candidates.Count} stack(s).");

        return ServiceResult<ConnectExistingScanResult>.Ok(new ConnectExistingScanResult
        {
            Candidates = candidates,
            Notes = notes,
        });
    }

    public static async Task<ServiceResult<ManagerLocalConfig>> HydrateAsync(
        ConnectExistingCandidate candidate,
        string sshKeyPath,
        ManagerLocalConfig? preserveLocal = null,
        string? rconPassword = null,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (candidate.Document is null)
            return ServiceResult<ManagerLocalConfig>.Fail("Candidate has no meta/infra.json document.");

        var compatibility = ConnectExistingCompatibility.Evaluate(candidate);
        if (compatibility.BlocksConnect)
            return ServiceResult<ManagerLocalConfig>.Fail(compatibility.HydrateError);

        var doc = candidate.Document;
        var needVm1 = string.IsNullOrWhiteSpace(doc.Vm1.SshHost);
        var needDoor = string.IsNullOrWhiteSpace(doc.Door.SshHost);
        if (needVm1 || needDoor)
        {
            progress?.Report("Refreshing stale/missing SSH hosts (Get-by-OCID)…");
            var sessionResult = OciSession.TryCreate(
                candidate.OciConfigFile,
                candidate.ProfileName,
                string.IsNullOrWhiteSpace(doc.Region) ? candidate.Region : doc.Region);
            if (!sessionResult.Succeeded || sessionResult.Value is null)
            {
                progress?.Report($"SSH host refresh skipped: {sessionResult.Error}");
            }
            else
            {
                using var session = sessionResult.Value;
                var compute = new ComputeService(session);
                var compartmentId = string.IsNullOrWhiteSpace(doc.CompartmentId)
                    ? candidate.CompartmentId
                    : doc.CompartmentId;

                if (needVm1 && !string.IsNullOrWhiteSpace(doc.Vm1.InstanceId))
                {
                    var ip = await compute.TryGetPrimaryPublicIpAsync(
                        compartmentId, doc.Vm1.InstanceId, cancellationToken).ConfigureAwait(false);
                    if (ip.Succeeded && !string.IsNullOrWhiteSpace(ip.Value))
                        doc.Vm1.SshHost = ip.Value;
                    else if (!ip.Succeeded)
                        progress?.Report($"VM1 ssh_host refresh failed: {ip.Error}");
                }

                if (needDoor && !string.IsNullOrWhiteSpace(doc.Door.InstanceId))
                {
                    var ip = await compute.TryGetPrimaryPublicIpAsync(
                        compartmentId, doc.Door.InstanceId, cancellationToken).ConfigureAwait(false);
                    if (ip.Succeeded && !string.IsNullOrWhiteSpace(ip.Value))
                        doc.Door.SshHost = ip.Value;
                    else if (!ip.Succeeded)
                        progress?.Report($"Door ssh_host refresh failed: {ip.Error}");
                }
            }
        }

        return ServiceResult<ManagerLocalConfig>.Ok(
            doc.ToLocalConfig(
                candidate.OciConfigFile,
                candidate.ProfileName,
                sshKeyPath,
                rconPassword,
                preserveLocal));
    }

    private static async Task<ConnectExistingScanResult> ScanProfileAsync(
        OciSession session,
        OciConfigProfile profile,
        string configPath,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var candidates = new List<ConnectExistingCandidate>();

        progress?.Report($"Listing compartments for {profile.Name}…");
        var compartments = await ListProductCompartmentsAsync(session, profile.Tenancy, notes, cancellationToken)
            .ConfigureAwait(false);
        if (compartments.Count == 0)
        {
            notes.Add(
                $"Profile '{profile.Name}': no compartment named '{ProductCompartmentName}' / '{ProductCompartmentName}-2' "
                + $"or tagged {DomainTagKey}={DomainTagValue}.");
            return new ConnectExistingScanResult { Notes = notes };
        }

        progress?.Report($"Resolving Object Storage namespace for {profile.Name}…");
        var nsResult = await GetNamespaceAsync(session, cancellationToken).ConfigureAwait(false);
        if (!nsResult.Succeeded || string.IsNullOrWhiteSpace(nsResult.Value))
        {
            notes.Add($"Profile '{profile.Name}': {nsResult.Error ?? "GetNamespace failed."}");
            return new ConnectExistingScanResult { Notes = notes };
        }

        var ns = nsResult.Value!;
        foreach (var compartment in compartments)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report($"Checking compartment {compartment.Name}…");
            var found = await ScanCompartmentBucketsAsync(
                session,
                profile,
                configPath,
                ns,
                compartment,
                notes,
                progress,
                cancellationToken).ConfigureAwait(false);
            candidates.AddRange(found);
        }

        return new ConnectExistingScanResult { Candidates = candidates, Notes = notes };
    }

    private static async Task<IReadOnlyList<Compartment>> ListProductCompartmentsAsync(
        OciSession session,
        string tenancyId,
        List<string> notes,
        CancellationToken cancellationToken)
    {
        var matches = new List<Compartment>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var root = await session.Identity.GetCompartment(
                new GetCompartmentRequest { CompartmentId = tenancyId },
                retryConfiguration: session.RetryConfiguration,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (root.Compartment is not null
                && IsProductCompartment(root.Compartment.Name, root.Compartment.FreeformTags)
                && seen.Add(root.Compartment.Id))
            {
                matches.Add(root.Compartment);
            }
        }
        catch (Exception ex)
        {
            notes.Add("GetCompartment(tenancy root): " + OciErrorFormatter.Format("GetCompartment", ex));
        }

        try
        {
            string? page = null;
            do
            {
                var response = await session.Identity.ListCompartments(
                    new ListCompartmentsRequest
                    {
                        CompartmentId = tenancyId,
                        CompartmentIdInSubtree = true,
                        AccessLevel = ListCompartmentsRequest.AccessLevelEnum.Accessible,
                        LifecycleState = Compartment.LifecycleStateEnum.Active,
                        Page = page,
                    },
                    retryConfiguration: session.RetryConfiguration,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (response.Items is not null)
                {
                    foreach (var compartment in response.Items)
                    {
                        if (compartment is null || string.IsNullOrWhiteSpace(compartment.Id))
                            continue;
                        if (!IsProductCompartment(compartment.Name, compartment.FreeformTags))
                            continue;
                        if (seen.Add(compartment.Id))
                            matches.Add(compartment);
                    }
                }

                page = response.OpcNextPage;
            }
            while (!string.IsNullOrWhiteSpace(page));
        }
        catch (Exception ex)
        {
            notes.Add(OciErrorFormatter.Format("ListCompartments", ex));
        }

        return matches;
    }

    private static async Task<List<ConnectExistingCandidate>> ScanCompartmentBucketsAsync(
        OciSession session,
        OciConfigProfile profile,
        string configPath,
        string ns,
        Compartment compartment,
        List<string> notes,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var found = new List<ConnectExistingCandidate>();
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        progress?.Report($"Looking for {GreenfieldBucketName} in {compartment.Name}…");
        var greenfield = await TryReadMetaAsync(
            session, ns, GreenfieldBucketName, profile, configPath, compartment, cancellationToken)
            .ConfigureAwait(false);
        tried.Add(GreenfieldBucketName);
        if (greenfield.Candidate is not null)
            found.Add(greenfield.Candidate);
        else if (!string.IsNullOrWhiteSpace(greenfield.Note))
            notes.Add(greenfield.Note);

        progress?.Report($"Listing buckets in {compartment.Name}…");
        IReadOnlyList<string> bucketNames;
        try
        {
            bucketNames = await ListBucketNamesAsync(session, ns, compartment.Id, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            notes.Add($"{compartment.Name}: {OciErrorFormatter.Format("ListBuckets", ex)}");
            return found;
        }

        foreach (var bucket in bucketNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!tried.Add(bucket))
                continue;

            progress?.Report($"Checking {bucket} for {InfraObjectName}…");
            var read = await TryReadMetaAsync(
                session, ns, bucket, profile, configPath, compartment, cancellationToken)
                .ConfigureAwait(false);
            if (read.Candidate is not null)
                found.Add(read.Candidate);
            else if (!string.IsNullOrWhiteSpace(read.Note)
                     && !read.MissingObject)
            {
                notes.Add(read.Note);
            }
        }

        if (found.Count == 0)
        {
            notes.Add(
                $"Profile '{profile.Name}' compartment '{compartment.Name}': no {InfraObjectName} found.");
        }

        return found;
    }

    private static async Task<(ConnectExistingCandidate? Candidate, string? Note, bool MissingObject)> TryReadMetaAsync(
        OciSession session,
        string ns,
        string bucket,
        OciConfigProfile profile,
        string configPath,
        Compartment compartment,
        CancellationToken cancellationToken)
    {
        var bytes = await GetObjectBytesAsync(session, ns, bucket, InfraObjectName, cancellationToken)
            .ConfigureAwait(false);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            var missing = OciErrorFormatter.IsNotFoundMessage(bytes.Error);
            return (null, missing ? null : $"{bucket}: {bytes.Error}", missing);
        }

        var parsed = InfraMetaStore.ParseForConnect(bytes.Value);
        if (!parsed.Succeeded || parsed.Value is null)
            return (null, $"{bucket}: {parsed.Error}", false);

        var read = parsed.Value;
        if (read.Skipped || read.Document is null)
            return (null, $"{bucket}: {read.Notes}", false);

        var doc = read.Document;
        if (string.IsNullOrWhiteSpace(doc.ObjectStorage.Namespace))
            doc.ObjectStorage.Namespace = ns;
        if (string.IsNullOrWhiteSpace(doc.ObjectStorage.Bucket))
            doc.ObjectStorage.Bucket = bucket;
        if (string.IsNullOrWhiteSpace(doc.Region))
            doc.Region = profile.Region;
        if (string.IsNullOrWhiteSpace(doc.TenancyId))
            doc.TenancyId = profile.Tenancy;
        if (string.IsNullOrWhiteSpace(doc.CompartmentId))
            doc.CompartmentId = compartment.Id;

        var ociConfig = string.IsNullOrWhiteSpace(configPath) ? DefaultOciConfigFile : DefaultOciConfigFile;
        return (new ConnectExistingCandidate
        {
            ProfileName = profile.Name,
            OciConfigFile = ociConfig,
            Region = string.IsNullOrWhiteSpace(doc.Region) ? profile.Region : doc.Region,
            TenancyId = doc.TenancyId,
            CompartmentId = doc.CompartmentId,
            CompartmentName = compartment.Name ?? "",
            Namespace = doc.ObjectStorage.Namespace,
            Bucket = doc.ObjectStorage.Bucket,
            BucketId = doc.ObjectStorage.BucketId,
            Document = doc,
            IsLegacy = read.IsLegacy,
            SchemaWarnings = read.SchemaWarnings,
        }, read.Notes, false);
    }

    private static async Task<IReadOnlyList<string>> ListBucketNamesAsync(
        OciSession session,
        string ns,
        string compartmentId,
        CancellationToken cancellationToken)
    {
        var names = new List<string>();
        string? page = null;
        do
        {
            var response = await session.ObjectStorage.ListBuckets(
                new ListBucketsRequest
                {
                    NamespaceName = ns,
                    CompartmentId = compartmentId,
                    Page = page,
                },
                retryConfiguration: session.RetryConfiguration,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (response.Items is not null)
            {
                foreach (var bucket in response.Items)
                {
                    if (!string.IsNullOrWhiteSpace(bucket.Name))
                        names.Add(bucket.Name);
                }
            }

            page = response.OpcNextPage;
        }
        while (!string.IsNullOrWhiteSpace(page));

        return names;
    }

    private static async Task<ServiceResult<string>> GetNamespaceAsync(
        OciSession session,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await session.ObjectStorage.GetNamespace(
                new GetNamespaceRequest(),
                retryConfiguration: session.RetryConfiguration,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var ns = response.Value?.Trim();
            if (string.IsNullOrWhiteSpace(ns))
                return ServiceResult<string>.Fail("GetNamespace returned empty.");
            return ServiceResult<string>.Ok(ns);
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(OciErrorFormatter.Format("GetNamespace", ex));
        }
    }

    private static async Task<ServiceResult<byte[]>> GetObjectBytesAsync(
        OciSession session,
        string ns,
        string bucket,
        string objectName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await session.ObjectStorage.GetObject(
                new GetObjectRequest
                {
                    NamespaceName = ns,
                    BucketName = bucket,
                    ObjectName = objectName,
                },
                retryConfiguration: session.RetryConfiguration,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            await using var stream = response.InputStream;
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
            return ServiceResult<byte[]>.Ok(memory.ToArray());
        }
        catch (Exception ex)
        {
            return ServiceResult<byte[]>.Fail(OciErrorFormatter.Format("GetObject", ex));
        }
    }
}

public sealed class ConnectExistingScanResult
{
    public IReadOnlyList<ConnectExistingCandidate> Candidates { get; init; } = [];
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public sealed class ConnectExistingCandidate
{
    public required string ProfileName { get; init; }
    public required string OciConfigFile { get; init; }
    public required string Region { get; init; }
    public required string TenancyId { get; init; }
    public required string CompartmentId { get; init; }
    public required string CompartmentName { get; init; }
    public required string Namespace { get; init; }
    public required string Bucket { get; init; }
    public string BucketId { get; init; } = "";
    public InfraMetaDocument? Document { get; init; }
    public bool IsLegacy { get; init; }
    public IReadOnlyList<string> SchemaWarnings { get; init; } = [];

    public string DedupeKey =>
        $"{TenancyId}|{CompartmentId}|{Bucket}";

    public ConnectExistingDecision Compatibility =>
        ConnectExistingCompatibility.Evaluate(this);

    public bool HasSchemaWarning => Compatibility.RequiresConfirm;
    public bool IsIncompatible => Compatibility.BlocksConnect;

    public string ChooserLabel
    {
        get
        {
            var play = Document?.Play.ReservedPublicIp;
            var vm1 = Document?.Vm1.DisplayName;
            var warn = Compatibility.Level switch
            {
                ConnectExistingCompatibilityLevel.Block => " (incompatible)",
                ConnectExistingCompatibilityLevel.Warn => " (version warning)",
                _ => "",
            };
            return
                $"{ProfileName} · {Region} · {CompartmentName} · play {play} · {vm1} · {Bucket}{warn}";
        }
    }

    public string IdentitySummary =>
        Document is null
            ? $"Profile: {ProfileName}\nRegion: {Region}\nCompartment: {CompartmentName}\nBucket: {Bucket}"
            : Document.FormatConnectSummary(ProfileName, CompartmentName);

    public string ConfirmSummary
    {
        get
        {
            var body = IdentitySummary;
            if (SchemaWarnings.Count == 0)
                return body;
            return body + "\n\nWarnings:\n- " + string.Join("\n- ", SchemaWarnings);
        }
    }
}
