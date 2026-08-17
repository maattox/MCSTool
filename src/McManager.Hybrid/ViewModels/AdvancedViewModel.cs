using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Advanced / Danger Zone: technical VM/door status, break-glass Compute, idle
/// timeout/enable, infra meta publish, Auto-detect, Deploy/repair → Setup wizard,
/// typed-confirm delete infrastructure.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart.
/// </summary>
public sealed partial class AdvancedViewModel : ObservableObject
{
    private readonly ManagerLocalConfig? _config;
    private readonly ComputeService? _compute;
    private readonly UsageBudgetStore? _budgetStore;
    private readonly InfraMetaStore? _infraStore;
    private readonly ISshService _ssh;
    private readonly IUiDialogs _dialogs;
    private readonly HybridShell _shell;
    private readonly MainViewModel _main;
    private readonly ConnectExistingFlow _connectExisting;
    private readonly DestroyInfrastructureViewModel _destroy;

    private BudgetConfigDocument? _lastBudget;
    private InfraMetaDocument? _lastInfra;
    private string _idleSnapshot = "";
    private string _infraSnapshot = "";
    private bool _suppressDirty;
    private bool _tabSelected;

    [ObservableProperty]
    private string _statusMessage =
        "Break-glass Compute actions do not move the reserved play IP. Prefer top-bar Start/Stop (door-aware).";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _editIdleTimeout = "15";

    [ObservableProperty]
    private string _editBudgetWarn = "5";

    [ObservableProperty]
    private bool _editIdleAgentEnabled = true;

    [ObservableProperty]
    private string _infraSummary = "Not loaded yet.";

    [ObservableProperty]
    private string _editStackVersion = InfraMetaDocument.DefaultStackVersion;

    [ObservableProperty]
    private string _editServerKind = "vanilla";

    [ObservableProperty]
    private string _editMinecraftVersion = "unspecified";

    [ObservableProperty]
    private string _gameVmLifecycle = "—";

    [ObservableProperty]
    private string _doorVmLifecycle = "—";

    [ObservableProperty]
    private string _doorServiceState = "—";

    [ObservableProperty]
    private bool _hasIdleChanges;

    [ObservableProperty]
    private bool _hasInfraChanges;

    public bool CanApplyIdle => HasIdleChanges && !IsBusy;

    public bool CanPublishInfra => HasInfraChanges && !IsBusy && _infraStore is not null;

    public AdvancedViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        IUiDialogs dialogs,
        HybridShell shell,
        MainViewModel main,
        ConnectExistingFlow connectExisting,
        DestroyInfrastructureViewModel destroy)
    {
        _config = configHost.Config;
        _compute = cloud.Compute;
        _budgetStore = cloud.UsageStore;
        _ssh = cloud.Ssh;
        _dialogs = dialogs;
        _shell = shell;
        _main = main;
        _connectExisting = connectExisting;
        _destroy = destroy;

        if (_config is not null && cloud.Session is not null)
        {
            var os = new ObjectStorageService(cloud.Session, _config.ObjectStorage);
            _infraStore = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
        }

        SeedIdleFromLocal();
        CaptureInfraSnapshot();
        ApplyLiveStatus(_main.Vm1Lifecycle, _main.DoorState);
        _main.PropertyChanged += OnMainChanged;
    }

    /// <summary>Call when the Advanced tab component is created (tab selected).</summary>
    public void OnTabSelected()
    {
        _tabSelected = true;
        ApplyLiveStatus(_main.Vm1Lifecycle, _main.DoorState);
        _ = RefreshAdvancedTabAsync();
    }

    /// <summary>Call when the Advanced tab component is disposed (tab left).</summary>
    public void OnTabLeft() => _tabSelected = false;

    public void OpenSetup()
    {
        // Opens the Setup wizard. This button does not tofu apply.
        _shell.OpenSetup();
    }

    public void OpenDestroyInfrastructure()
    {
        if (IsBusy)
            return;
        _destroy.Open();
    }

    public async Task AutoDetectAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Auto-detect: scanning OCI profiles…";
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var outcome = await _connectExisting.RunAsync(progress);
            if (outcome == ConnectExistingOutcome.Connected)
            {
                StatusMessage = "Connected. Close and reopen Manager to load the new config.";
                await _dialogs.ShowInfoAsync(
                    "Connected",
                    "Local manage config was written from the detected stack. "
                    + "Close and reopen Manager to load it. SSH key path and RCON stayed on this PC.");
                return;
            }

            if (outcome == ConnectExistingOutcome.NoneFound)
                StatusMessage = "No product stack found. Existing local config was not changed.";
            else if (outcome == ConnectExistingOutcome.Cancelled)
                StatusMessage = "Auto-detect cancelled. Existing local config was not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BreakGlassStartAsync()
    {
        if (_compute is null || _config is null)
        {
            StatusMessage = "OCI session unavailable — cannot start VM1.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Break-glass: START VM1 (no IP move)…";

        try
        {
            var start = await _compute.StartInstanceAsync(_config.Vm1.InstanceId);
            if (!start.Succeeded)
            {
                StatusMessage = start.Error ?? "START failed.";
                return;
            }

            StatusMessage = "Waiting for RUNNING…";
            var wait = await _compute.WaitForLifecycleAsync(_config.Vm1.InstanceId, "RUNNING");
            StatusMessage = wait.Succeeded
                ? $"VM1 is {wait.Value}. Reserved IP was NOT moved — use top-bar Start for play path."
                : wait.Error ?? "Wait for RUNNING failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BreakGlassSoftStopAsync()
    {
        if (_compute is null || _config is null)
        {
            StatusMessage = "OCI session unavailable — cannot SoftStop VM1.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Break-glass: SOFTSTOP VM1 (no door handback)…";

        try
        {
            var stop = await _compute.SoftStopInstanceAsync(_config.Vm1.InstanceId);
            if (!stop.Succeeded)
            {
                StatusMessage = stop.Error ?? "SOFTSTOP failed.";
                return;
            }

            StatusMessage = "Waiting for STOPPED…";
            var wait = await _compute.WaitForLifecycleAsync(_config.Vm1.InstanceId, "STOPPED");
            StatusMessage = wait.Succeeded
                ? $"VM1 is {wait.Value}. Prefer top-bar Stop so door reclaim the play IP."
                : wait.Error ?? "Wait for STOPPED failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RefreshIdleFromOsAsync() => RefreshIdleFromOsCoreAsync();

    public async Task ApplyIdleSettingsAsync()
    {
        if (IsBusy || !HasIdleChanges)
            return;

        if (!TryParseIdleEdit(out var timeout, out var warn, out var error))
        {
            StatusMessage = error;
            return;
        }

        var enabling = EditIdleAgentEnabled;

        if (!enabling)
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Danger Zone — disable idle agent?",
                "This DISABLES the idle agent on VM1 (empty-server SoftStop and daily budget SoftStop stop until the next Minecraft boot).\n\n"
                + "Testing / troubleshooting only. Every VM1 boot / Minecraft start FORCE-ENABLES the idle timer and rewrites shared Object Storage budget to enabled if it was off (OS-ISSUE-7). "
                + "A forgotten disable cannot leave Always Free brakes off after a restart.\n\n"
                + "Publishes budget/config.json and applies on VM1 if RUNNING. Continue?",
                confirmButtonText: "Disable idle");
            if (!confirmed)
            {
                StatusMessage = "Disable cancelled.";
                return;
            }
        }
        else
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Apply idle settings?",
                "This publishes idle settings to Object Storage (budget/config.json, notifies door + VM1) "
                + "and, if VM1 is RUNNING, patches /etc/mc-manager/config.json and enables the idle timer. Continue?",
                confirmButtonText: "Apply");
            if (!confirmed)
            {
                StatusMessage = "Apply cancelled.";
                return;
            }
        }

        IsBusy = true;
        StatusMessage = "Publishing budget…";

        try
        {
            if (_budgetStore is null || _config is null)
            {
                StatusMessage = "Object Storage unavailable — cannot publish budget.";
                return;
            }

            var doc = _lastBudget ?? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
            doc.IdleTimeoutMinutes = timeout;
            doc.BudgetWarnMinutes = warn;
            doc.IdleAgentEnabled = enabling;

            var published = await _budgetStore.PublishBudgetAsync(doc);
            if (!published.Succeeded || published.Value is null)
            {
                StatusMessage = published.Error ?? "Publish budget failed.";
                return;
            }

            _lastBudget = published.Value.Budget;
            ApplyBudgetToIdleEdit(published.Value.Budget);

            var life = (_main.Vm1Lifecycle ?? "").ToUpperInvariant();
            if (life != "RUNNING")
            {
                StatusMessage =
                    $"{published.Value.Message} ({published.Value.Flags.SummarizeBudgetFlags()}). "
                    + $"VM1 is '{_main.Vm1Lifecycle}' — SSH apply skipped. "
                    + "Start VM1 and Apply again to change the on-box timer, or wait for boot force-enable when enabling.";
                return;
            }

            StatusMessage = "Budget published — applying on VM1 via SSH…";
            var ssh = await _ssh.ApplyIdleSettingsAsync(
                _config.Vm1,
                enabling,
                timeout,
                warn);
            StatusMessage = ssh.Succeeded
                ? $"{published.Value.Message} Applied on VM1 "
                  + (enabling ? "(timer enabled)." : "(timer stopped/disabled).")
                : $"Budget published, but SSH apply failed: {ssh.Error}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task RefreshInfraMetaAsync() => RefreshInfraMetaCoreAsync();

    public async Task PublishInfraMetaAsync()
    {
        if (_infraStore is null || _config is null)
        {
            StatusMessage = "Object Storage unavailable — cannot publish meta/infra.json.";
            return;
        }

        if (IsBusy || !HasInfraChanges)
            return;

        var stackVersion = EditStackVersion.Trim();
        var serverKind = EditServerKind.Trim();
        var minecraftVersion = EditMinecraftVersion.Trim();
        if (string.IsNullOrWhiteSpace(stackVersion)
            || string.IsNullOrWhiteSpace(serverKind)
            || string.IsNullOrWhiteSpace(minecraftVersion))
        {
            StatusMessage = "stack_version, server_kind, and minecraft_version are required.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Publish infrastructure meta?",
            "This writes meta/infra.json from local config (OCIDs, play IP, network, VM/door identity, Object Storage). "
            + "It does NOT include SSH private keys, OCI API paths, or RCON passwords.\n\n"
            + "If the bucket still has the legacy flat v1 object, this migrates it to nested v2 for Connect existing. Continue?",
            confirmButtonText: "Publish meta");
        if (!confirmed)
        {
            StatusMessage = "Publish meta cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Publishing meta/infra.json…";

        try
        {
            var published = await _infraStore.PublishFromLocalAsync(
                _config,
                stackVersion: stackVersion,
                serverKind: serverKind,
                minecraftVersion: minecraftVersion);
            if (!published.Succeeded || published.Value is null)
            {
                StatusMessage = published.Error ?? "Publish meta failed.";
                return;
            }

            _lastInfra = published.Value.Document;
            InfraSummary = published.Value.Document.FormatSummary();
            EditStackVersion = published.Value.Document.StackVersion;
            EditServerKind = published.Value.Document.Game.ServerKind;
            EditMinecraftVersion = published.Value.Document.Game.MinecraftVersion;
            CaptureInfraSnapshot();
            StatusMessage = published.Value.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshAdvancedTabAsync()
    {
        await RefreshDoorVmLifecycleAsync();
        await RefreshIdleFromOsCoreAsync();
        await RefreshInfraMetaCoreAsync();
    }

    private void ApplyLiveStatus(string gameVmLifecycle, string doorServiceState)
    {
        GameVmLifecycle = string.IsNullOrWhiteSpace(gameVmLifecycle) ? "—" : gameVmLifecycle;
        DoorServiceState = string.IsNullOrWhiteSpace(doorServiceState) ? "—" : doorServiceState;
    }

    private async Task RefreshDoorVmLifecycleAsync()
    {
        if (_compute is null || _config is null)
        {
            DoorVmLifecycle = "unavailable";
            return;
        }

        var life = await _compute.GetLifecycleStateAsync(_config.Door.InstanceId);
        DoorVmLifecycle = life.Succeeded && !string.IsNullOrWhiteSpace(life.Value)
            ? life.Value
            : life.Error ?? "unavailable";
    }

    private async Task RefreshIdleFromOsCoreAsync()
    {
        if (_budgetStore is null)
        {
            StatusMessage = "Object Storage unavailable — using local config for idle fields.";
            if (!HasIdleChanges)
                SeedIdleFromLocal();
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Loading idle settings from Object Storage…";

        try
        {
            var pull = await _budgetStore.PullAsync(forceLedger: false);
            if (!pull.Succeeded || pull.Value is null)
            {
                StatusMessage = pull.Error ?? "Failed to pull budget.";
                SeedIdleFromLocal();
                return;
            }

            var budget = pull.Value.Budget ?? (_config is not null
                ? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1)
                : new BudgetConfigDocument());
            _lastBudget = budget;
            if (!HasIdleChanges)
                ApplyBudgetToIdleEdit(budget);
            StatusMessage = pull.Value.BudgetMissing
                ? "budget/config.json missing — seeded from local config."
                : "Idle settings loaded from Object Storage budget.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshInfraMetaCoreAsync()
    {
        if (_infraStore is null)
        {
            InfraSummary = "Object Storage unavailable — cannot read meta/infra.json.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Loading meta/infra.json…";

        try
        {
            var read = await _infraStore.GetAsync();
            if (!read.Succeeded || read.Value is null)
            {
                InfraSummary = read.Error ?? "Failed to read meta/infra.json.";
                StatusMessage = InfraSummary;
                return;
            }

            ApplyInfraRead(read.Value);
            StatusMessage = read.Value.Notes;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyInfraRead(InfraMetaReadResult read)
    {
        if (read.Document is { } doc)
        {
            _lastInfra = doc;
            InfraSummary = doc.FormatSummary();
            if (!HasInfraChanges)
            {
                EditStackVersion = doc.StackVersion;
                EditServerKind = doc.Game.ServerKind;
                EditMinecraftVersion = doc.Game.MinecraftVersion;
                CaptureInfraSnapshot();
            }
            return;
        }

        _lastInfra = null;
        if (read.Missing)
        {
            InfraSummary = "meta/infra.json missing — publish from local config to seed Connect existing.";
            return;
        }

        if (read.IsLegacy)
        {
            InfraSummary =
                $"Legacy object needs migration. {read.LegacySummary ?? ""} "
                + "Publish from local config to write nested v2.";
            return;
        }

        InfraSummary = read.Notes;
    }

    private void SeedIdleFromLocal()
    {
        if (_config is null)
            return;
        ApplyBudgetToIdleEdit(BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1));
    }

    private void ApplyBudgetToIdleEdit(BudgetConfigDocument budget)
    {
        _suppressDirty = true;
        try
        {
            EditIdleTimeout = budget.IdleTimeoutMinutes.ToString();
            EditBudgetWarn = budget.BudgetWarnMinutes.ToString();
            EditIdleAgentEnabled = budget.IdleAgentEnabled;
            CaptureIdleSnapshot();
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void CaptureIdleSnapshot()
    {
        _idleSnapshot = IdleFingerprint();
        HasIdleChanges = false;
    }

    private void CaptureInfraSnapshot()
    {
        _infraSnapshot = InfraFingerprint();
        HasInfraChanges = false;
    }

    private string IdleFingerprint() =>
        $"{EditIdleTimeout}|{EditBudgetWarn}|{EditIdleAgentEnabled}";

    private string InfraFingerprint() =>
        $"{EditStackVersion}|{EditServerKind}|{EditMinecraftVersion}";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(EditIdleTimeout):
            case nameof(EditBudgetWarn):
            case nameof(EditIdleAgentEnabled):
                if (!_suppressDirty)
                    HasIdleChanges = IdleFingerprint() != _idleSnapshot;
                break;
            case nameof(EditStackVersion):
            case nameof(EditServerKind):
            case nameof(EditMinecraftVersion):
                if (!_suppressDirty)
                    HasInfraChanges = InfraFingerprint() != _infraSnapshot;
                break;
            case nameof(HasIdleChanges):
            case nameof(IsBusy):
                OnPropertyChanged(nameof(CanApplyIdle));
                OnPropertyChanged(nameof(CanPublishInfra));
                break;
            case nameof(HasInfraChanges):
                OnPropertyChanged(nameof(CanPublishInfra));
                break;
        }
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle)
            or nameof(MainViewModel.DoorState)
            or null)
        {
            ApplyLiveStatus(_main.Vm1Lifecycle, _main.DoorState);
        }

        if (_tabSelected && e.PropertyName is nameof(MainViewModel.Vm1Lifecycle) or null)
            _ = RefreshDoorVmLifecycleAsync();
    }

    private bool TryParseIdleEdit(out int timeout, out int warn, out string error)
    {
        timeout = 0;
        warn = 0;
        error = "";

        if (!int.TryParse(EditIdleTimeout.Trim(), out timeout) || timeout < 1)
        {
            error = "Idle timeout must be an integer ≥ 1.";
            return false;
        }

        if (!int.TryParse(EditBudgetWarn.Trim(), out warn) || warn < 0)
        {
            error = "Budget warn minutes must be an integer ≥ 0.";
            return false;
        }

        return true;
    }
}
