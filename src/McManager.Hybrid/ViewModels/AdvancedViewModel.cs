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
    private readonly IClipboard _clipboard;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly ActionBanner _banner;
    private readonly ChromeViewModel _chrome;
    private readonly DestroyInfrastructureViewModel _destroy;
    private bool _forwardBanner;
    private bool _suppressServerSwitch;

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
        "Emergency power doesn't move the play IP. Use Start and Stop in the sidebar for normal use.";

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

    [ObservableProperty]
    private string _vm1SshHost = "";

    [ObservableProperty]
    private string _doorSshHost = "";

    [ObservableProperty]
    private string _playReservedIp = "";

    [ObservableProperty]
    private IReadOnlyList<ServerIndexEntry> _servers = [];

    [ObservableProperty]
    private string _selectedServerId = "";

    [ObservableProperty]
    private string _newServerName = "";

    [ObservableProperty]
    private string _renameServerName = "";

    public bool ShowServerSwitcher => !ServerCatalog.HasEnvOverride;

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

    public bool HasVm1SshHost => !string.IsNullOrWhiteSpace(Vm1SshHost);

    public bool HasDoorSshHost => !string.IsNullOrWhiteSpace(DoorSshHost);

    public string Vm1SshHostDisplay => HasVm1SshHost ? Vm1SshHost : "—";

    public string DoorSshHostDisplay => HasDoorSshHost ? DoorSshHost : "—";

    public string? PlayerMapUrl => PlayerMapPublicUrl.TryFormat(DoorSshHost);

    public bool HasPlayerMapUrl => !string.IsNullOrWhiteSpace(PlayerMapUrl);

    public string PlayerMapUrlDisplay => HasPlayerMapUrl ? PlayerMapUrl! : "—";

    public bool CanRefreshSshHosts =>
        !IsBusy && _config is not null && _compute is not null;

    public AdvancedViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiDialogs dialogs,
        HybridShell shell,
        MainViewModel main,
        ConnectExistingFlow connectExisting,
        IFilePicker filePicker,
        IClipboard clipboard,
        ActionBanner banner,
        ChromeViewModel chrome,
        DestroyInfrastructureViewModel destroy)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _dialogs = dialogs;
        _shell = shell;
        _main = main;
        _connectExisting = connectExisting;
        _filePicker = filePicker;
        _clipboard = clipboard;
        _banner = banner;
        _chrome = chrome;
        _destroy = destroy;

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

    partial void OnIsBusyChanged(bool value) => NotifySshHostDerived();

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
        SeedSshHostsFromLocal();
        CaptureInfraSnapshot();
        RefreshServerSwitcher();
        OnPropertyChanged(nameof(CanPublishInfra));
        OnPropertyChanged(nameof(CanApplyIdleTimeout));
        OnPropertyChanged(nameof(CanApplyIdleEnabled));
        NotifySshKeyDerived();
        NotifySshHostDerived();
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

    public async Task SwitchServerAsync(string slug)
    {
        if (!ShowServerSwitcher || string.IsNullOrWhiteSpace(slug))
            return;
        if (string.Equals(slug, ServerCatalog.ActiveSlug(), StringComparison.OrdinalIgnoreCase))
            return;
        if (!await CanLeaveCurrentServerAsync().ConfigureAwait(true))
        {
            RefreshServerSwitcher();
            return;
        }

        var set = ServerCatalog.SetActive(slug);
        if (!set.Succeeded)
        {
            await _dialogs.ShowInfoAsync("Could not switch server", set.Error ?? "Unknown error.");
            RefreshServerSwitcher();
            return;
        }

        ApplyServerFolder();
    }

    public async Task AddServerAsync()
    {
        if (!ShowServerSwitcher || IsBusy)
            return;
        if (!await CanLeaveCurrentServerAsync().ConfigureAwait(true))
            return;

        var name = string.IsNullOrWhiteSpace(NewServerName)
            ? ServerCatalog.SuggestDisplayName()
            : NewServerName.Trim();
        var added = ServerCatalog.AddServer(name);
        if (!added.Succeeded)
        {
            await _dialogs.ShowInfoAsync("Could not add server", added.Error ?? "Unknown error.");
            return;
        }

        NewServerName = ServerCatalog.SuggestDisplayName();
        ApplyServerFolder();
    }

    public async Task RenameServerAsync()
    {
        if (!ShowServerSwitcher || IsBusy)
            return;
        var slug = ServerCatalog.ActiveSlug();
        if (string.IsNullOrWhiteSpace(slug))
            return;

        var renamed = ServerCatalog.Rename(slug, RenameServerName);
        if (!renamed.Succeeded)
        {
            await _dialogs.ShowInfoAsync("Could not rename server", renamed.Error ?? "Unknown error.");
            return;
        }

        RefreshServerSwitcher();
        _chrome.RefreshServerLabel();
        StatusMessage = "Server name saved.";
    }

    partial void OnSelectedServerIdChanged(string value)
    {
        if (_suppressServerSwitch)
            return;
        _ = SwitchServerAsync(value);
    }

    private async Task<bool> CanLeaveCurrentServerAsync()
    {
        if (_destroy.Phase == DestroyInfrastructurePhase.Running)
        {
            await _dialogs.ShowInfoAsync(
                "Deletion still running",
                "Wait until Delete from Oracle Cloud finishes before switching servers.");
            return false;
        }

        if (_shell.Page == HybridShell.PageKind.Setup)
        {
            await _dialogs.ShowInfoAsync(
                "Setup is still open",
                "Finish or close Setup before switching servers.");
            return false;
        }

        return true;
    }

    private void ApplyServerFolder()
    {
        _session.ReloadFromDisk();
        if (_configHost.HasManageConfig)
            _shell.EnterManage();
        else
            _shell.EnterFirstRun();
        _chrome.RefreshServerLabel();
        StatusMessage = "Switched server. MCSTool reloaded this folder.";
    }

    private void RefreshServerSwitcher()
    {
        _suppressServerSwitch = true;
        try
        {
            Servers = ServerCatalog.List();
            SelectedServerId = ServerCatalog.ActiveSlug() ?? "";
            RenameServerName = ServerCatalog.ActiveDisplayName() ?? "";
            if (string.IsNullOrWhiteSpace(NewServerName))
                NewServerName = ServerCatalog.SuggestDisplayName();
            OnPropertyChanged(nameof(ShowServerSwitcher));
            _chrome.RefreshServerLabel();
        }
        finally
        {
            _suppressServerSwitch = false;
        }
    }

    public async Task AutoDetectAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Looking for an existing server…";
        var progress = new Progress<string>(msg => StatusMessage = msg);

        try
        {
            var outcome = await _connectExisting.RunAsync(progress);
            if (outcome == ConnectExistingOutcome.Connected)
            {
                _session.ReloadFromDisk();
                StatusMessage = "Connected. MCSTool loaded this server's settings.";
                await _dialogs.ShowInfoAsync(
                    "Connected",
                    "This server's settings were saved on this PC and loaded. "
                    + "Your SSH key and console password stay on this PC.");
                return;
            }

            if (outcome == ConnectExistingOutcome.NoneFound)
                StatusMessage = "No MCSTool server found. Nothing was changed.";
            else if (outcome == ConnectExistingOutcome.Incompatible)
                StatusMessage = "That server is incompatible with this version of MCSTool. Nothing was changed.";
            else if (outcome == ConnectExistingOutcome.Cancelled)
                StatusMessage = "Cancelled. Nothing was changed.";
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
        StatusMessage = "Doorbell VM will use the game VM private key after Save.";
    }

    public void CopyDoorKeyToVm1()
    {
        if (!CanCopyDoorKeyToVm1)
            return;
        EditVm1SshKeyPath = SshKeyPathUx.Normalize(EditDoorSshKeyPath);
        StatusMessage = "Game VM will use the doorbell VM private key after Save.";
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
                StatusMessage = saved.Error ?? "Failed to save settings.";
                return Task.CompletedTask;
            }

            _session.ReloadFromDisk();
            StatusMessage = SshKeysUseSameFile
                ? "Saved SSH key paths on this PC. Both VMs use the same private key file."
                : "Saved SSH key paths on this PC. Game VM and doorbell VM now use different private key files.";
        }
        finally
        {
            IsBusy = false;
            NotifySshKeyDerived();
        }

        return Task.CompletedTask;
    }

    public async Task CopyVm1SshHostAsync()
    {
        if (!HasVm1SshHost)
            return;
        if (!await ClipboardUx.TrySetTextAsync(_clipboard, Vm1SshHost).ConfigureAwait(true))
        {
            StatusMessage = "Clipboard unavailable. Try copy again.";
            return;
        }

        StatusMessage = "Copied game VM SSH IP.";
    }

    public async Task CopyDoorSshHostAsync()
    {
        if (!HasDoorSshHost)
            return;
        if (!await ClipboardUx.TrySetTextAsync(_clipboard, DoorSshHost).ConfigureAwait(true))
        {
            StatusMessage = "Clipboard unavailable. Try copy again.";
            return;
        }

        StatusMessage = "Copied doorbell VM SSH IP.";
    }

    public async Task CopyPlayerMapUrlAsync()
    {
        if (!HasPlayerMapUrl)
            return;
        if (!await ClipboardUx.TrySetTextAsync(_clipboard, PlayerMapUrl!).ConfigureAwait(true))
        {
            StatusMessage = "Clipboard unavailable. Try copy again.";
            return;
        }

        StatusMessage = "Copied player map URL.";
    }

    public async Task RefreshSshHostsFromOciAsync()
    {
        if (!CanRefreshSshHosts || _config is null || _compute is null)
        {
            StatusMessage = _compute is null
                ? "Not connected to Oracle Cloud yet."
                : "Cannot refresh SSH IPs yet.";
            return;
        }

        var compartment = _config.Oci.CompartmentId;
        if (string.IsNullOrWhiteSpace(compartment))
        {
            StatusMessage = "This server's settings are missing its Oracle compartment.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Refreshing SSH IPs from Oracle…";
        NotifySshHostDerived();
        try
        {
            var notes = new List<string>();
            if (!string.IsNullOrWhiteSpace(_config.Vm1.InstanceId))
            {
                var ip = await _compute.TryGetPrimaryPublicIpAsync(
                    compartment, _config.Vm1.InstanceId);
                if (ip.Succeeded && !string.IsNullOrWhiteSpace(ip.Value))
                    _config.Vm1.SshHost = ip.Value.Trim();
                else
                    notes.Add("Game VM: " + (ip.Error ?? "no public IP"));
            }

            if (!string.IsNullOrWhiteSpace(_config.Door.InstanceId))
            {
                var ip = await _compute.TryGetPrimaryPublicIpAsync(
                    compartment, _config.Door.InstanceId);
                if (ip.Succeeded && !string.IsNullOrWhiteSpace(ip.Value))
                    _config.Door.SshHost = ip.Value.Trim();
                else
                    notes.Add("Doorbell VM: " + (ip.Error ?? "no public IP"));
            }

            var saved = LocalConfigStore.SaveConfig(_config);
            if (!saved.Succeeded)
            {
                StatusMessage = saved.Error ?? "Failed to save settings.";
                return;
            }

            _session.ReloadFromDisk();
            SeedSshHostsFromLocal();
            StatusMessage = notes.Count == 0
                ? "Updated SSH IPs from Oracle. Players still join with the play IP."
                : "SSH IPs refreshed with warnings: " + string.Join("; ", notes);
        }
        finally
        {
            IsBusy = false;
            NotifySshHostDerived();
        }
    }

    public Task TestVm1SshAsync() => TestSshAsync(forVm1: true);

    public Task TestDoorSshAsync() => TestSshAsync(forVm1: false);

    public async Task BreakGlassStartAsync()
    {
        if (_compute is null || _config is null)
        {
            StatusMessage = "Oracle Cloud connection unavailable — can't start the game VM.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Starting the game VM (play IP not moved)…";

        try
        {
            var start = await _compute.StartInstanceAsync(_config.Vm1.InstanceId);
            if (!start.Succeeded)
            {
                StatusMessage = start.Error ?? "Start failed.";
                return;
            }

            StatusMessage = "Waiting for the game VM to start…";
            var wait = await _compute.WaitForLifecycleAsync(_config.Vm1.InstanceId, "RUNNING");
            StatusMessage = wait.Succeeded
                ? "Game VM is on. The play IP was not moved. Use Start in the sidebar so players can join."
                : wait.Error ?? "Waiting for the game VM to start failed.";
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
            StatusMessage = "Oracle Cloud connection unavailable — can't stop the game VM.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Stopping the game VM (play IP not moved)…";

        try
        {
            var stop = await _compute.SoftStopInstanceAsync(_config.Vm1.InstanceId);
            if (!stop.Succeeded)
            {
                StatusMessage = stop.Error ?? "Stop failed.";
                return;
            }

            StatusMessage = "Waiting for the game VM to stop…";
            var wait = await _compute.WaitForLifecycleAsync(_config.Vm1.InstanceId, "STOPPED");
            StatusMessage = wait.Succeeded
                ? "Game VM is off. Next time, use Stop in the sidebar so the doorbell VM takes back the play IP."
                : wait.Error ?? "Waiting for the game VM to stop failed.";
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
            "Save idle settings?",
            "This saves the idle timeout and budget warning time to cloud storage for both VMs. "
            + "If the server is running, the game VM updates right away. This does not turn off the idle timer. Continue?",
            confirmButtonText: "Save");
        if (!confirmed)
        {
            StatusMessage = "Save cancelled.";
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
                "Turn off the idle timer?",
                "Until Minecraft next starts, the server is not stopped when it's empty or out of daily hours.\n\n"
                + "For testing only. The idle timer turns back on every time the game VM boots or Minecraft starts, "
                + "so forgetting to turn it back on can't leave it off after a restart.\n\n"
                + "This saves to cloud storage and, if the server is running, updates the game VM now. Continue?",
                confirmButtonText: "Turn off");
            if (!confirmed)
            {
                StatusMessage = "Disable cancelled.";
                return;
            }
        }
        else
        {
            var confirmed = await _dialogs.ConfirmAsync(
                "Turn on the idle timer?",
                "The server will stop again when it's empty or out of daily hours. "
                + "This saves to cloud storage and, if the server is running, turns the timer on now. Continue?",
                confirmButtonText: "Turn on");
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
        StatusMessage = "Saving idle settings…";

        try
        {
            if (_budgetStore is null || _config is null)
            {
                StatusMessage = "Cloud storage unavailable — can't save idle settings.";
                return;
            }

            var doc = _lastBudget ?? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
            doc.IdleTimeoutMinutes = timeout;
            doc.BudgetWarnMinutes = warn;
            doc.IdleAgentEnabled = enabling;

            var published = await _budgetStore.PublishBudgetAsync(doc);
            if (!published.Succeeded || published.Value is null)
            {
                StatusMessage = published.Error ?? "Saving idle settings failed.";
                return;
            }

            _lastBudget = published.Value.Budget;
            ApplyBudgetToIdleEdit(published.Value.Budget);

            var life = (_main.Vm1Lifecycle ?? "").ToUpperInvariant();
            if (life != "RUNNING")
            {
                StatusMessage =
                    $"{published.Value.Message} "
                    + "The game VM is off, so it was not updated. "
                    + "Start the server and save again to update it. Starting it always turns the idle timer on.";
                return;
            }

            StatusMessage = "Saved — updating the game VM…";
            var ssh = await _ssh.ApplyIdleSettingsAsync(
                _config.Vm1,
                enabling,
                timeout,
                warn);
            StatusMessage = ssh.Succeeded
                ? $"{published.Value.Message} Game VM updated "
                  + (enabling ? "(idle timer on)." : "(idle timer off).")
                : $"Saved, but updating the game VM failed: {ssh.Error}";
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
            StatusMessage = "Cloud storage unavailable — can't save server details.";
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
            StatusMessage = "Setup version, server type, and Minecraft version are required.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Save server details?",
            "This saves this server's details (both VMs, the play IP, and the network) to cloud storage "
            + "so another PC can find it with Find existing server. "
            + "SSH private keys, API keys, and the console password are never included.\n\n"
            + "Older saved details are updated to the current format. Continue?",
            confirmButtonText: "Save");
        if (!confirmed)
        {
            StatusMessage = "Save cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Saving server details…";

        try
        {
            var published = await _infraStore.PublishFromLocalAsync(
                _config,
                stackVersion: stackVersion,
                serverKind: serverKind,
                minecraftVersion: minecraftVersion);
            if (!published.Succeeded || published.Value is null)
            {
                var error = published.Error ?? "Saving server details failed.";
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
            StatusMessage = "Cloud storage unavailable — showing this PC's idle settings.";
            if (!HasIdleTimeoutChanges && !HasIdleEnabledChanges)
                SeedIdleFromLocal();
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        StatusMessage = "Loading idle settings from cloud storage…";

        try
        {
            var pull = await _budgetStore.PullAsync(forceLedger: false);
            if (!pull.Succeeded || pull.Value is null)
            {
                StatusMessage = pull.Error ?? "Failed to load idle settings.";
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
                ? "Idle settings missing from cloud storage — showing this PC's settings."
                : "Idle settings loaded from cloud storage.";
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
            InfraSummary = "Cloud storage unavailable — can't read server details.";
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
                InfraSummary = read.Error ?? "Failed to read server details.";
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
            InfraSummary = "No server details saved yet. Save to let other PCs find this server.";
            return;
        }

        if (read.IsLegacy)
        {
            InfraSummary =
                $"Server details use an older format. {read.LegacySummary ?? ""} "
                + "Save to update them.";
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

    private void SeedSshHostsFromLocal()
    {
        Vm1SshHost = _config?.Vm1.SshHost ?? "";
        DoorSshHost = _config?.Door.SshHost ?? "";
        PlayReservedIp = _config?.Play.ReservedPublicIp ?? "";
        NotifySshHostDerived();
    }

    private void NotifySshHostDerived()
    {
        OnPropertyChanged(nameof(HasVm1SshHost));
        OnPropertyChanged(nameof(HasDoorSshHost));
        OnPropertyChanged(nameof(Vm1SshHostDisplay));
        OnPropertyChanged(nameof(DoorSshHostDisplay));
        OnPropertyChanged(nameof(PlayerMapUrl));
        OnPropertyChanged(nameof(HasPlayerMapUrl));
        OnPropertyChanged(nameof(PlayerMapUrlDisplay));
        OnPropertyChanged(nameof(CanRefreshSshHosts));
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
                Title = $"Select SSH private key for the {label}",
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
                Label = "Game VM",
            }
            : new SshTarget
            {
                Host = _config.Door.SshHost,
                User = string.IsNullOrWhiteSpace(_config.Door.SshUser) ? "ubuntu" : _config.Door.SshUser,
                KeyPath = path,
                Label = "Doorbell VM",
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
            error = "Idle timeout must be a whole number, 1 or more.";
            return false;
        }

        if (!int.TryParse(EditBudgetWarn.Trim(), out warn) || warn < 0)
        {
            error = "Budget warning time must be a whole number, 0 or more.";
            return false;
        }

        return true;
    }
}
