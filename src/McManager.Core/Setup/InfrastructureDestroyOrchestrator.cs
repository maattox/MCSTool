using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Tears down OpenTofu-managed product resources (state under LocalAppData).
/// Does not delete the Oracle tenancy. Best-effort first: empty the product bucket,
/// purge OCIR images, and delete leftover Functions/Events that would block
/// <c>DeleteApplication</c> even when they were never imported into tofu state.
/// Dry-run via <c>MCMANAGER_TOFU_DRY_RUN=1</c> never calls live destroy.
/// </summary>
public sealed class InfrastructureDestroyOrchestrator
{
    public const string ConfirmPhrase = "confirm";

    private IOpenTofuRunner? _tofu;
    private readonly bool _dryRun;

    public InfrastructureDestroyOrchestrator(
        IOpenTofuRunner? tofu = null,
        bool? dryRun = null)
    {
        _dryRun = dryRun ?? ProductPaths.IsTofuDryRun();
        if (tofu is not null)
        {
            _tofu = tofu;
        }
        else if (_dryRun)
        {
            _tofu = new RecordingOpenTofuRunner();
        }
    }

    private IOpenTofuRunner Tofu =>
        _tofu ?? throw new InvalidOperationException(OpenTofuLocator.MissingMessage());

    private async Task<string?> EnsureTofuAsync(IProgress<string>? log, CancellationToken cancellationToken)
    {
        if (_tofu is not null)
            return null;
        try
        {
            _tofu = await OpenTofuLocator.CreateRunnerAsync(log, cancellationToken).ConfigureAwait(false);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    public async Task<InfrastructureDestroyResult> RunAsync(
        IProgress<string>? log,
        CancellationToken cancellationToken = default,
        IProgress<DestroyProgressUpdate>? progress = null)
    {
        Report(progress, 2, "Finding OpenTofu state…");

        var infra = ProductPaths.FindInfraDirectory();
        if (infra is null)
            return InfrastructureDestroyResult.Fail("Could not find product infra/ (main.tf).");

        var workspace = ResolveWorkspace(log);
        if (workspace is null)
        {
            return InfrastructureDestroyResult.Fail(
                "No OpenTofu state on this PC. Destroy only removes resources this Manager "
                + "deployed (from %LOCALAPPDATA%\\McManager\\tofu). Oracle’s default tenancy "
                + "resources are never touched. If Setup ran on another PC, copy that tofu "
                + "folder here, or delete leftover product resources in the OCI Console.");
        }

        var tofuError = await EnsureTofuAsync(log, cancellationToken).ConfigureAwait(false);
        if (tofuError is not null)
            return InfrastructureDestroyResult.Fail(tofuError);

        log?.Report($"Using OpenTofu state: {workspace.StatePath}");
        Report(progress, 8, "Emptying shared storage…");

        if (_dryRun)
        {
            log?.Report("[dry-run] Skipping Object Storage empty, OCIR purge, Functions/Events purge, tofu destroy, and local file deletes.");
            Report(progress, 40, "Planning deletion (dry-run)…");
            var dryPlan = await Tofu.PlanDestroyAsync(infra, workspace, log, cancellationToken)
                .ConfigureAwait(false);
            if (!dryPlan.Succeeded)
                return InfrastructureDestroyResult.Fail("Dry-run plan failed. See the log.");
            Report(progress, 70, "Destroying cloud resources (dry-run)…");
            var dryDestroy = await Tofu.DestroyAsync(infra, workspace, log, cancellationToken)
                .ConfigureAwait(false);
            if (!dryDestroy.Succeeded)
                return InfrastructureDestroyResult.Fail("Dry-run destroy failed. See the log.");
            Report(progress, 100, "Dry-run finished");
            return InfrastructureDestroyResult.Ok(
                "Dry-run finished. No Oracle resources or local config files were deleted.");
        }

        var session = TryCreateSession(log);
        await EmptyBucketAsync(session, workspace, log, cancellationToken).ConfigureAwait(false);
        Report(progress, 18, "Removing Function images…");
        await PurgeOcirAsync(session, workspace, log, cancellationToken).ConfigureAwait(false);
        Report(progress, 21, "Removing leftover Functions…");
        await PurgeFunctionsEventsAsync(session, workspace, log, cancellationToken).ConfigureAwait(false);

        Report(progress, 24, "Allowing bucket delete…");
        BucketDestroyOverride.Install(infra);
        try
        {
            Report(progress, 28, "Initializing OpenTofu…");
            var init = await Tofu.InitAsync(infra, log, cancellationToken).ConfigureAwait(false);
            if (!init.Succeeded)
                return InfrastructureDestroyResult.Fail("tofu init failed. See the log.");

            var tracker = new TofuDestroyProgress();
            var trackedLog = new Progress<string>(line =>
            {
                tracker.Observe(line);
                log?.Report(line);
                var mapped = 35 + (int)Math.Round(55.0 * tracker.PercentOfDestroyPhase / 100.0);
                Report(progress, mapped, "Waiting for Oracle to finish deleting resources…");
            });

            Report(progress, 32, "Planning deletion…");
            var plan = await Tofu.PlanDestroyAsync(infra, workspace, trackedLog, cancellationToken)
                .ConfigureAwait(false);
            if (!plan.Succeeded)
                return InfrastructureDestroyResult.Fail("tofu plan -destroy failed. See the log.");

            tracker.Observe(plan.Output);
            log?.Report(
                tracker.ToDestroy > 0
                    ? $"OpenTofu will destroy {tracker.ToDestroy} managed resource(s). This can take several minutes."
                    : "OpenTofu destroy plan had no resource count; continuing.");

            Report(progress, 35, "Destroying cloud resources…");
            var destroy = await Tofu.DestroyAsync(infra, workspace, trackedLog, cancellationToken)
                .ConfigureAwait(false);
            if (!destroy.Succeeded)
            {
                return InfrastructureDestroyResult.Fail(
                    "tofu destroy failed. Local config was kept so you can retry. See the log.");
            }

            tracker.Observe(destroy.Output);
        }
        finally
        {
            try
            {
                BucketDestroyOverride.Remove(infra);
            }
            catch (Exception ex)
            {
                log?.Report($"Could not remove bucket destroy override: {ex.Message}");
            }
        }

        Report(progress, 92, "Removing local stack files…");
        DeleteLocalStackFiles(workspace, log);

        Report(progress, 100, "Deletion finished");
        return InfrastructureDestroyResult.Ok(
            "Product cloud infrastructure is gone. This did not close your Oracle account. "
            + "Close Manager fully, reopen it, then run Setup to deploy a fresh stack.");
    }

    internal static TofuWorkspace? ResolveWorkspace(IProgress<string>? log)
    {
        var existing = TofuWorkspace.ListExisting();
        if (existing.Count == 0)
            return null;

        var wizard = SetupWizardStore.LoadOrNew();
        var preferredName = wizard.CreateCompartment
            ? (string.IsNullOrWhiteSpace(wizard.CompartmentName) ? TofuWorkspace.DefaultStackId : wizard.CompartmentName)
            : TofuWorkspace.DefaultStackId;
        var preferred = TofuWorkspace.Sanitize(preferredName);

        var match = existing.FirstOrDefault(w =>
            string.Equals(w.StackId, preferred, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        match = existing.FirstOrDefault(w =>
            string.Equals(w.StackId, TofuWorkspace.DefaultStackId, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
            return match;

        if (existing.Count == 1)
            return existing[0];

        log?.Report(
            "Multiple OpenTofu stacks found: "
            + string.Join(", ", existing.Select(w => w.StackId))
            + $". Using {existing[0].StackId}.");
        return existing[0];
    }

    private static OciSession? TryCreateSession(IProgress<string>? log)
    {
        var loaded = LocalConfigStore.Load();
        if (loaded.Succeeded && loaded.Config is not null)
        {
            var created = OciSession.TryCreate(loaded.Config);
            if (created.Succeeded)
                return created.Value;
            log?.Report(created.Error ?? "Could not open an OCI session from local config.");
        }

        var wizard = SetupWizardStore.LoadOrNew();
        if (!string.IsNullOrWhiteSpace(wizard.OciProfile) && !string.IsNullOrWhiteSpace(wizard.OciRegion))
        {
            var created = OciSession.TryCreate(
                OciConfigProfiles.DefaultConfigPath(),
                wizard.OciProfile,
                wizard.OciRegion);
            if (created.Succeeded)
                return created.Value;
            log?.Report(created.Error ?? "Could not open an OCI session from Setup profile.");
        }

        return null;
    }

    private static async Task EmptyBucketAsync(
        OciSession? session,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            log?.Report("Skipping Object Storage empty (no OCI session). tofu destroy may fail if the bucket still has objects.");
            return;
        }

        var settings = ResolveBucketSettings(workspace);
        if (settings is null)
        {
            log?.Report("Skipping Object Storage empty (no namespace/bucket in local config or tofu outputs).");
            return;
        }

        log?.Report($"Emptying bucket {settings.Bucket} (world backups in the cloud will be deleted)…");
        var os = new ObjectStorageService(session, settings);
        var result = await os.DeleteAllObjectsAsync(cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            log?.Report(result.Error ?? "Failed to empty the bucket. Continuing with tofu destroy.");
            return;
        }

        log?.Report($"Deleted {result.Value} object(s) from shared storage.");
    }

    private static async Task PurgeOcirAsync(
        OciSession? session,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            log?.Report("Skipping OCIR image purge (no OCI session).");
            return;
        }

        var compartmentId = ResolveCompartmentId(workspace);
        if (string.IsNullOrWhiteSpace(compartmentId))
        {
            log?.Report("Skipping OCIR image purge (no compartment OCID).");
            return;
        }

        var result = await OcirImagePurger.DeleteProductImagesAsync(
            session, compartmentId, log, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            log?.Report(result.Error ?? "OCIR image purge failed. Continuing; tofu destroy may fail on the container repo.");
            return;
        }

        log?.Report($"Deleted {result.Value} OCIR image(s) from {OcirImagePurger.ProductRepositoryName}.");
    }

    private static async Task PurgeFunctionsEventsAsync(
        OciSession? session,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (session is null)
        {
            log?.Report("Skipping Functions/Events purge (no OCI session).");
            return;
        }

        var compartmentId = ResolveCompartmentId(workspace);
        if (string.IsNullOrWhiteSpace(compartmentId))
        {
            log?.Report("Skipping Functions/Events purge (no compartment OCID).");
            return;
        }

        var result = await FunctionsEventsPurger.PurgeProductLeftoversAsync(
            session, compartmentId, log, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            log?.Report(
                result.Error
                ?? "Functions/Events purge failed. Continuing; tofu destroy may fail on mcmgr-fn-app.");
            return;
        }

        var counts = result.Value;
        log?.Report(
            $"Deleted {counts.FunctionsDeleted} leftover Function(s) and {counts.EventsDeleted} Events rule(s).");
    }

    private static ObjectStorageSettings? ResolveBucketSettings(TofuWorkspace workspace)
    {
        var loaded = LocalConfigStore.Load();
        if (loaded.Succeeded && loaded.Config is not null
            && !string.IsNullOrWhiteSpace(loaded.Config.ObjectStorage.Namespace)
            && !string.IsNullOrWhiteSpace(loaded.Config.ObjectStorage.Bucket))
        {
            return loaded.Config.ObjectStorage;
        }

        if (!File.Exists(workspace.OutputsPath))
            return null;

        try
        {
            var parsed = TofuApplyOutputs.Parse(File.ReadAllText(workspace.OutputsPath));
            if (!parsed.Succeeded || parsed.Value is null)
                return null;
            if (string.IsNullOrWhiteSpace(parsed.Value.ObjectStorageNamespace)
                || string.IsNullOrWhiteSpace(parsed.Value.ObjectStorageBucket))
            {
                return null;
            }

            return new ObjectStorageSettings
            {
                Namespace = parsed.Value.ObjectStorageNamespace,
                Bucket = parsed.Value.ObjectStorageBucket,
                BucketId = parsed.Value.ObjectStorageBucketId,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ResolveCompartmentId(TofuWorkspace workspace)
    {
        var loaded = LocalConfigStore.Load();
        if (loaded.Succeeded && !string.IsNullOrWhiteSpace(loaded.Config?.Oci.CompartmentId))
            return loaded.Config!.Oci.CompartmentId;

        if (!File.Exists(workspace.OutputsPath))
            return null;

        try
        {
            var parsed = TofuApplyOutputs.Parse(File.ReadAllText(workspace.OutputsPath));
            return parsed.Succeeded ? parsed.Value?.CompartmentId : null;
        }
        catch
        {
            return null;
        }
    }

    private static void DeleteLocalStackFiles(TofuWorkspace workspace, IProgress<string>? log)
    {
        var local = LocalConfigStore.DeleteManageConfigAndWizard();
        if (!local.Succeeded)
            log?.Report(local.Error ?? "Could not delete local manage config.");
        else
            log?.Report("Removed data/config.local.json and data/setup-wizard.local.json (friends list kept).");

        try
        {
            if (Directory.Exists(workspace.RootDirectory))
            {
                Directory.Delete(workspace.RootDirectory, recursive: true);
                log?.Report($"Removed {workspace.RootDirectory}");
            }
        }
        catch (Exception ex)
        {
            log?.Report($"Could not delete OpenTofu workspace folder: {ex.Message}");
        }
    }

    private static void Report(IProgress<DestroyProgressUpdate>? progress, int percent, string caption) =>
        progress?.Report(new DestroyProgressUpdate(Math.Clamp(percent, 0, 100), caption));
}
