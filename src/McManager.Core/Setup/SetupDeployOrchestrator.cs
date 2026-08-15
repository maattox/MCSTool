using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;
using McManager.Core.Usage;

namespace McManager.Core.Setup;

public sealed class SetupDeployResult
{
    public bool Succeeded { get; init; }
    public bool CapacityWait { get; init; }
    public string Stage { get; init; } = SetupApplyStage.NotStarted;
    public string Message { get; init; } = "";
    public string? FunctionSkipReason { get; init; }
    public TofuApplyOutputs? Outputs { get; init; }

    public static SetupDeployResult Ok(string stage, string message, TofuApplyOutputs? outputs = null, string? functionSkip = null) =>
        new()
        {
            Succeeded = true,
            Stage = stage,
            Message = message,
            Outputs = outputs,
            FunctionSkipReason = functionSkip,
        };

    public static SetupDeployResult Fail(string stage, string message) =>
        new() { Stage = stage, Message = message };

    public static SetupDeployResult Capacity(string message) =>
        new()
        {
            CapacityWait = true,
            Stage = SetupApplyStage.NotStarted,
            Message = message,
        };
}

/// <summary>Resumable Setup apply pipeline. Dry-run via <c>MCMANAGER_TOFU_DRY_RUN=1</c> uses a fake tofu runner and skips live SSH/OCI.</summary>
public sealed class SetupDeployOrchestrator
{
    private readonly IOpenTofuRunner _tofu;
    private readonly SetupBootstrapService _bootstrap;
    private readonly bool _dryRun;

    public SetupDeployOrchestrator(
        IOpenTofuRunner? tofu = null,
        SetupBootstrapService? bootstrap = null,
        bool? dryRun = null)
    {
        _dryRun = dryRun ?? ProductPaths.IsTofuDryRun();
        _bootstrap = bootstrap ?? new SetupBootstrapService();
        if (tofu is not null)
        {
            _tofu = tofu;
        }
        else if (_dryRun)
        {
            _tofu = new RecordingOpenTofuRunner();
        }
        else
        {
            var path = OpenTofuLocator.Find()
                       ?? throw new InvalidOperationException(OpenTofuLocator.MissingMessage());
            _tofu = new OpenTofuRunner(path);
        }
    }

    public async Task<SetupDeployResult> RunAsync(
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        if (!state.EulaAccepted)
            return SetupDeployResult.Fail(state.ApplyStage, "EULA is not accepted.");

        var cidr = TfvarsWriter.NormalizeAdminCidr(state.AdminCidr);
        if (cidr is null)
            return SetupDeployResult.Fail(state.ApplyStage, "Admin public IP /32 is required.");

        if (!MinecraftUsername.IsMissingOrValid(state.AdminMinecraftUsername))
        {
            return SetupDeployResult.Fail(
                state.ApplyStage,
                "Admin Minecraft username must be empty or 3–16 letters, digits, or underscore (optional; joins use OCI Security List).");
        }

        var infra = ProductPaths.FindInfraDirectory();
        if (infra is null)
            return SetupDeployResult.Fail(state.ApplyStage, "Could not find product infra/ (main.tf).");

        var stackId = state.CreateCompartment ? state.CompartmentName : TofuWorkspace.DefaultStackId;
        if (_dryRun)
            stackId += "-dry";
        var workspace = TofuWorkspace.ForStack(stackId);

        var tfvars = TfvarsWriter.Write(workspace, state, state.FunctionImage);
        if (!tfvars.Succeeded)
            return SetupDeployResult.Fail(state.ApplyStage, tfvars.Error ?? "tfvars write failed.");

        log?.Report($"Wrote {workspace.VarFilePath} (not repo infra/terraform.tfvars).");

        TofuApplyOutputs? outputs = null;
        var stage = state.ApplyStage;

        if (!SetupApplyStage.Reached(stage, SetupApplyStage.TofuApplied))
        {
            var init = await _tofu.InitAsync(infra, log, cancellationToken).ConfigureAwait(false);
            if (!init.Succeeded)
                return SetupDeployResult.Fail(stage, "tofu init failed. See the deploy log.");

            if (!_dryRun)
            {
                var probe = await SetupCapacityChecker.CheckVm1Async(state, log, cancellationToken)
                    .ConfigureAwait(false);
                if (probe.OutOfCapacity)
                    return SetupDeployResult.Capacity(probe.Message);
                if (probe.Unsupported)
                    return SetupDeployResult.Fail(stage, probe.Message);
            }

            var apply = await _tofu.ApplyAsync(infra, workspace, log, cancellationToken).ConfigureAwait(false);
            if (apply.IsCapacityError)
            {
                return SetupDeployResult.Capacity(
                    "Always Free A1 Flex host capacity is unavailable in this region right now. "
                    + "VM1 was not created. Retry reuses any compartment/VCN/door already in OpenTofu state.");
            }

            if (!apply.Succeeded)
                return SetupDeployResult.Fail(stage, "tofu apply failed. See the deploy log.");

            var raw = await _tofu.OutputJsonAsync(infra, workspace, log, cancellationToken).ConfigureAwait(false);
            if (!raw.Succeeded)
                return SetupDeployResult.Fail(stage, "tofu output failed. See the deploy log.");

            File.WriteAllText(workspace.OutputsPath, raw.Output);
            var parsed = TofuApplyOutputs.Parse(raw.Output);
            if (!parsed.Succeeded || parsed.Value is null)
                return SetupDeployResult.Fail(stage, parsed.Error ?? "output parse failed.");

            outputs = parsed.Value;
            stage = SetupApplyStage.TofuApplied;
            PersistStage(state, stage);
        }
        else
        {
            outputs = LoadOutputs(workspace);
            if (outputs is null)
                return SetupDeployResult.Fail(stage, "Saved tofu outputs missing; cannot resume.");
        }

        if (_dryRun)
        {
            log?.Report("[dry-run] Skipping cloud-init wait, SSH bootstrap, Object Storage, and config.local.json write.");
            log?.Report("[dry-run] apply_stage left unchanged so a later real Deploy still runs tofu apply.");
            return SetupDeployResult.Ok(
                state.ApplyStage,
                "Dry-run finished. No OCI resources were created and config.local.json was not written. "
                    + "Unset MCMANAGER_TOFU_DRY_RUN for a real operator apply.",
                outputs,
                "dry-run");
        }

        if (!SetupApplyStage.Reached(stage, SetupApplyStage.CloudInit))
        {
            var waitVm = await WaitRunningAsync(outputs, state, log, cancellationToken).ConfigureAwait(false);
            if (!waitVm.Succeeded)
                return SetupDeployResult.Fail(stage, waitVm.Error ?? "Wait RUNNING failed.");

            var key = TofuApplyOutputs.PrivateKeyPath(state);
            var c1 = await _bootstrap.WaitCloudInitAsync(
                outputs.Vm1SshHost, outputs.SshUser, key, "/etc/mcmgr/cloud-init-done", log, cancellationToken)
                .ConfigureAwait(false);
            if (!c1.Succeeded)
                return SetupDeployResult.Fail(stage, c1.Error ?? "VM1 cloud-init wait failed.");

            var c2 = await _bootstrap.WaitCloudInitAsync(
                outputs.DoorSshHost, outputs.SshUser, key, "/etc/mcmgr-door/cloud-init-done", log, cancellationToken)
                .ConfigureAwait(false);
            if (!c2.Succeeded)
                return SetupDeployResult.Fail(stage, c2.Error ?? "Door cloud-init wait failed.");

            stage = SetupApplyStage.CloudInit;
            PersistStage(state, stage);
        }

        if (!SetupApplyStage.Reached(stage, SetupApplyStage.Door))
        {
            var door = await _bootstrap.DeployDoorAsync(outputs, state, log, cancellationToken).ConfigureAwait(false);
            if (!door.Succeeded)
                return SetupDeployResult.Fail(stage, door.Error ?? "Door bootstrap failed.");
            stage = SetupApplyStage.Door;
            PersistStage(state, stage);
        }

        if (!SetupApplyStage.Reached(stage, SetupApplyStage.Vm1))
        {
            var vm1 = await _bootstrap.DeployVm1Async(outputs, state, log, cancellationToken).ConfigureAwait(false);
            if (!vm1.Succeeded)
                return SetupDeployResult.Fail(stage, vm1.Error ?? "VM1 bootstrap failed.");
            stage = SetupApplyStage.Vm1;
            PersistStage(state, stage);
        }

        var running = await EnsureVm1RunningForSshAsync(outputs, state, log, cancellationToken)
            .ConfigureAwait(false);
        if (!running.Succeeded)
            return SetupDeployResult.Fail(stage, running.Error ?? "VM1 start/wait failed.");

        var guest = await _bootstrap.EnsureGuestRuntimeAsync(outputs, state, log, cancellationToken)
            .ConfigureAwait(false);
        if (!guest.Succeeded)
            return SetupDeployResult.Fail(stage, guest.Error ?? "Guest runtime repair failed.");

        var rcon = "";
        var secret = await _bootstrap.PullRconSecretAsync(outputs, state, cancellationToken).ConfigureAwait(false);
        if (secret.Succeeded)
            rcon = (secret.Value ?? "").Trim();

        string? mcVersion = state.MinecraftVersion;
        var manifest = await _bootstrap.PullGameManifestAsync(outputs, state, cancellationToken).ConfigureAwait(false);
        if (manifest.Succeeded && !string.IsNullOrWhiteSpace(manifest.Value))
        {
            try
            {
                using var doc = JsonDocument.Parse(manifest.Value);
                if (doc.RootElement.TryGetProperty("minecraft_version", out var v))
                    mcVersion = v.GetString() ?? mcVersion;
            }
            catch
            {
                // keep wizard version id
            }
        }

        var config = outputs.ToLocalConfig(state, rcon);

        if (!SetupApplyStage.Reached(stage, SetupApplyStage.OsMeta))
        {
            log?.Report("Seeding Object Storage (budget/config.json, ledger/usage.json, meta/infra.json)…");
            var os = await SeedObjectStorageAsync(config, mcVersion, log, cancellationToken).ConfigureAwait(false);
            if (!os.Succeeded)
            {
                log?.Report("Object Storage seed failed: " + (os.Error ?? "unknown error"));
                return SetupDeployResult.Fail(stage, os.Error ?? "Object Storage seed failed.");
            }

            stage = SetupApplyStage.OsMeta;
            PersistStage(state, stage);
        }

        string? fnSkip = null;
        if (!SetupApplyStage.Reached(stage, SetupApplyStage.Function))
        {
            var push = await OcirFunctionPublisher.TryPushAsync(outputs, state, log, cancellationToken)
                .ConfigureAwait(false);
            if (push.Succeeded && !string.IsNullOrWhiteSpace(push.Value))
            {
                state.FunctionImage = push.Value;
                var rewrite = TfvarsWriter.Write(workspace, state, push.Value);
                if (!rewrite.Succeeded)
                    return SetupDeployResult.Fail(stage, rewrite.Error ?? "tfvars rewrite failed.");
                var apply2 = await _tofu.ApplyAsync(infra, workspace, log, cancellationToken).ConfigureAwait(false);
                if (!apply2.Succeeded)
                {
                    fnSkip = "Function image pushed but second tofu apply failed: " + apply2.Output;
                    log?.Report(fnSkip);
                }
            }
            else
            {
                fnSkip = push.Error ?? "Function image skipped.";
                log?.Report(fnSkip);
            }

            stage = SetupApplyStage.Function;
            PersistStage(state, stage);
        }

        var saved = LocalConfigStore.SaveConfig(config);
        if (!saved.Succeeded)
            return SetupDeployResult.Fail(stage, saved.Error ?? "Failed to save config.local.json.");

        SeedAdminFriend(state);
        stage = SetupApplyStage.ConfigWritten;
        PersistStage(state, stage);

        return SetupDeployResult.Ok(
            stage,
            "Setup finished. Local config written. "
            + (fnSkip is null
                ? "Function image applied."
                : "Function image skipped or second apply failed — see the deploy log."),
            outputs,
            fnSkip);
    }

    private static async Task<ServiceResult> WaitRunningAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var session = OciSession.TryCreate(outputs.ToLocalConfig(state, ""));
        if (!session.Succeeded || session.Value is null)
            return ServiceResult.Fail(session.Error ?? "OCI session failed after apply.");

        using var s = session.Value;
        var compute = new ComputeService(s);
        log?.Report("Waiting for VM1 RUNNING…");
        var vm1 = await compute.WaitForLifecycleAsync(outputs.Vm1InstanceId, "RUNNING", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!vm1.Succeeded)
            return ServiceResult.Fail(vm1.Error ?? "VM1 wait failed.");

        log?.Report("Waiting for door RUNNING…");
        var door = await compute.WaitForLifecycleAsync(outputs.DoorInstanceId, "RUNNING", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return door.Succeeded ? ServiceResult.Ok() : ServiceResult.Fail(door.Error ?? "Door wait failed.");
    }

    private static async Task<ServiceResult> EnsureVm1RunningForSshAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var session = OciSession.TryCreate(outputs.ToLocalConfig(state, ""));
        if (!session.Succeeded || session.Value is null)
            return ServiceResult.Fail(session.Error ?? "OCI session failed before guest repair.");

        using var s = session.Value;
        var compute = new ComputeService(s);
        var life = await compute.GetLifecycleStateAsync(outputs.Vm1InstanceId, cancellationToken)
            .ConfigureAwait(false);
        if (!life.Succeeded)
            return ServiceResult.Fail(life.Error ?? "Get VM1 lifecycle failed.");

        var current = (life.Value ?? "").ToUpperInvariant();
        if (current is "STOPPING")
        {
            log?.Report("Waiting for VM1 STOPPED before start…");
            var stopped = await compute.WaitForLifecycleAsync(
                    outputs.Vm1InstanceId, "STOPPED", cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!stopped.Succeeded)
                return ServiceResult.Fail(stopped.Error ?? "Wait STOPPED failed.");
            current = "STOPPED";
        }

        if (current is "STOPPED")
        {
            log?.Report("VM1 is stopped; starting so Setup can finish on-box repair…");
            var start = await compute.StartInstanceAsync(outputs.Vm1InstanceId, cancellationToken)
                .ConfigureAwait(false);
            if (!start.Succeeded)
                return ServiceResult.Fail(start.Error ?? "VM1 start failed.");
        }

        log?.Report("Waiting for VM1 RUNNING…");
        var running = await compute.WaitForLifecycleAsync(
                outputs.Vm1InstanceId, "RUNNING", cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return running.Succeeded
            ? ServiceResult.Ok()
            : ServiceResult.Fail(running.Error ?? "VM1 wait RUNNING failed.");
    }

    private static async Task<ServiceResult> SeedObjectStorageAsync(
        ManagerLocalConfig config,
        string? minecraftVersion,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var session = OciSession.TryCreate(config);
        if (!session.Succeeded || session.Value is null)
            return ServiceResult.Fail(session.Error ?? "OCI session failed for Object Storage seed.");

        using var s = session.Value;
        var os = new ObjectStorageService(s, config.ObjectStorage);
        var budgetStore = new UsageBudgetStore(os, config.ObjectStorage.Prefixes);
        var infraStore = new InfraMetaStore(os, config.ObjectStorage.Prefixes);

        var budget = BudgetConfigDocument.FromLocal(config.Budget, config.Vm1);
        var pubBudget = await budgetStore.PublishBudgetAsync(budget, cancellationToken).ConfigureAwait(false);
        if (!pubBudget.Succeeded)
        {
            log?.Report("Publish budget/config.json failed: " + (pubBudget.Error ?? "unknown"));
            return ServiceResult.Fail(pubBudget.Error ?? "Publish budget failed.");
        }

        var ledger = await budgetStore.SeedEmptyLedgerIfMissingAsync(cancellationToken).ConfigureAwait(false);
        if (!ledger.Succeeded)
        {
            log?.Report("Seed ledger/usage.json failed: " + (ledger.Error ?? "unknown"));
            return ServiceResult.Fail(ledger.Error ?? "Seed ledger failed.");
        }

        var pubMeta = await infraStore.PublishFromLocalAsync(
            config,
            stackVersion: InfraMetaDocument.DefaultStackVersion,
            serverKind: "vanilla",
            minecraftVersion: minecraftVersion ?? "unspecified",
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (!pubMeta.Succeeded)
        {
            log?.Report("Publish meta/infra.json failed: " + (pubMeta.Error ?? "unknown"));
            return ServiceResult.Fail(pubMeta.Error ?? "Publish meta/infra.json failed.");
        }

        log?.Report("Published budget/config.json, ledger/usage.json, and meta/infra.json.");
        return ServiceResult.Ok();
    }

    private static void SeedAdminFriend(SetupWizardState state)
    {
        var cidr = TfvarsWriter.NormalizeAdminCidr(state.AdminCidr);
        if (cidr is null)
            return;
        var ip = cidr.EndsWith("/32", StringComparison.Ordinal) ? cidr[..^3] : cidr;
        var existing = LocalConfigStore.Load().Friends;
        if (existing?.Friends is { Count: > 0 })
            return;

        LocalConfigStore.SaveFriends(new FriendsLocalFile
        {
            SchemaVersion = 1,
            Friends =
            [
                new FriendEntry
                {
                    Id = Guid.NewGuid().ToString("N")[..12],
                    Name = "admin",
                    Ip = ip,
                    IsAdmin = true,
                },
            ],
        });
    }

    private static TofuApplyOutputs? LoadOutputs(TofuWorkspace workspace)
    {
        if (!File.Exists(workspace.OutputsPath))
            return null;
        var parsed = TofuApplyOutputs.Parse(File.ReadAllText(workspace.OutputsPath));
        return parsed.Succeeded ? parsed.Value : null;
    }

    private static void PersistStage(SetupWizardState state, string stage)
    {
        state.ApplyStage = stage;
        SetupWizardStore.Save(state);
    }
}
