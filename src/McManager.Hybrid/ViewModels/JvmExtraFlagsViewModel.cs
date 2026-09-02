using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Danger Zone non-heap JVM flags. Save confirms, then SSH apply + Minecraft restart.
/// Heap radios stay the only Xms/Xmx control.
/// </summary>
public sealed partial class JvmExtraFlagsViewModel : ObservableObject
{
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private readonly IUiDialogs _dialogs;

    private ManagerLocalConfig? _config;
    private ISshService _ssh = null!;
    private bool _loadQueued;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage =
        "Extra JVM flags (not heap). Save restarts Minecraft; the VM stays up. Paper empty save restores Fill/Aikar flags.";

    [ObservableProperty]
    private string _flagsText = "";

    public string BlockedReason
    {
        get
        {
            if (_config is null)
                return "Local config is missing.";
            if (IsBusy)
                return "";
            if (!ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle))
                return "Start the server from the sidebar first — loading and saving flags needs SSH.";
            return "";
        }
    }

    public bool CanSave =>
        !IsBusy
        && _config is not null
        && ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle);

    public bool CanLoad => CanSave;

    public JvmExtraFlagsViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        MainViewModel main,
        IUiDialogs dialogs)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _main = main;
        _dialogs = dialogs;

        BindFromHost();
        _main.PropertyChanged += OnMainChanged;
        _session.Reloaded += OnSessionReloaded;
    }

    public void OnPaneSelected()
    {
        if (CanLoad)
            _ = LoadAsync();
    }

    public async Task LoadAsync()
    {
        if (!CanLoad || _config is null)
            return;

        IsBusy = true;
        StatusMessage = "Loading JVM flags from the game VM…";
        NotifyDerived();
        try
        {
            var result = await _ssh.DumpJvmExtraFlagsAsync(_config.Vm1);
            if (!result.Succeeded)
            {
                StatusMessage = result.Error ?? "Could not load JVM flags.";
                return;
            }

            FlagsText = JvmExtraFlags.Format(result.Value ?? []);
            StatusMessage = "Loaded current extra flags. Heap stays on the card above.";
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    public async Task SaveAsync()
    {
        if (!CanSave || _config is null)
            return;

        var strippedHeap = JvmExtraFlags.ContainedHeapTokens(FlagsText);
        var flags = JvmExtraFlags.Parse(FlagsText);
        var body =
            "Changing JVM flags can stop Minecraft from starting. This rewrites the launch extras, "
            + "restarts Minecraft, and leaves the VM up. Do not use /reload. Heap (-Xms/-Xmx) stays on the card above.";
        if (strippedHeap)
            body += " Any -Xms/-Xmx in the box will be ignored.";

        var confirmed = await _dialogs.ConfirmAsync(
            "Save JVM flags?",
            body,
            confirmButtonText: "Save");
        if (!confirmed)
        {
            StatusMessage = "Flag change cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Applying JVM flags and restarting Minecraft…";
        NotifyDerived();
        try
        {
            var result = await _ssh.ApplyJvmExtraFlagsAsync(_config.Vm1, flags);
            if (!result.Succeeded)
            {
                StatusMessage = result.Error ?? "Flag apply failed.";
                return;
            }

            var dumped = await _ssh.DumpJvmExtraFlagsAsync(_config.Vm1);
            if (dumped.Succeeded)
                FlagsText = JvmExtraFlags.Format(dumped.Value ?? []);
            else
                FlagsText = JvmExtraFlags.Format(flags);
            StatusMessage = "JVM flags saved. Minecraft was restarted.";
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        BindFromHost();
        NotifyDerived();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle) or null)
        {
            NotifyDerived();
            if (CanLoad && !_loadQueued)
            {
                _loadQueued = true;
                _ = LoadOnceWhenRunningAsync();
            }
        }
    }

    private async Task LoadOnceWhenRunningAsync()
    {
        try
        {
            await LoadAsync();
        }
        finally
        {
            _loadQueued = false;
        }
    }

    partial void OnIsBusyChanged(bool value) => NotifyDerived();

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanLoad));
    }
}
