using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Manage chrome: novice status, door-aware power, pinned hours, poll, action banner.
/// Tab Object Storage work (B6–B10) must not set <see cref="_powerActionInFlight"/>.
/// </summary>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    public const string Placeholder = "—";

    private static readonly TimeSpan DoorPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OciPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromMinutes(2);

    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly IClipboard _clipboard;
    private readonly WindowFocusBroker _focus;
    private readonly NotificationCenter _notices;
    private readonly ActionBanner _banner;

    private ManagerLocalConfig? _config;
    private DoorClient? _door;
    private ComputeService? _compute;
    private UsageBudgetStore? _usageStore;
    private SpendBrakeLockStore? _spendBrake;
    private OversizedWorldBackupStore? _oversizedWorld;
    private TroubleshootingService? _troubleshooting;
    private SshService _ssh = null!;
    private bool _resumeChromeAfterReload;
    private SpendBrakeUiState _spendBrakeUi = SpendBrakeUiState.Unknown;

    private CancellationTokenSource? _pollCts;
    private CancellationTokenSource? _copyLabelCts;
    private DateTimeOffset _lastDoorPollUtc = DateTimeOffset.MinValue;
    private DateTimeOffset _lastOciPollUtc = DateTimeOffset.MinValue;
    private UsageLedgerDocument _pinLedger = UsageLedgerDocument.Empty();
    private bool _windowFocused = true;
    private bool _disposed;
    private bool _chromeStarted;
    private bool _hasInitialStatus;
    private bool _powerActionInFlight;
    private PowerActionKind _powerAction = PowerActionKind.None;
    private bool _playersPollInFlight;

    public string Title { get; } = "mc manager";

    [ObservableProperty]
    private string _status = Placeholder;

    [ObservableProperty]
    private bool _statusIsRunning;

    [ObservableProperty]
    private string _playIp = Placeholder;

    [ObservableProperty]
    private string _playersDisplay = "0";

    [ObservableProperty]
    private string _copyPlayIpLabel = "copy";

    [ObservableProperty]
    private string _vm1Lifecycle = Placeholder;

    [ObservableProperty]
    private string _doorState = Placeholder;

    [ObservableProperty]
    private string _actionFeedback = "";

    [ObservableProperty]
    private bool _configLoaded;

    [ObservableProperty]
    private bool _canStart;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _canRestart;

    [ObservableProperty]
    private string _pinTodayValue = Placeholder;

    [ObservableProperty]
    private string _pinTodayHint = "";

    [ObservableProperty]
    private double _pinTodayFraction;

    [ObservableProperty]
    private string _pinAvgValue = Placeholder;

    [ObservableProperty]
    private string _pinAvgHint = "";

    [ObservableProperty]
    private double _pinAvgFraction;

    [ObservableProperty]
    private string _pinMonthValue = Placeholder;

    [ObservableProperty]
    private string _pinMonthHint = "";

    [ObservableProperty]
    private double _pinMonthFraction;

    [ObservableProperty]
    private string _pinRolloverValue = Placeholder;

    [ObservableProperty]
    private string _pinRolloverHint = "";

    [ObservableProperty]
    private bool _pinRolloverPositive;

    [ObservableProperty]
    private string _pinTodayHelp = AlwaysOnCapableCopy.PinTodayHelp(false);

    [ObservableProperty]
    private string _pinMonthHelp = AlwaysOnCapableCopy.PinMonthHelp(false);

    [ObservableProperty]
    private string _pinAvgHelp = AlwaysOnCapableCopy.PinAvgHelp(false);

    [ObservableProperty]
    private string _pinRolloverHelp = AlwaysOnCapableCopy.PinRolloverHelp(false);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmSpendBrakeUnlock))]
    private string _spendBrakeTypedConfirm = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConfirmSpendBrakeUnlock))]
    private bool _spendBrakeUnlockInFlight;

    [ObservableProperty]
    private string _spendBrakeUnlockStatus = "";

    public bool SpendBrakeOverlayVisible =>
        _spendBrakeUi == SpendBrakeUiState.Locked;

    public bool CanConfirmSpendBrakeUnlock =>
        SpendBrakeOverlayVisible
        && !SpendBrakeUnlockInFlight
        && SpendBrakeLockUx.MatchesConfirmation(SpendBrakeTypedConfirm);

    public string SpendBrakeConfirmationSentence =>
        SpendBrakeLockUx.ConfirmationSentence;

    public bool SpendBrakeDebugEnabled => UiHostProbes.Enabled;

    public bool HasPlayIp =>
        !string.IsNullOrWhiteSpace(PlayIp) && PlayIp != Placeholder;

    public string StartButtonLabel =>
        _powerAction == PowerActionKind.Start ? "Starting…" : "Start";

    public string StopButtonLabel =>
        _powerAction == PowerActionKind.Stop ? "Stopping…" : "Stop";

    public string RestartButtonLabel =>
        _powerAction == PowerActionKind.Restart ? "Restarting…" : "Restart";

    public bool StatusIsBusy =>
        Status is "Starting…" or "Stopping…" or "Restarting…";

    public string StartToolTip => CanStart
        ? (string.Equals(DoorState, "DEGRADED", StringComparison.OrdinalIgnoreCase)
            ? "The server is on but not joinable. Start retries the wake path."
            : "Start the Minecraft server so friends can connect.")
        : StartDisabledReason;

    public string StopToolTip => CanStop
        ? "Save the world and shut the server down."
        : StopDisabledReason;

    public string RestartToolTip => CanRestart
        ? "Restart Minecraft only. The server stays on."
        : RestartDisabledReason;

    private string StartDisabledReason
    {
        get
        {
            if (!_hasInitialStatus)
                return "Waiting for the first status check.";
            if (_powerActionInFlight)
                return "Wait — a start, stop, or restart is already in progress.";
            if (!ConfigLoaded)
                return "Local config is missing or failed to load.";
            if (ManagePowerUx.IsVm1Running(Vm1Lifecycle)
                || string.Equals(DoorState, "PLAYABLE", StringComparison.OrdinalIgnoreCase))
                return "The server is already on. Use Stop or Restart.";
            if (ManagePowerUx.IsVm1ComingUp(Vm1Lifecycle)
                || string.Equals(DoorState, "STARTING", StringComparison.OrdinalIgnoreCase))
                return "Already starting. Wait until status is Running.";
            if (_spendBrakeUi == SpendBrakeUiState.Locked)
                return "The monthly spend brake is on. Confirm in the warning to unlock, then use Start.";
            if (_spendBrakeUi == SpendBrakeUiState.Unknown)
                return "Can't start: spend-brake lock status is unknown. Check Object Storage, then try Start again.";
            if (!ManagePowerUx.LifecycleAllowsStart(Vm1Lifecycle))
                return ManagePowerUx.WaitUntilFullyStoppedToolTip;
            if (DoorState is Placeholder or "unreachable")
                return "Can't start: the wake service is unreachable. Try Troubleshooting if this lasts.";
            return "Start is unavailable right now.";
        }
    }

    private string StopDisabledReason
    {
        get
        {
            if (!_hasInitialStatus)
                return "Waiting for the first status check.";
            if (_powerActionInFlight)
                return "Wait — a start, stop, or restart is already in progress.";
            if (!ConfigLoaded)
                return "Local config is missing or failed to load.";
            return "Nothing to stop — the server is already off.";
        }
    }

    private string RestartDisabledReason
    {
        get
        {
            if (!_hasInitialStatus)
                return "Waiting for the first status check.";
            if (_powerActionInFlight)
                return "Wait — a start, stop, or restart is already in progress.";
            if (!ConfigLoaded)
                return "Local config is missing or failed to load.";
            return "Can't restart Minecraft while the server is off. Use Start first.";
        }
    }

    public MainViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiClock clock,
        IUiDispatcher dispatcher,
        IClipboard clipboard,
        WindowFocusBroker focus,
        NotificationCenter notices,
        ActionBanner banner)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _clock = clock;
        _dispatcher = dispatcher;
        _clipboard = clipboard;
        _focus = focus;
        _notices = notices;
        _banner = banner;

        BindFromHost();
        _session.ClientsRebuilding += OnClientsRebuilding;
        _session.Reloaded += OnSessionReloaded;
    }

    private void OnClientsRebuilding(object? sender, EventArgs e)
    {
        _resumeChromeAfterReload = _chromeStarted;
        if (_chromeStarted)
            StopChrome();
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        BindFromHost();
        if (!_resumeChromeAfterReload)
            return;
        _resumeChromeAfterReload = false;
        StartChrome();
    }

    /// <summary>
    /// Capture the current <see cref="LocalConfigHost"/> / cloud clients (after
    /// <see cref="ManageSession.ReloadFromDisk"/> or first construction).
    /// </summary>
    private void BindFromHost()
    {
        _config = _configHost.Config;
        _door = _cloud.Door;
        _compute = _cloud.Compute;
        _usageStore = _cloud.UsageStore;
        _spendBrake = _cloud.SpendBrakeLock;
        _oversizedWorld = _cloud.OversizedWorldBackup;
        _ssh = _cloud.Ssh;
        _troubleshooting = _config is not null
            ? new TroubleshootingService(_config, _ssh, _compute, _door)
            : null;
        PlayIp = _configHost.PlayIp;
        ConfigLoaded = _configHost.HasManageConfig && _config is not null;
        _hasInitialStatus = false;
        _pinLedger = UsageLedgerDocument.Empty();
        _powerActionInFlight = false;
        _powerAction = PowerActionKind.None;
        SpendBrakeTypedConfirm = "";
        SpendBrakeUnlockInFlight = false;
        SpendBrakeUnlockStatus = "";
        SetSpendBrakeUi(_spendBrake is null
            ? SpendBrakeUiState.NotConfigured
            : SpendBrakeUiState.Unknown);
        CanStart = false;
        CanStop = false;
        CanRestart = false;
        if (!ConfigLoaded)
        {
            Status = Placeholder;
            StatusIsRunning = false;
            PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(false, null, null);
            Vm1Lifecycle = Placeholder;
            DoorState = Placeholder;
        }

        NotifyPowerTooltips();
        NotifyPowerButtonCaptions();
    }

    /// <summary>
    /// Begin poll + first status/pin load. Call from the manage layout only
    /// (not first-run). Idempotent.
    /// </summary>
    public void StartChrome()
    {
        if (_disposed || _chromeStarted)
            return;

        _chromeStarted = true;
        _windowFocused = _focus.IsFocused;
        _focus.FocusChanged += OnWindowFocusChanged;

        if (!ConfigLoaded || _config is null)
        {
            Status = "Stopped";
            StatusIsRunning = false;
            PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(false, null, null);
            ShowToast(_configHost.LoadResult.Error ?? "Local config failed to load.", isError: true);
            return;
        }

        ApplyPinnedUsage(BuildLocalFallbackPins());

        if (!string.IsNullOrWhiteSpace(_cloud.SessionError))
            ShowToast($"Cloud session failed: {_cloud.SessionError}", isError: true);
        if (!string.IsNullOrWhiteSpace(_cloud.DoorError))
            ShowToast($"Wake service client failed: {_cloud.DoorError}", isError: true);

        StartPoller();
        _ = RefreshStatusAsync(forceDoor: true, forceOci: true);
        _ = RefreshPinsAsync();
        _ = RefreshSpendBrakeLockAsync();
        _ = RefreshOversizedWorldFlagAsync();
    }

    /// <summary>
    /// Stop poll when leaving manage (e.g. after destroy). <see cref="StartChrome"/>
    /// can run again if manage is shown later in this process.
    /// </summary>
    public void StopChrome()
    {
        if (!_chromeStarted)
            return;

        _chromeStarted = false;
        _focus.FocusChanged -= OnWindowFocusChanged;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public void SetWindowFocused(bool focused)
    {
        _windowFocused = focused;
        if (focused)
            _ = RefreshStatusAsync(forceDoor: true, forceOci: true);
    }

    public async Task StartAsync()
    {
        if (_powerActionInFlight || SpendBrakeUnlockInFlight)
            return;

        await RefreshSpendBrakeLockAsync();
        if (_spendBrakeUi == SpendBrakeUiState.Locked)
            return;
        if (_spendBrakeUi == SpendBrakeUiState.Unknown)
        {
            ActionFeedback = StartDisabledReason;
            ShowToast(ActionFeedback, isError: true);
            return;
        }

        if (_door is null || !CanStart)
            return;

        await WakeGameServerAsync();
    }

    /// <summary>
    /// Overlay confirm: park doorbell, DELETE the lock, refresh door OS cache.
    /// Does not wake VM1 — the admin uses top-bar Start.
    /// </summary>
    public async Task ConfirmSpendBrakeUnlockAsync()
    {
        if (!CanConfirmSpendBrakeUnlock || _spendBrake is null)
            return;

        SpendBrakeUnlockInFlight = true;
        SpendBrakeUnlockStatus = "Starting the doorbell and parking the play IP…";
        ShowToast(SpendBrakeUnlockStatus, isError: false);

        try
        {
            if (_troubleshooting is null)
            {
                SpendBrakeUnlockStatus =
                    "Can't recover the doorbell (config/OCI/SSH unavailable). The lock was not cleared.";
                ShowToast(SpendBrakeUnlockStatus, isError: true);
                return;
            }

            var park = await _troubleshooting.ParkPlayIpAsync();
            if (!park.Succeeded)
            {
                SpendBrakeUnlockStatus = park.Summary;
                ShowToast(SpendBrakeUnlockStatus, isError: true);
                return;
            }

            SpendBrakeUnlockStatus = "Clearing the monthly spend-brake lock…";
            var cleared = await _spendBrake.ClearAsync();
            if (!cleared.Succeeded)
            {
                SpendBrakeUnlockStatus = cleared.Error
                    ?? "Could not delete the spend-brake lock. The lock was not cleared.";
                ShowToast(SpendBrakeUnlockStatus, isError: true);
                return;
            }

            SpendBrakeTypedConfirm = "";
            SetSpendBrakeUi(SpendBrakeUiState.Unlocked);
            SpendBrakeUnlockStatus = "Refreshing the doorbell budget cache…";
            var refresh = await _troubleshooting.RefreshOsBudgetAsync();
            if (!refresh.Succeeded)
            {
                ShowToast(
                    "Lock cleared, but the doorbell cache refresh failed. Try Troubleshooting → Refresh OS budget, then Start.",
                    isError: true);
                SpendBrakeUnlockStatus =
                    "Lock cleared. Use Start on the top bar when you are ready (doorbell cache refresh failed).";
                return;
            }

            SpendBrakeUnlockStatus = "Lock cleared. Use Start on the top bar when you are ready.";
            ShowToast(SpendBrakeUnlockStatus, isError: false);
        }
        finally
        {
            SpendBrakeUnlockInFlight = false;
            UpdateCommandFlags();
        }
    }

    public async Task CopySpendBrakeConfirmationAsync()
    {
        await _clipboard.SetTextAsync(SpendBrakeLockUx.ConfirmationSentence);
        SpendBrakeUnlockStatus = "Copied the confirmation sentence.";
        ShowToast(SpendBrakeUnlockStatus, isError: false);
    }

    public async Task RefreshSpendBrakeLockAsync()
    {
        if (_spendBrake is null)
        {
            SetSpendBrakeUi(SpendBrakeUiState.NotConfigured);
            UpdateCommandFlags();
            return;
        }

        var got = await _spendBrake.GetAsync();
        if (!got.Succeeded || got.Value is null)
        {
            SetSpendBrakeUi(SpendBrakeUiState.Unknown);
            if (!string.IsNullOrWhiteSpace(got.Error) && !_powerActionInFlight)
                ActionFeedback = got.Error;
            UpdateCommandFlags();
            return;
        }

        SetSpendBrakeUi(got.Value.Present
            ? SpendBrakeUiState.Locked
            : SpendBrakeUiState.Unlocked);
        UpdateCommandFlags();
    }

    public async Task DebugPutSpendBrakeFixtureAsync()
    {
        if (!UiHostProbes.Enabled || _spendBrake is null)
            return;
        var put = await _spendBrake.PutAsync(SpendBrakeLockDocument.Create());
        if (!put.Succeeded)
        {
            ShowToast(put.Error ?? "DEBUG: could not PUT spend-brake fixture.", isError: true);
            return;
        }

        ShowToast("DEBUG: spend-brake lock fixture written.", isError: false);
        await RefreshSpendBrakeLockAsync();
    }

    public async Task DebugClearSpendBrakeAsync()
    {
        if (!UiHostProbes.Enabled || _spendBrake is null)
            return;
        var cleared = await _spendBrake.ClearAsync();
        if (!cleared.Succeeded)
        {
            ShowToast(cleared.Error ?? "DEBUG: could not DELETE spend-brake lock.", isError: true);
            return;
        }

        SpendBrakeTypedConfirm = "";
        SpendBrakeUnlockStatus = "";
        ShowToast("DEBUG: spend-brake lock deleted (no Start).", isError: false);
        await RefreshSpendBrakeLockAsync();
    }

    public async Task RefreshOversizedWorldFlagAsync()
    {
        if (_oversizedWorld is null)
            return;

        var got = await _oversizedWorld.GetAsync();
        if (!got.Succeeded || got.Value is null)
            return;

        OversizedWorldBackupUx.SyncBell(_notices, got.Value);
    }

    public async Task DebugPutOversizedWorldFixtureAsync()
    {
        if (!UiHostProbes.Enabled || _oversizedWorld is null)
            return;
        var put = await _oversizedWorld.PutAsync(
            OversizedWorldBackupDocument.CreateBlocked(
                archiveSizeBytes: 12_000_000_000,
                softCapBytes: 10_200_547_328));
        if (!put.Succeeded)
        {
            ShowToast(put.Error ?? "DEBUG: could not PUT oversized-world fixture.", isError: true);
            return;
        }

        ShowToast("DEBUG: oversized-world flag fixture written.", isError: false);
        await RefreshOversizedWorldFlagAsync();
    }

    public async Task DebugClearOversizedWorldAsync()
    {
        if (!UiHostProbes.Enabled || _oversizedWorld is null)
            return;
        var cleared = await _oversizedWorld.ClearAsync();
        if (!cleared.Succeeded)
        {
            ShowToast(cleared.Error ?? "DEBUG: could not DELETE oversized-world flag.", isError: true);
            return;
        }

        ShowToast("DEBUG: oversized-world flag deleted.", isError: false);
        await RefreshOversizedWorldFlagAsync();
    }

    private async Task WakeGameServerAsync()
    {
        if (_door is null)
            return;

        BeginPowerAction(PowerActionKind.Start);
        ActionFeedback = "Starting…";
        ShowToast("Starting the game server…", isError: false);

        try
        {
            var wake = await _door.WakeAsync();
            if (!wake.Succeeded)
            {
                ActionFeedback = wake.Error ?? "Start failed.";
                ShowToast(ActionFeedback, isError: true);
                return;
            }

            ActionFeedback = "Start accepted — waiting until friends can connect…";
            await WaitForDoorAsync(
                s => s.IsPlayable || s.IsDegraded || s.IsSpendBrake,
                TimeSpan.FromMinutes(30));
        }
        finally
        {
            EndPowerAction();
            await RefreshStatusAsync(forceDoor: true, forceOci: true);
        }
    }

    public async Task StopAsync()
    {
        if (_door is null || _powerActionInFlight || !CanStop)
            return;

        BeginPowerAction(PowerActionKind.Stop);
        ActionFeedback = "Stopping…";
        ShowToast("Stopping the game server…", isError: false);

        try
        {
            var stop = await _door.IdleEmptyAsync();
            if (!stop.Succeeded)
            {
                ActionFeedback = stop.Error ?? "Stop failed.";
                ShowToast(ActionFeedback, isError: true);
                return;
            }

            ActionFeedback = "Stop accepted — waiting until the server is off…";
            await WaitForDoorAsync(
                s => !s.StopInProgress && (s.IsIdle || s.IsDegraded || s.IsBudgetExhausted),
                TimeSpan.FromMinutes(20));
        }
        finally
        {
            EndPowerAction();
            await RefreshStatusAsync(forceDoor: true, forceOci: true);
        }
    }

    public async Task RestartAsync()
    {
        if (_config is null || _powerActionInFlight || !CanRestart)
            return;

        BeginPowerAction(PowerActionKind.Restart);
        ActionFeedback = "Restarting…";
        ShowToast("Restarting Minecraft…", isError: false);

        try
        {
            var result = await _ssh.RestartMinecraftAsync(_config.Vm1);
            ActionFeedback = result.Succeeded
                ? "Minecraft restarted."
                : result.Error ?? "Restart failed.";
            ShowToast(ActionFeedback, isError: !result.Succeeded);
        }
        finally
        {
            EndPowerAction();
            ApplyNoviceStatus(CachedDoorStatus());
            UpdateCommandFlags();
        }
    }

    public async Task CopyPlayIpAsync()
    {
        if (!HasPlayIp)
        {
            ActionFeedback = "No play IP to copy.";
            ShowToast(ActionFeedback, isError: true);
            return;
        }

        await _clipboard.SetTextAsync(PlayIp);
        CopyPlayIpLabel = "copied";
        _copyLabelCts?.Cancel();
        _copyLabelCts?.Dispose();
        var cts = new CancellationTokenSource();
        _copyLabelCts = cts;
        _ = RestoreCopyLabelAsync(cts.Token);
        ActionFeedback = $"Copied play IP: {PlayIp}";
        ShowToast("Copied play IP.", isError: false);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session.ClientsRebuilding -= OnClientsRebuilding;
        _session.Reloaded -= OnSessionReloaded;
        _focus.FocusChanged -= OnWindowFocusChanged;
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _copyLabelCts?.Cancel();
        _copyLabelCts?.Dispose();
    }

    partial void OnCanStartChanged(bool value) => NotifyPowerTooltips();
    partial void OnCanStopChanged(bool value) => NotifyPowerTooltips();
    partial void OnCanRestartChanged(bool value) => NotifyPowerTooltips();
    partial void OnPlayIpChanged(string value) => OnPropertyChanged(nameof(HasPlayIp));

    private void OnWindowFocusChanged(bool focused) =>
        _ = _dispatcher.InvokeAsync(() => SetWindowFocused(focused));

    private void StartPoller()
    {
        _pollCts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_pollCts.Token);
    }

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = _clock.CreatePeriodicTimer(TimeSpan.FromSeconds(5));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(OnPollTickAsync, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task OnPollTickAsync()
    {
        if (_disposed || _powerActionInFlight)
            return;

        var now = _clock.UtcNow;
        var doorDue = now - _lastDoorPollUtc >= (_windowFocused ? DoorPollInterval : BackgroundPollInterval);
        var ociDue = now - _lastOciPollUtc >= (_windowFocused ? OciPollInterval : BackgroundPollInterval);
        if (!doorDue && !ociDue)
            return;

        await RefreshStatusAsync(forceDoor: doorDue, forceOci: ociDue);
    }

    private async Task RefreshStatusAsync(bool forceDoor, bool forceOci)
    {
        if (_config is null)
            return;

        DoorStatus? doorStatus = null;

        if (forceDoor && _door is not null)
        {
            _lastDoorPollUtc = _clock.UtcNow;
            var doorResult = await _door.GetStatusParsedAsync();
            if (doorResult.Succeeded && doorResult.Value is not null)
            {
                doorStatus = doorResult.Value;
                DoorState = doorStatus.Door;
            }
            else if (!string.IsNullOrWhiteSpace(doorResult.Error))
            {
                DoorState = "unreachable";
                if (!_powerActionInFlight)
                    ActionFeedback = doorResult.Error;
            }
        }

        if (forceOci && _compute is not null)
        {
            _lastOciPollUtc = _clock.UtcNow;
            var life = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId);
            if (life.Succeeded && life.Value is not null)
            {
                Vm1Lifecycle = life.Value;
            }
            else if (life.Error is not null)
            {
                if (!_powerActionInFlight)
                    ActionFeedback = life.Error;
            }
        }

        if (doorStatus is null && DoorState is not (Placeholder or "unreachable"))
            doorStatus = new DoorStatus { Door = DoorState };

        ApplyNoviceStatus(doorStatus);
        _hasInitialStatus = true;
        UpdateCommandFlags(doorStatus);
        if (StatusIsRunning && ManagePowerUx.IsVm1Running(Vm1Lifecycle) && forceDoor)
            await RefreshPlayersPinAsync();
    }

    private void ApplyNoviceStatus(DoorStatus? door)
    {
        if (_powerActionInFlight)
        {
            ApplyInProgressStatus();
            return;
        }

        StatusIsRunning = door?.IsPlayable == true;
        Status = StatusIsRunning ? "Running" : "Stopped";
        if (!StatusIsRunning)
            PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(false, null, null);
    }

    private void UpdateCommandFlags(DoorStatus? door = null)
    {
        door ??= CachedDoorStatus();

        var spendBrakeBlocks = _spendBrakeUi is SpendBrakeUiState.Locked or SpendBrakeUiState.Unknown;
        CanStart = ManagePowerUx.CanStart(
            _hasInitialStatus,
            _powerActionInFlight,
            SpendBrakeUnlockInFlight,
            ConfigLoaded,
            Vm1Lifecycle,
            door?.IsPlayable == true,
            door?.IsStarting == true,
            door?.IsDegraded == true,
            spendBrakeBlocks,
            door is not null);

        var allowPower = _hasInitialStatus && !_powerActionInFlight && !SpendBrakeUnlockInFlight;
        var degraded = door?.IsDegraded == true;
        var alreadyOn = !degraded && (ManagePowerUx.IsVm1Running(Vm1Lifecycle) || door?.IsPlayable == true);
        var starting = !degraded && (ManagePowerUx.IsVm1ComingUp(Vm1Lifecycle) || door?.IsStarting == true);
        CanStop = allowPower && (alreadyOn || starting || degraded);
        CanRestart = allowPower && ManagePowerUx.IsVm1Running(Vm1Lifecycle);
        NotifyPowerTooltips();
    }

    private DoorStatus? CachedDoorStatus() =>
        DoorState is Placeholder or "unreachable"
            ? null
            : new DoorStatus { Door = DoorState };

    private async Task RefreshPlayersPinAsync()
    {
        if (_config is null || _playersPollInFlight || !StatusIsRunning
            || !ManagePowerUx.IsVm1Running(Vm1Lifecycle))
            return;

        _playersPollInFlight = true;
        try
        {
            var run = await _ssh.SendMinecraftRconAsync(_config.Vm1, "list");
            if (!StatusIsRunning)
                return;

            if (!run.Succeeded
                || !MinecraftConsoleRemote.TryParsePlayerList(run.Output, out var online, out var max))
            {
                PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(true, null, null);
                return;
            }

            PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(true, online, max);
        }
        finally
        {
            _playersPollInFlight = false;
        }
    }

    private void BeginPowerAction(PowerActionKind kind)
    {
        _powerAction = kind;
        _powerActionInFlight = true;
        ApplyInProgressStatus();
        UpdateCommandFlags();
        NotifyPowerButtonCaptions();
    }

    private void EndPowerAction()
    {
        _powerAction = PowerActionKind.None;
        _powerActionInFlight = false;
        NotifyPowerButtonCaptions();
    }

    private void ApplyInProgressStatus()
    {
        StatusIsRunning = false;
        Status = _powerAction switch
        {
            PowerActionKind.Start => "Starting…",
            PowerActionKind.Stop => "Stopping…",
            PowerActionKind.Restart => "Restarting…",
            _ => Status
        };
        if (!StatusIsRunning)
            PlayersDisplay = MinecraftConsoleRemote.FormatPlayersPin(false, null, null);
    }

    private void NotifyPowerTooltips()
    {
        OnPropertyChanged(nameof(StartToolTip));
        OnPropertyChanged(nameof(StopToolTip));
        OnPropertyChanged(nameof(RestartToolTip));
    }

    private void NotifyPowerButtonCaptions()
    {
        OnPropertyChanged(nameof(StartButtonLabel));
        OnPropertyChanged(nameof(StopButtonLabel));
        OnPropertyChanged(nameof(RestartButtonLabel));
        OnPropertyChanged(nameof(StatusIsBusy));
    }

    private enum SpendBrakeUiState
    {
        NotConfigured,
        Unknown,
        Unlocked,
        Locked
    }

    private void SetSpendBrakeUi(SpendBrakeUiState state)
    {
        if (_spendBrakeUi == state)
            return;
        _spendBrakeUi = state;
        OnPropertyChanged(nameof(SpendBrakeOverlayVisible));
        OnPropertyChanged(nameof(CanConfirmSpendBrakeUnlock));
        NotifyPowerTooltips();
    }

    private enum PowerActionKind
    {
        None,
        Start,
        Stop,
        Restart
    }

    /// <summary>
    /// Chrome pin pull. Must not grey Start/Stop/Restart (not a power action;
    /// later tab Object Storage polls must follow the same rule).
    /// </summary>
    private async Task RefreshPinsAsync()
    {
        if (_config is null)
            return;

        BudgetConfigDocument budget;
        if (_usageStore is not null)
        {
            var pull = await _usageStore.PullAsync(forceLedger: true, _pinLedger);
            if (!pull.Succeeded || pull.Value is null)
                return;

            _pinLedger = pull.Value.Ledger;
            budget = pull.Value.Budget ?? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
        }
        else
        {
            budget = BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
        }

        var report = UsageMath.ComputeBudgetReport(
            _pinLedger,
            budget.MonthlyOcpuTarget,
            budget.MonthlyGbTarget,
            budget.SoftOcpuCap,
            budget.SoftGbCap);
        ApplyPinnedUsage(PinnedUsageSnapshot.FromReport(report, ResolveShapeOcpus(budget.ShapeOcpus)));
    }

    private PinnedUsageSnapshot BuildLocalFallbackPins()
    {
        var budget = BudgetConfigDocument.FromLocal(_config!.Budget, _config.Vm1);
        var report = UsageMath.ComputeBudgetReport(
            _pinLedger,
            budget.MonthlyOcpuTarget,
            budget.MonthlyGbTarget,
            budget.SoftOcpuCap,
            budget.SoftGbCap);
        return PinnedUsageSnapshot.FromReport(report, ResolveShapeOcpus(budget.ShapeOcpus));
    }

    private double ResolveShapeOcpus(double shapeOcpus)
    {
        if (shapeOcpus > 0)
            return shapeOcpus;
        if (_config is not null && _config.Vm1.ShapeOcpus > 0)
            return _config.Vm1.ShapeOcpus;
        return 4;
    }

    private void ApplyPinnedUsage(PinnedUsageSnapshot snap)
    {
        PinTodayValue = snap.TodayValue;
        PinTodayHint = snap.TodayHint;
        PinTodayFraction = snap.TodayFraction;
        PinAvgValue = snap.AvgValue;
        PinAvgHint = snap.AvgHint;
        PinAvgFraction = snap.AvgFraction;
        PinMonthValue = snap.MonthValue;
        PinMonthHint = snap.MonthHint;
        PinMonthFraction = snap.MonthFraction;
        PinRolloverValue = snap.RolloverValue;
        PinRolloverHint = snap.RolloverHint;
        PinRolloverPositive = snap.RolloverPositive;
        PinTodayHelp = snap.TodayHelp;
        PinMonthHelp = snap.MonthHelp;
        PinAvgHelp = snap.AvgHelp;
        PinRolloverHelp = snap.RolloverHelp;
    }

    private void ShowToast(string message, bool isError)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            _banner.Dismiss();
            return;
        }

        var severity = isError
            ? ActionBannerSeverity.Error
            : ActionBanner.InferSeverity(message);
        _banner.Show(message.Trim(), severity);
    }

    private async Task RestoreCopyLabelAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _clock.Delay(TimeSpan.FromMilliseconds(1200), cancellationToken).ConfigureAwait(false);
            CopyPlayIpLabel = "copy";
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WaitForDoorAsync(Func<DoorStatus, bool> predicate, TimeSpan timeout)
    {
        if (_door is null)
            return;

        var deadline = _clock.UtcNow + timeout;
        var delaySeconds = 3.0;

        while (_clock.UtcNow < deadline)
        {
            var result = await _door.GetStatusParsedAsync();
            _lastDoorPollUtc = _clock.UtcNow;
            if (result.Succeeded && result.Value is not null)
            {
                DoorState = result.Value.Door;
                ApplyNoviceStatus(result.Value);
                if (predicate(result.Value))
                {
                    ActionFeedback = result.Value.IsDegraded
                        ? $"Wake service degraded: {result.Value.LastError}"
                        : result.Value.IsPlayable
                            ? "Server is running."
                            : result.Value.IsSpendBrake
                                ? "The monthly spend brake blocked Start."
                                : "Server is stopped.";
                    ShowToast(
                        ActionFeedback,
                        isError: result.Value.IsDegraded || result.Value.IsSpendBrake);
                    return;
                }
            }

            await _clock.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 15)));
            delaySeconds = Math.Min(delaySeconds * 1.5, 15);
        }

        ActionFeedback = "Timed out waiting for the server to change state.";
        ShowToast(ActionFeedback, isError: true);
    }
}
