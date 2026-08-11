using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.App.Dialogs;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;

namespace McManager.App.ViewModels;

public partial class UsageViewModel : ViewModelBase, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private readonly ManagerLocalConfig _config;
    private readonly UsageBudgetStore? _store;
    private readonly Action<string> _setTodayUsage;
    private readonly Action<bool>? _setBusy;

    private UsageLedgerDocument _ledger = UsageLedgerDocument.Empty();
    private DispatcherTimer? _timer;
    private bool _tabSelected;
    private bool _disposed;
    private bool _seededEdit;

    [ObservableProperty]
    private string _statusMessage = "Open this tab to pull usage from Object Storage.";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _lastRefreshDisplay = "—";

    [ObservableProperty]
    private string _monthLabel = "—";

    [ObservableProperty]
    private string _monthlyTargetsDisplay = "—";

    [ObservableProperty]
    private string _softCapsDisplay = "—";

    [ObservableProperty]
    private string _mtdDisplay = "—";

    [ObservableProperty]
    private string _avgHoursDisplay = "—";

    [ObservableProperty]
    private string _leftoverDisplay = "—";

    [ObservableProperty]
    private string _todayDisplay = "—";

    [ObservableProperty]
    private string _softCapHitDisplay = "—";

    [ObservableProperty]
    private string _editMonthlyOcpu = "";

    [ObservableProperty]
    private string _editMonthlyGb = "";

    [ObservableProperty]
    private string _editSoftOcpu = "";

    [ObservableProperty]
    private string _editSoftGb = "";

    [ObservableProperty]
    private string _editIdleTimeout = "";

    [ObservableProperty]
    private string _editBudgetWarn = "";

    [ObservableProperty]
    private string _editShapeOcpus = "";

    [ObservableProperty]
    private string _editShapeMemory = "";

    [ObservableProperty]
    private bool _editIdleAgentEnabled = true;

    public bool HasObjectStorage => _store is not null;

    public UsageViewModel(
        ManagerLocalConfig config,
        UsageBudgetStore? store,
        Action<string> setTodayUsage,
        Action<bool>? setBusy = null)
    {
        _config = config;
        _store = store;
        _setTodayUsage = setTodayUsage;
        _setBusy = setBusy;
        SeedEditFromLocal();
    }

    public void OnTabSelected(bool selected)
    {
        if (_disposed)
            return;

        _tabSelected = selected;
        if (selected)
        {
            EnsureTimer();
            _timer!.Start();
            _ = RefreshAsync(forceLedger: true);
        }
        else
        {
            _timer?.Stop();
        }
    }

    [RelayCommand]
    private async Task RefreshAsync() => await RefreshAsync(forceLedger: true);

    [RelayCommand]
    private async Task PublishAsync()
    {
        if (_store is null || IsBusy)
            return;

        if (!TryBuildBudgetFromEdit(out var doc, out var error))
        {
            StatusMessage = error;
            return;
        }

        var owner = (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow;
        var confirmed = await ConfirmDialog.ShowAsync(
            owner,
            "Publish budget?",
            "This publishes budget/config.json to Object Storage and notifies door + VM1 (dirty flags). Continue?");
        if (!confirmed)
        {
            StatusMessage = "Publish cancelled.";
            return;
        }

        IsBusy = true;
        _setBusy?.Invoke(true);
        StatusMessage = "Publishing budget…";

        try
        {
            var result = await _store.PublishBudgetAsync(doc);
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "Publish failed.";
                return;
            }

            ApplyBudgetToEdit(result.Value.Budget);
            StatusMessage = $"{result.Value.Message} ({result.Value.Flags.SummarizeBudgetFlags()})";
            await RefreshAsync(forceLedger: true, manageBusy: false);
        }
        finally
        {
            IsBusy = false;
            _setBusy?.Invoke(false);
        }
    }

    private async Task RefreshAsync(bool forceLedger, bool manageBusy = true)
    {
        if (_store is null)
        {
            StatusMessage = "Object Storage is not configured / OCI session unavailable.";
            ApplyReportFromLocalFallback();
            return;
        }

        if (IsBusy && manageBusy && !forceLedger)
            return;

        if (manageBusy)
        {
            IsBusy = true;
            _setBusy?.Invoke(true);
        }

        try
        {
            var pull = await _store.PullAsync(forceLedger, _ledger);
            if (!pull.Succeeded || pull.Value is null)
            {
                StatusMessage = pull.Error ?? "Pull failed.";
                return;
            }

            var snap = pull.Value;
            _ledger = snap.Ledger;

            BudgetConfigDocument budget;
            if (snap.Budget is not null)
            {
                budget = snap.Budget;
                if (!_seededEdit || forceLedger)
                    ApplyBudgetToEdit(budget);
            }
            else
            {
                budget = BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
                if (!_seededEdit)
                    ApplyBudgetToEdit(budget);
            }

            var report = UsageMath.ComputeBudgetReport(
                _ledger,
                budget.MonthlyOcpuTarget,
                budget.MonthlyGbTarget,
                budget.SoftOcpuCap,
                budget.SoftGbCap);

            ApplyReport(report);
            LastRefreshDisplay = DateTime.Now.ToString("HH:mm:ss");
            var refreshNote = string.IsNullOrWhiteSpace(snap.Notes)
                ? $"Refreshed at {LastRefreshDisplay}."
                : $"{snap.Notes} ({LastRefreshDisplay})";
            if (!StatusMessage.StartsWith("Published", StringComparison.Ordinal))
                StatusMessage = refreshNote;
            else
                StatusMessage = $"{StatusMessage} · {refreshNote}";
        }
        finally
        {
            if (manageBusy)
            {
                IsBusy = false;
                _setBusy?.Invoke(false);
            }
        }
    }

    private void ApplyReport(BudgetReport report)
    {
        MonthLabel = $"{report.Year}-{report.Month:D2} UTC ({report.DaysInMonth} days)";
        MonthlyTargetsDisplay =
            $"{report.MonthlyOcpuTarget:F0} OCPU-h / {report.MonthlyGbTarget:F0} GB-h";
        SoftCapsDisplay =
            $"{report.SoftOcpuCap:F0} OCPU-h / {report.SoftGbCap:F0} GB-h";
        MtdDisplay =
            $"{report.MonthOcpu:F1} OCPU-h · {report.MonthGb:F1} GB-h · {report.MonthUptime:F1} instance-h";
        AvgHoursDisplay = $"{report.AvgHoursPerDay:F2} h/day";
        LeftoverDisplay =
            $"{report.LeftoverOcpu:F1} OCPU-h / {report.LeftoverGb:F1} GB-h";
        TodayDisplay =
            $"{report.TodayOcpu:F1} / {report.DailyOcpuAllowance:F1} OCPU-h"
            + (report.OcpuOverDaily ? " (over daily)" : "");
        SoftCapHitDisplay = report.HitSoftCap ? "Yes — soft cap hit" : "No";
        _setTodayUsage(report.FormatTodayBar());
    }

    private void ApplyReportFromLocalFallback()
    {
        var budget = BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1);
        ApplyBudgetToEdit(budget);
        var report = UsageMath.ComputeBudgetReport(
            _ledger,
            budget.MonthlyOcpuTarget,
            budget.MonthlyGbTarget,
            budget.SoftOcpuCap,
            budget.SoftGbCap);
        ApplyReport(report);
    }

    private void SeedEditFromLocal()
    {
        ApplyBudgetToEdit(BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1));
    }

    private void ApplyBudgetToEdit(BudgetConfigDocument budget)
    {
        EditMonthlyOcpu = budget.MonthlyOcpuTarget.ToString("G");
        EditMonthlyGb = budget.MonthlyGbTarget.ToString("G");
        EditSoftOcpu = budget.SoftOcpuCap.ToString("G");
        EditSoftGb = budget.SoftGbCap.ToString("G");
        EditIdleTimeout = budget.IdleTimeoutMinutes.ToString();
        EditBudgetWarn = budget.BudgetWarnMinutes.ToString();
        EditShapeOcpus = budget.ShapeOcpus.ToString("G");
        EditShapeMemory = budget.ShapeMemoryGb.ToString("G");
        EditIdleAgentEnabled = budget.IdleAgentEnabled;
        _seededEdit = true;
    }

    private bool TryBuildBudgetFromEdit(out BudgetConfigDocument doc, out string error)
    {
        doc = new BudgetConfigDocument();
        error = "";

        if (!TryParseDouble(EditMonthlyOcpu, "Monthly OCPU target", out var monthlyOcpu)
            || !TryParseDouble(EditMonthlyGb, "Monthly GB target", out var monthlyGb)
            || !TryParseDouble(EditSoftOcpu, "Soft OCPU cap", out var softOcpu)
            || !TryParseDouble(EditSoftGb, "Soft GB cap", out var softGb)
            || !TryParseInt(EditIdleTimeout, "Idle timeout minutes", out var idleTimeout)
            || !TryParseInt(EditBudgetWarn, "Budget warn minutes", out var warnMinutes)
            || !TryParseDouble(EditShapeOcpus, "Shape OCPUs", out var shapeOcpus)
            || !TryParseDouble(EditShapeMemory, "Shape memory GB", out var shapeMemory))
        {
            error = _lastParseError;
            return false;
        }

        if (idleTimeout < 1 || warnMinutes < 0)
        {
            error = "Idle timeout must be ≥ 1; warn minutes must be ≥ 0.";
            return false;
        }

        doc = new BudgetConfigDocument
        {
            MonthlyOcpuTarget = monthlyOcpu,
            MonthlyGbTarget = monthlyGb,
            SoftOcpuCap = softOcpu,
            SoftGbCap = softGb,
            IdleTimeoutMinutes = idleTimeout,
            BudgetWarnMinutes = warnMinutes,
            ShapeOcpus = shapeOcpus,
            ShapeMemoryGb = shapeMemory,
            IdleAgentEnabled = EditIdleAgentEnabled,
            Mode = "always_free",
        };
        return true;
    }

    private string _lastParseError = "";

    private bool TryParseDouble(string text, string label, out double value)
    {
        if (double.TryParse(text.Trim(), out value) && value >= 0)
            return true;
        _lastParseError = $"Invalid {label}.";
        value = 0;
        return false;
    }

    private bool TryParseInt(string text, string label, out int value)
    {
        if (int.TryParse(text.Trim(), out value))
            return true;
        _lastParseError = $"Invalid {label}.";
        value = 0;
        return false;
    }

    private void EnsureTimer()
    {
        if (_timer is not null)
            return;

        _timer = new DispatcherTimer { Interval = PollInterval };
        _timer.Tick += async (_, _) =>
        {
            if (_tabSelected && !IsBusy)
                await RefreshAsync(forceLedger: false);
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer?.Stop();
        _timer = null;
    }
}
