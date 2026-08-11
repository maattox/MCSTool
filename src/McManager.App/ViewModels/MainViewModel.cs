using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;

namespace McManager.App.ViewModels;

public partial class MainViewModel : ViewModelBase, IDisposable
{
    private const string Placeholder = "—";
    private static readonly TimeSpan DoorPollInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan OciPollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan BackgroundPollInterval = TimeSpan.FromMinutes(2);

    private ManagerLocalConfig? _config;
    private OciSession? _session;
    private DoorClient? _door;
    private ComputeService? _compute;
    private DispatcherTimer? _pollTimer;
    private DateTime _lastDoorPollUtc = DateTime.MinValue;
    private DateTime _lastOciPollUtc = DateTime.MinValue;
    private bool _windowFocused = true;
    private bool _disposed;

    public string Title { get; } = "OCI MC Server Manager";

    [ObservableProperty]
    private string _status = "Loading local config…";

    [ObservableProperty]
    private string _playIp = Placeholder;

    [ObservableProperty]
    private string _playersDisplay = Placeholder;

    [ObservableProperty]
    private string _todayUsageDisplay = Placeholder;

    [ObservableProperty]
    private string _vm1Lifecycle = Placeholder;

    [ObservableProperty]
    private string _doorState = Placeholder;

    [ObservableProperty]
    private string _actionFeedback = "";

    [ObservableProperty]
    private bool _configLoaded;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _canStart;

    [ObservableProperty]
    private bool _canStop;

    [ObservableProperty]
    private bool _canRestart;

    public WhitelistViewModel? Whitelist { get; private set; }

    public UsageViewModel? Usage { get; private set; }

    public ServerManagementViewModel? ServerManagement { get; private set; }

    public AdvancedViewModel? Advanced { get; private set; }

    public MainViewModel()
    {
        Initialize();
    }

    public void SetWindowFocused(bool focused)
    {
        _windowFocused = focused;
        if (focused)
            _ = RefreshStatusAsync(forceDoor: true, forceOci: true);
    }

    /// <summary>Tab indices: 0 Whitelist, 1 Usage, 2 Server Management, 3 Advanced.</summary>
    public void OnMainTabChanged(int selectedIndex)
    {
        Usage?.OnTabSelected(selectedIndex == 1);
        ServerManagement?.OnTabSelected(selectedIndex == 2);
        Advanced?.OnTabSelected(selectedIndex == 3);
    }

    private void SetTodayUsageDisplay(string text)
    {
        TodayUsageDisplay = string.IsNullOrWhiteSpace(text) ? Placeholder : text;
    }

    private void OnUsageBusyChanged(bool busy)
    {
        IsBusy = busy;
        UpdateCommandFlags();
    }

    private void OnServerManagementBusyChanged(bool busy)
    {
        IsBusy = busy;
        UpdateCommandFlags();
    }

    private void Initialize()
    {
        var loaded = LocalConfigStore.Load();
        if (!loaded.Succeeded || loaded.Config is null)
        {
            Status = loaded.Error ?? "Local config failed to load.";
            return;
        }

        ConfigLoaded = true;
        _config = loaded.Config;
        PlayIp = string.IsNullOrWhiteSpace(_config.Play.ReservedPublicIp)
            ? Placeholder
            : _config.Play.ReservedPublicIp;

        Whitelist = new WhitelistViewModel(
            _config,
            loaded.Friends,
            loaded.DataDirectory ?? "");

        var warn = loaded.Warnings.Count == 0
            ? "no validation warnings"
            : $"{loaded.Warnings.Count} validation warning(s)";

        Status = $"Config OK — region {_config.Oci.Region}, play IP {PlayIp}, {warn}. Waiting for status…";

        var sessionResult = OciSession.TryCreate(_config);
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            Status = $"Config OK, but OCI session failed: {sessionResult.Error}";
            Usage = new UsageViewModel(_config, store: null, SetTodayUsageDisplay, OnUsageBusyChanged);
            ServerManagement = new ServerManagementViewModel(
                _config,
                backups: null,
                new SshService(),
                () => Vm1Lifecycle,
                OnServerManagementBusyChanged);
        }
        else
        {
            _session = sessionResult.Value;
            _compute = new ComputeService(_session);
            var os = new ObjectStorageService(_session, _config.ObjectStorage);
            var usageStore = new UsageBudgetStore(os, _config.ObjectStorage.Prefixes);
            var ssh = new SshService();
            Advanced = new AdvancedViewModel(
                _config,
                _compute,
                usageStore,
                ssh,
                () => Vm1Lifecycle,
                OnAdvancedBusyChanged);
            Usage = new UsageViewModel(_config, usageStore, SetTodayUsageDisplay, OnUsageBusyChanged);
            var backupStore = new BackupStore(os, _config.ObjectStorage);
            ServerManagement = new ServerManagementViewModel(
                _config,
                backupStore,
                ssh,
                () => Vm1Lifecycle,
                OnServerManagementBusyChanged);
        }

        try
        {
            _door = new DoorClient(_config.DoorAdminBaseUrl);
        }
        catch (Exception ex)
        {
            Status = $"Config OK, but door client failed: {ex.Message}";
        }

        StartPoller();
        _ = RefreshStatusAsync(forceDoor: true, forceOci: true);
    }

    private void OnAdvancedBusyChanged(bool busy)
    {
        IsBusy = busy;
        UpdateCommandFlags();
    }

    private void StartPoller()
    {
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _pollTimer.Tick += async (_, _) => await OnPollTickAsync();
        _pollTimer.Start();
    }

    private async Task OnPollTickAsync()
    {
        if (_disposed || IsBusy)
            return;

        var now = DateTime.UtcNow;
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
            _lastDoorPollUtc = DateTime.UtcNow;
            var doorResult = await _door.GetStatusParsedAsync();
            if (doorResult.Succeeded && doorResult.Value is not null)
            {
                doorStatus = doorResult.Value;
                DoorState = doorStatus.Door;
            }
            else if (!string.IsNullOrWhiteSpace(doorResult.Error))
            {
                DoorState = "unreachable";
                if (!IsBusy)
                    ActionFeedback = doorResult.Error;
            }
        }

        if (forceOci && _compute is not null)
        {
            _lastOciPollUtc = DateTime.UtcNow;
            var life = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId);
            if (life.Succeeded && life.Value is not null)
            {
                Vm1Lifecycle = life.Value;
            }
            else if (life.Error is not null)
            {
                if (!IsBusy)
                    ActionFeedback = life.Error;
            }
        }

        // If we didn't refresh door this tick, keep using last DoorState string only.
        if (doorStatus is null && DoorState is not (Placeholder or "unreachable"))
        {
            doorStatus = new DoorStatus { Door = DoorState };
        }

        Status = doorStatus is null
            ? $"VM1: {Vm1Lifecycle}. Door: {DoorState}."
            : $"{doorStatus.ToDisplayLabel(Vm1Lifecycle)} · VM1 {Vm1Lifecycle}";

        UpdateCommandFlags(doorStatus);
    }

    private void UpdateCommandFlags(DoorStatus? door = null)
    {
        door ??= DoorState is Placeholder or "unreachable"
            ? null
            : new DoorStatus { Door = DoorState };

        var life = (Vm1Lifecycle ?? "").ToUpperInvariant();
        var vmRunning = life == "RUNNING";

        CanStart = !IsBusy
                   && door is not null
                   && !door.IsStarting
                   && !door.IsPlayable;

        CanStop = !IsBusy
                  && (door is { IsPlayable: true } or { IsStarting: true } || vmRunning);

        CanRestart = !IsBusy && vmRunning;
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (_door is null || IsBusy)
            return;

        IsBusy = true;
        UpdateCommandFlags();
        ActionFeedback = "Sending wake…";

        try
        {
            var wake = await _door.WakeAsync();
            if (!wake.Succeeded)
            {
                ActionFeedback = wake.Error ?? "Wake failed.";
                return;
            }

            ActionFeedback = "Wake accepted — waiting for playable…";
            await WaitForDoorAsync(
                s => s.IsPlayable || s.IsDegraded || s.IsBudgetExhausted,
                TimeSpan.FromMinutes(20));
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync(forceDoor: true, forceOci: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (_door is null || IsBusy)
            return;

        IsBusy = true;
        UpdateCommandFlags();
        ActionFeedback = "Sending idle-empty (door SoftStop + IP handback)…";

        try
        {
            var stop = await _door.IdleEmptyAsync();
            if (!stop.Succeeded)
            {
                ActionFeedback = stop.Error ?? "Stop failed.";
                return;
            }

            ActionFeedback = "Stop accepted — waiting for idle…";
            await WaitForDoorAsync(
                s => s.IsIdle || s.IsDegraded || s.IsBudgetExhausted,
                TimeSpan.FromMinutes(20));
        }
        finally
        {
            IsBusy = false;
            await RefreshStatusAsync(forceDoor: true, forceOci: true);
        }
    }

    [RelayCommand(CanExecute = nameof(CanRestart))]
    private async Task RestartAsync()
    {
        if (_config is null || IsBusy)
            return;

        IsBusy = true;
        UpdateCommandFlags();
        ActionFeedback = "Restarting Minecraft via SSH…";

        try
        {
            var ssh = new SshService();
            var result = await ssh.RestartMinecraftAsync(_config.Vm1);
            ActionFeedback = result.Succeeded
                ? "Minecraft restarted."
                : result.Error ?? "Restart failed.";
        }
        finally
        {
            IsBusy = false;
            UpdateCommandFlags();
        }
    }

    [RelayCommand]
    private async Task CopyPlayIpAsync()
    {
        if (string.IsNullOrWhiteSpace(PlayIp) || PlayIp == Placeholder)
        {
            ActionFeedback = "No play IP to copy.";
            return;
        }

        var lifetime = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
        var clipboard = lifetime?.MainWindow?.Clipboard;
        if (clipboard is null)
        {
            ActionFeedback = "Clipboard unavailable.";
            return;
        }

        await clipboard.SetTextAsync(PlayIp);
        ActionFeedback = $"Copied play IP: {PlayIp}";
    }

    private async Task WaitForDoorAsync(Func<DoorStatus, bool> predicate, TimeSpan timeout)
    {
        if (_door is null)
            return;

        var deadline = DateTime.UtcNow + timeout;
        var delaySeconds = 3.0;

        while (DateTime.UtcNow < deadline)
        {
            var result = await _door.GetStatusParsedAsync();
            _lastDoorPollUtc = DateTime.UtcNow;
            if (result.Succeeded && result.Value is not null)
            {
                DoorState = result.Value.Door;
                Status = $"{result.Value.ToDisplayLabel(Vm1Lifecycle)} · VM1 {Vm1Lifecycle}";
                if (predicate(result.Value))
                {
                    ActionFeedback = result.Value.IsDegraded
                        ? $"Door degraded: {result.Value.LastError}"
                        : $"Door: {result.Value.Door}";
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Min(delaySeconds, 15)));
            delaySeconds = Math.Min(delaySeconds * 1.5, 15);
        }

        ActionFeedback = "Timed out waiting for door status change.";
    }

    partial void OnCanStartChanged(bool value) => StartCommand.NotifyCanExecuteChanged();
    partial void OnCanStopChanged(bool value) => StopCommand.NotifyCanExecuteChanged();
    partial void OnCanRestartChanged(bool value) => RestartCommand.NotifyCanExecuteChanged();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _pollTimer?.Stop();
        Usage?.Dispose();
        _door?.Dispose();
        _session?.Dispose();
    }
}
