using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Danger Zone Minecraft heap presets. Apply rewrites the guest launch and restarts Minecraft.
/// VM1 must be RUNNING (SSH). Does not stop the VM.
/// </summary>
public sealed partial class JvmHeapViewModel : ObservableObject
{
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private readonly IUiDialogs _dialogs;

    private ManagerLocalConfig? _config;
    private ISshService _ssh = null!;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Minecraft heap is 4G, 6G, or 8G. Apply restarts Minecraft; the VM stays up.";

    [ObservableProperty]
    private string _currentHeap = JvmHeapChoice.Default;

    [ObservableProperty]
    private string _targetHeap = JvmHeapChoice.Default;

    public bool TargetIs4G => TargetHeap == JvmHeapChoice.Default;
    public bool TargetIs6G => TargetHeap == JvmHeapChoice.Medium;
    public bool TargetIs8G => TargetHeap == JvmHeapChoice.Large;

    public string CurrentHeapDisplay => JvmHeapChoice.Format(CurrentHeap);

    public string BlockedReason
    {
        get
        {
            if (_config is null)
                return "Local config is missing.";
            if (IsBusy)
                return "";
            if (!ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle))
                return "Start the server from the sidebar first — applying heap needs SSH.";
            return "";
        }
    }

    public bool CanApply =>
        !IsBusy
        && _config is not null
        && ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle);

    public JvmHeapViewModel(
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
        SeedFromLocal();
        _main.PropertyChanged += OnMainChanged;
        _session.Reloaded += OnSessionReloaded;
    }

    public void Select4G() => TargetHeap = JvmHeapChoice.Default;

    public void Select6G() => TargetHeap = JvmHeapChoice.Medium;

    public void Select8G() => TargetHeap = JvmHeapChoice.Large;

    public async Task ApplyAsync()
    {
        if (!CanApply || _config is null)
            return;

        var heap = JvmHeapChoice.Normalize(TargetHeap);
        var confirmed = await _dialogs.ConfirmAsync(
            "Change Minecraft memory?",
            "This rewrites the Minecraft launch line to "
            + heap
            + " (Xms = Xmx) and restarts Minecraft. Paper keeps its Fill GC flags. "
            + "Do not use /reload. The VM stays up.",
            confirmButtonText: "Apply heap");
        if (!confirmed)
        {
            StatusMessage = "Heap change cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Applying {heap} heap and restarting Minecraft…";
        NotifyDerived();
        try
        {
            var result = await _ssh.ApplyJvmHeapAsync(_config.Vm1, heap);
            if (!result.Succeeded)
            {
                StatusMessage = result.Error ?? "Heap apply failed.";
                return;
            }

            _config.Vm1.JvmXmx = heap;
            var saved = LocalConfigStore.SaveConfig(_config);
            if (!saved.Succeeded)
            {
                StatusMessage =
                    $"Minecraft heap is now {heap} on the guest, but saving config.local.json failed: "
                    + (saved.Error ?? "unknown");
                return;
            }

            _session.ReloadFromDisk();
            CurrentHeap = heap;
            TargetHeap = heap;
            StatusMessage = $"Minecraft heap is {heap}. Minecraft was restarted.";
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    private void SeedFromLocal()
    {
        var heap = JvmHeapChoice.Normalize(_config?.Vm1.JvmXmx);
        CurrentHeap = heap;
        TargetHeap = heap;
        NotifyDerived();
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        BindFromHost();
        SeedFromLocal();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle) or null)
            NotifyDerived();
    }

    partial void OnTargetHeapChanged(string value) => NotifyDerived();

    partial void OnCurrentHeapChanged(string value) => NotifyDerived();

    partial void OnIsBusyChanged(bool value) => NotifyDerived();

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(TargetIs4G));
        OnPropertyChanged(nameof(TargetIs6G));
        OnPropertyChanged(nameof(TargetIs8G));
        OnPropertyChanged(nameof(CurrentHeapDisplay));
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(CanApply));
    }
}
