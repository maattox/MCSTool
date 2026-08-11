using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.App.ViewModels;

public partial class AdvancedViewModel : ViewModelBase
{
    private readonly ManagerLocalConfig _config;
    private readonly ComputeService _compute;
    private readonly Action<bool> _setBusy;

    [ObservableProperty]
    private string _statusMessage =
        "Break-glass Compute actions do not move the reserved play IP. Prefer top-bar Start/Stop (door-aware).";

    [ObservableProperty]
    private bool _isBusy;

    public AdvancedViewModel(ManagerLocalConfig config, ComputeService compute, Action<bool> setBusy)
    {
        _config = config;
        _compute = compute;
        _setBusy = setBusy;
    }

    [RelayCommand]
    private async Task BreakGlassStartAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _setBusy(true);
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
            _setBusy(false);
        }
    }

    [RelayCommand]
    private async Task BreakGlassSoftStopAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        _setBusy(true);
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
            _setBusy(false);
        }
    }
}
