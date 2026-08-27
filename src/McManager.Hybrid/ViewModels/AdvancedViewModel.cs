using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Advanced: technical VM/door status, break-glass Compute, idle timeout/warn,
/// infra meta publish, Auto-detect, Deploy/repair → Setup wizard.
/// Danger Zone reuses idle <see cref="EditIdleAgentEnabled"/> apply, VM1 shape
/// scale lives on <see cref="Vm1ShapeScaleViewModel"/>, and typed-confirm delete
/// is the existing destroy dialog (not constructed here). SSH private-key paths
/// (this PC, per VM) live on Stack.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart.
/// </summary>
public sealed partial class AdvancedViewModel : ObservableObject
{
    private ManagerLocalConfig? _config;
    private ComputeService? _compute;
    private UsageBudgetStore? _budgetStore;
    private InfraMetaStore? _infraStore;
    private ISshService _ssh = null!;
    private readonly IUiDialogs _dialogs;
    private readonly HybridShell _shell;
    private readonly MainViewModel _main;
    private readonly ConnectExistingFlow _connectExisting;
    private readonly IFilePicker _filePicker;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly ActionBanner _banner;
    private bool _forwardBanner;

    private BudgetConfigDocument? _lastBudget;
    private InfraMetaDocument? _lastInfra;
    private string _idleTimeoutSnapshot = "";
    private bool _idleEnabledSnapshot = true;
    private string _infraSnapshot = "";
    private string _sshKeySnapshot = "";
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
    private bool _hasIdleTimeoutChanges;

    [ObservableProperty]
    private bool _hasIdleEnabledChanges;

    [ObservableProperty]
    private bool _hasInfraChanges;

    [ObservableProperty]
    private string _editVm1SshKeyPath = "";

    [ObservableProperty]
    private string _editDoorSshKeyPath = "";

    [ObservableProperty]
    private bool _hasSshKeyChanges;

    public bool CanApplyIdleTimeout => HasIdleTimeoutChanges && !IsBusy;

    public bool CanApplyIdleEnabled => HasIdleEnabledChanges && !IsBusy;

    public bool CanPublishInfra => HasInfraChanges && !IsBusy && _infraStore is not null;

    public bool CanSaveSshKeys => HasSshKeyChanges && !IsBusy && _config is not null;

    public bool CanCopyVm1KeyToDoor =>
        !IsBusy && !SshKeyPathUx.PathsEqual(EditVm1SshKeyPath, EditDoorSshKeyPath)
        && SshKeyPathUx.Normalize(EditVm1SshKeyPath).Length > 0;

    public bool CanCopyDoorKeyToVm1 =>
        !IsBusy && !SshKeyPathUx.PathsEqual(EditVm1SshKeyPath, EditDoorSshKeyPath)
        && SshKeyPathUx.Normalize(EditDoorSshKeyPath).Length > 0;

    public bool SshKeysUseSameFile =>
        SshKeyPathUx.UsesSameFile(EditVm1SshKeyPath, EditDoorSshKeyPath);

    public bool Vm1SshKeyMissing => SshKeyPathUx.FileMissing(EditVm1SshKeyPath);

    public bool DoorSshKeyMissing => SshKeyPathUx.FileMissing(EditDoorSshKeyPath);

    public string SshKeyHelp => SshKeyPathUx.HelpText;

    public AdvancedViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiDialogs dialogs,
        HybridShell shell,
        MainViewModel main,
        ConnectExistingFlow connectExisting,
        IFilePicker filePicker,
        ActionBanner banner)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _dialogs = dialogs;
        _shell = shell;
        _main = main;
        _connectExisting = connectExisting;
        _filePicker = filePicker;
        _banner = banner;

        BindFromHost();
        _forwardBanner = true;
        ApplyLiveStatus(_main.Vm1Lifecycle, _main.DoorState);
        _main.PropertyChanged += OnMainChanged;
        _session.Reloaded += OnSessionReloaded;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!_forwardBanner || !TabStatusBannerPolicy.ShouldForwardAdvancedStatus(value))
            return;
        _banner.ShowInferred(value);
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void BindFromHost()
    {
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        BindFromHostCore();
        _forwardBanner = wasForward;
    }

    private void BindFromHostCore()
    {
        _config = _configHost.Config;
        _compute = _cloud.Compute;
        _budgetStore = _cloud.UsageStore;
        _ssh = _cloud.Ssh;
        _infraStore = null;
        if (_config is not null && _cloud.Session is not null)
        {
            var os = new ObjectStorageService(_cloud.Session, _config.ObjectStorage);
            _infraStore = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
        }

        SeedIdleFromLocal();
        SeedSshKeysFromLocal();
        CaptureInfraSnapshot();
        OnPropertyChanged(nameof(CanPublishInfra));
        OnPropertyChanged(nameof(CanApplyIdleTimeout));
        OnPropertyChanged(nameof(CanApplyIdleEnabled));
        NotifySshKeyDerived();
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
                _session.ReloadFromDisk();
                StatusMessage = "Connected. Manager reloaded the new local config.";
                await _dialogs.ShowInfoAsync(
                    "Connected",
                    "Local manage config was written from the detected stack. "
                    + "Manager loaded it without a restart. SSH key path and RCON stayed on this PC.");
                return;
            }

            if (outcome == ConnectExistingOutcome.NoneFound)
                StatusMessage = "No product stack found. Existing local config was not changed.";
            else if (outcome == ConnectExistingOutcome.Incompatible)
                StatusMessage = "Stack is incompatible with this Manager. Existing local config was not changed.";
            else if (outcome == ConnectExistingOutcome.Cancelled)
                StatusMessage = "Auto-detect cancelled. Existing local config was not changed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task BrowseVm1SshKeyAsync() =>
        await BrowseSshKeyAsync(forVm1: true).ConfigureAwait(true);

    public async Task BrowseDoorSshKeyAsync() =>
        await BrowseSshKeyAsync(forVm1: false).ConfigureAwait(true);

    public void CopyVm1KeyToDoor()
    {
        if (!CanCopyVm1KeyToDoor)
            return;
        EditDoorSshKeyPath = SshKeyPathUx.Normalize(EditVm1SshKeyPath);
        StatusMessage = "Door VM will use the game VM private key after Save.";
    }

    public void CopyDoorKeyToVm1()
    {
        if (!CanCopyDoorKeyToVm1)
            return;
        EditVm1SshKeyPath = SshKeyPathUx.Normalize(EditDoorSshKeyPath);
        StatusMessage = "Game VM will use the doorbell private key after Save.";
    }

    public Task SaveSshKeysAsync()
    {
        if (!CanSaveSshKeys || _config is null)
            return Task.CompletedTask;

        var check = SshKeyPathUx.ValidatePair(EditVm1SshKeyPath, EditDoorSshKeyPath);
        if (!check.Succeeded)
        {
            StatusMessage = check.Error ?? "SSH key paths are not valid.";
            return Task.CompletedTask;
        }

        IsBusy = true;
        NotifySshKeyDerived();
        try
        {
            SshKeyPathUx.Apply(_config, EditVm1SshKeyPath, EditDoorSshKeyPath);
            var saved = LocalConfigStore.SaveConfig(_config);
            if (!saved.Succeeded)
            {
                StatusMessage = saved.Error ?? "Failed to save config.local.json.";
                return Task.CompletedTask;
            }

            _session.ReloadFromDisk();
            StatusMessage = SshKeysUseSameFile
                ? "Saved SSH key paths on this PC. Both VMs use the same private key file."
                : "Saved SSH key paths on this PC. Game VM and doorbell now use different private key files.";
        }
        finally
        {
            IsBusy = false;
            NotifySshKeyDerived();
        }

        return Task.CompletedTask;
    }

    public Task TestVm1SshAsync() => TestSshAsync(forVm1: true);

    public Task TestDoorSshAsync() => TestSshAsync(forVm1: false);

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

    public async Task ApplyIdleTimeoutSettingsAsync()
    {
        if (IsBusy || !HasIdleTimeoutChanges)
            return;

        if (!TryParseIdleEdit(out var timeout, out var warn, out var error))
        {
            StatusMessage = error;
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Apply idle settings?",
            "This publishes idle timeout and budget warning lead time to Object Storage (budget/config.json, notifies door + VM1) "
            + "and, if VM1 is RUNNING, patches /etc/mc-manager/config.json. It does not turn the idle timer off. Continue?",
            confirmButtonText: "Apply");
        if (!confirmed)
        {
            StatusMessage = "Apply cancelled.";
            return;
        }

        await PublishAndApplyIdleAsync(timeout, warn, _idleEnabledSnapshot);
    }

    public async Task ApplyIdleEnabledAsync()
    {
        if (IsBusy || !HasIdleEnabledChanges)
            return;

        if (!TryParseIdleEdit(out var timeout, out var warn, out var error))
        {
            if (_lastBudget is null)
            {
                StatusMessage = error;
                return;
            }

            timeout = _lastBudget.IdleTimeoutMinutes;
            warn = _lastBudget.BudgetWarnMinutes;
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
                "Enable idle agent?",
                "This turns the idle timer back on (empty-server SoftStop and daily budget SoftStop). "
                + "Publishes budget/config.json and, if VM1 is RUNNING, enables the on-box timer. Continue?",
                confirmButtonText: "Enable idle");
            if (!confirmed)
            {
                StatusMessage = "Enable cancelled.";
                return;
            }
        }

        await PublishAndApplyIdleAsync(timeout, warn, enabling);
    }

    private async Task PublishAndApplyIdleAsync(int timeout, int warn, bool enabling)
    {
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
                var error = published.Error ?? "Publish meta failed.";
                StatusMessage = error;
                _banner.Show(error, ActionBannerSeverity.Error);
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
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        try
        {
            await RefreshDoorVmLifecycleAsync();
            await RefreshIdleFromOsCoreAsync();
            await RefreshInfraMetaCoreAsync();
        }
        finally
        {
            _forwardBanner = wasForward;
        }
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
            if (!HasIdleTimeoutChanges && !HasIdleEnabledChanges)
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
            if (!HasIdleTimeoutChanges && !HasIdleEnabledChanges)
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

        try
        {
            var read = await _infraStore.GetAsync();
            if (!read.Succeeded || read.Value is null)
            {
                InfraSummary = read.Error ?? "Failed to read meta/infra.json.";
                return;
            }

            ApplyInfraRead(read.Value);
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
        {
            EditIdleTimeout = "15";
            EditBudgetWarn = "5";
            EditIdleAgentEnabled = true;
            CaptureIdleSnapshot();
            return;
        }
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
        _idleTimeoutSnapshot = IdleTimeoutFingerprint();
        _idleEnabledSnapshot = EditIdleAgentEnabled;
        HasIdleTimeoutChanges = false;
        HasIdleEnabledChanges = false;
    }

    private void CaptureInfraSnapshot()
    {
        _infraSnapshot = InfraFingerprint();
        HasInfraChanges = false;
    }

    private void SeedSshKeysFromLocal()
    {
        _suppressDirty = true;
        try
        {
            EditVm1SshKeyPath = _config?.Vm1.SshKeyPath ?? "";
            EditDoorSshKeyPath = _config?.Door.SshKeyPath ?? "";
            CaptureSshKeySnapshot();
        }
        finally
        {
            _suppressDirty = false;
        }
    }

    private void CaptureSshKeySnapshot()
    {
        _sshKeySnapshot = SshKeyFingerprint();
        HasSshKeyChanges = false;
    }

    private string IdleTimeoutFingerprint() =>
        $"{EditIdleTimeout}|{EditBudgetWarn}";

    private string InfraFingerprint() =>
        $"{EditStackVersion}|{EditServerKind}|{EditMinecraftVersion}";

    private string SshKeyFingerprint() =>
        $"{SshKeyPathUx.Normalize(EditVm1SshKeyPath)}|{SshKeyPathUx.Normalize(EditDoorSshKeyPath)}";

    private void NotifySshKeyDerived()
    {
        OnPropertyChanged(nameof(CanSaveSshKeys));
        OnPropertyChanged(nameof(CanCopyVm1KeyToDoor));
        OnPropertyChanged(nameof(CanCopyDoorKeyToVm1));
        OnPropertyChanged(nameof(SshKeysUseSameFile));
        OnPropertyChanged(nameof(Vm1SshKeyMissing));
        OnPropertyChanged(nameof(DoorSshKeyMissing));
    }

    private async Task BrowseSshKeyAsync(bool forVm1)
    {
        if (IsBusy)
            return;

        var current = forVm1 ? EditVm1SshKeyPath : EditDoorSshKeyPath;
        var label = forVm1 ? "game VM" : "doorbell VM";
        var path = await _filePicker.OpenFileAsync(
            new FilePickRequest
            {
                Title = $"Select SSH private key for the {label} (not stored in Object Storage)",
                InitialDirectory = SshKeyPathUx.InitialDirectory(current),
                Filters = [new FileTypeFilter("All files", ".*")],
            });

        if (string.IsNullOrWhiteSpace(path))
            return;

        var check = SshKeyPathUx.ValidatePrivateKeyFile(path);
        if (!check.Succeeded)
        {
            StatusMessage = check.Error ?? "That file is not a usable SSH private key.";
            return;
        }

        if (forVm1)
            EditVm1SshKeyPath = path;
        else
            EditDoorSshKeyPath = path;

        StatusMessage = $"Selected private key for the {label}. Save to use it.";
    }

    private async Task TestSshAsync(bool forVm1)
    {
        if (IsBusy || _config is null)
            return;

        var path = forVm1 ? EditVm1SshKeyPath : EditDoorSshKeyPath;
        var check = SshKeyPathUx.ValidatePrivateKeyFile(path);
        if (!check.Succeeded)
        {
            StatusMessage = check.Error ?? "SSH key path is not valid.";
            return;
        }

        var target = forVm1
            ? new SshTarget
            {
                Host = _config.Vm1.SshHost,
                User = string.IsNullOrWhiteSpace(_config.Vm1.SshUser) ? "ubuntu" : _config.Vm1.SshUser,
                KeyPath = path,
                Label = "VM1",
            }
            : new SshTarget
            {
                Host = _config.Door.SshHost,
                User = string.IsNullOrWhiteSpace(_config.Door.SshUser) ? "ubuntu" : _config.Door.SshUser,
                KeyPath = path,
                Label = "door",
            };

        var label = forVm1 ? "game VM" : "doorbell VM";
        IsBusy = true;
        NotifySshKeyDerived();
        StatusMessage = $"Testing SSH to the {label}…";
        try
        {
            var result = await _ssh.RunCommandAsync(target, "true", TimeSpan.FromSeconds(20));
            StatusMessage = result.Succeeded
                ? $"SSH to the {label} succeeded."
                : result.Error ?? $"SSH to the {label} failed.";
        }
        finally
        {
            IsBusy = false;
            NotifySshKeyDerived();
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(EditIdleTimeout):
            case nameof(EditBudgetWarn):
                if (!_suppressDirty)
                    HasIdleTimeoutChanges = IdleTimeoutFingerprint() != _idleTimeoutSnapshot;
                break;
            case nameof(EditIdleAgentEnabled):
                if (!_suppressDirty)
                    HasIdleEnabledChanges = EditIdleAgentEnabled != _idleEnabledSnapshot;
                break;
            case nameof(EditStackVersion):
            case nameof(EditServerKind):
            case nameof(EditMinecraftVersion):
                if (!_suppressDirty)
                    HasInfraChanges = InfraFingerprint() != _infraSnapshot;
                break;
            case nameof(EditVm1SshKeyPath):
            case nameof(EditDoorSshKeyPath):
                if (!_suppressDirty)
                    HasSshKeyChanges = SshKeyFingerprint() != _sshKeySnapshot;
                NotifySshKeyDerived();
                break;
            case nameof(HasIdleTimeoutChanges):
            case nameof(HasIdleEnabledChanges):
            case nameof(IsBusy):
                OnPropertyChanged(nameof(CanApplyIdleTimeout));
                OnPropertyChanged(nameof(CanApplyIdleEnabled));
                OnPropertyChanged(nameof(CanPublishInfra));
                NotifySshKeyDerived();
                break;
            case nameof(HasInfraChanges):
                OnPropertyChanged(nameof(CanPublishInfra));
                break;
            case nameof(HasSshKeyChanges):
                OnPropertyChanged(nameof(CanSaveSshKeys));
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
