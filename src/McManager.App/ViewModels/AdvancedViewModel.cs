using Avalonia.Controls.ApplicationLifetimes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.App.Dialogs;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;

namespace McManager.App.ViewModels;

public partial class AdvancedViewModel : ViewModelBase
{
    private readonly ManagerLocalConfig _config;
    private readonly ComputeService _compute;
    private readonly UsageBudgetStore? _budgetStore;
    private readonly ISshService _ssh;
    private readonly Func<string> _getVm1Lifecycle;
    private readonly Action<bool> _setBusy;
    private BudgetConfigDocument? _lastBudget;

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

    public AdvancedViewModel(
        ManagerLocalConfig config,
        ComputeService compute,
        UsageBudgetStore? budgetStore,
        ISshService ssh,
        Func<string> getVm1Lifecycle,
        Action<bool> setBusy)
    {
        _config = config;
        _compute = compute;
        _budgetStore = budgetStore;
        _ssh = ssh;
        _getVm1Lifecycle = getVm1Lifecycle;
        _setBusy = setBusy;
        SeedIdleFromLocal();
    }

    public void OnTabSelected(bool selected)
    {
        if (selected)
            _ = RefreshIdleFromOsAsync();
    }

    [RelayCommand]
    private async Task RefreshIdleFromOsAsync()
    {
        if (_budgetStore is null)
        {
            StatusMessage = "Object Storage unavailable — using local config for idle fields.";
            SeedIdleFromLocal();
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        _setBusy(true);
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

            var budget = pull.Value.Budget ?? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
            _lastBudget = budget;
            ApplyBudgetToIdleEdit(budget);
            StatusMessage = pull.Value.BudgetMissing
                ? "budget/config.json missing — seeded from local config."
                : "Idle settings loaded from Object Storage budget.";
        }
        finally
        {
            IsBusy = false;
            _setBusy(false);
        }
    }

    [RelayCommand]
    private async Task ApplyIdleSettingsAsync()
    {
        if (IsBusy)
            return;

        if (!TryParseIdleEdit(out var timeout, out var warn, out var error))
        {
            StatusMessage = error;
            return;
        }

        var enabling = EditIdleAgentEnabled;
        var window = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;

        if (!enabling)
        {
            var confirmed = await ConfirmDialog.ShowAsync(
                window,
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
            var confirmed = await ConfirmDialog.ShowAsync(
                window,
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
        _setBusy(true);
        StatusMessage = "Publishing budget…";

        try
        {
            if (_budgetStore is null)
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

            var life = (_getVm1Lifecycle() ?? "").ToUpperInvariant();
            if (life != "RUNNING")
            {
                StatusMessage =
                    $"{published.Value.Message} ({published.Value.Flags.SummarizeBudgetFlags()}). "
                    + $"VM1 is '{_getVm1Lifecycle()}' — SSH apply skipped. "
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
            _setBusy(false);
        }
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

    private void SeedIdleFromLocal()
    {
        ApplyBudgetToIdleEdit(BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1));
    }

    private void ApplyBudgetToIdleEdit(BudgetConfigDocument budget)
    {
        EditIdleTimeout = budget.IdleTimeoutMinutes.ToString();
        EditBudgetWarn = budget.BudgetWarnMinutes.ToString();
        EditIdleAgentEnabled = budget.IdleAgentEnabled;
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
