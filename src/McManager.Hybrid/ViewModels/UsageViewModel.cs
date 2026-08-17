using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Usage tab: dashboard, budget edit/publish, ~2 min poll while the tab is alive.
/// Does not touch manage-chrome power-in-flight. Remaining-in-month stays here,
/// not on the rollover pin.
/// </summary>
public sealed partial class UsageViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private readonly ManagerLocalConfig? _config;
    private readonly UsageBudgetStore? _store;
    private readonly IUiClock _clock;
    private readonly IUiDialogs _dialogs;
    private readonly MainViewModel? _main;

    private UsageLedgerDocument _ledger = UsageLedgerDocument.Empty();
    private CancellationTokenSource? _pollCts;
    private bool _disposed;
    private bool _seededEdit;
    private string _savedBudgetFingerprint = "";
    private bool _suppressBudgetDirty;
    private string _lastParseError = "";

    [ObservableProperty]
    private string _statusMessage = "Open this tab to refresh hours used.";

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
    private bool _hitSoftCap;

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

    [ObservableProperty]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string _remainingDisplay = "—";

    [ObservableProperty]
    private string _remainingHoursValue = "—";

    [ObservableProperty]
    private string _usedHoursValue = "—";

    [ObservableProperty]
    private string _todayHoursValue = "—";

    [ObservableProperty]
    private string _todayHoursHint = "";

    [ObservableProperty]
    private string _rolloverHoursValue = "—";

    [ObservableProperty]
    private bool _rolloverHoursPositive;

    [ObservableProperty]
    private bool _todayOverDaily;

    public bool HasObjectStorage => _store is not null;

    public bool CanPublish => HasPendingChanges && !IsBusy && HasObjectStorage;

    public UsageViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        IUiClock clock,
        IUiDialogs dialogs,
        MainViewModel? main = null)
    {
        _config = configHost.Config;
        _store = cloud.UsageStore;
        _clock = clock;
        _dialogs = dialogs;
        _main = main;
        SeedEditFromLocal();
        ApplyReportFromLocalFallback();
    }

    /// <summary>Start the ~2 min poll and pull once. Call when the tab component is created.</summary>
    public void StartWatching()
    {
        if (_disposed)
            return;

        StopWatching();
        _pollCts = new CancellationTokenSource();
        _ = RunPollLoopAsync(_pollCts.Token);
        _ = RefreshAsync(forceLedger: true);
    }

    /// <summary>Stop the poll timer. Call when the tab component is disposed (tab left).</summary>
    public void StopWatching()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = null;
    }

    public Task RefreshAsync() => RefreshAsync(forceLedger: true);

    public async Task PublishAsync()
    {
        if (_store is null || IsBusy || !HasPendingChanges)
            return;

        if (!TryBuildBudgetFromEdit(out var doc, out var error))
        {
            StatusMessage = error;
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Save usage budget?",
            "This updates the shared hours budget the server uses to stop itself when you run out of free time. Continue?",
            confirmButtonText: "Publish");
        if (!confirmed)
        {
            StatusMessage = "Publish cancelled.";
            return;
        }

        IsBusy = true;
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
            CaptureBudgetSnapshot();
            StatusMessage = $"{result.Value.Message} ({result.Value.Flags.SummarizeBudgetFlags()})";
            await RefreshAsync(forceLedger: true, manageBusy: false);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunPollLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = _clock.CreatePeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (!IsBusy)
                    await RefreshAsync(forceLedger: false).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshAsync(bool forceLedger, bool manageBusy = true)
    {
        if (_store is null)
        {
            StatusMessage = _config is null
                ? "Local config isn't loaded. Showing default budget numbers only."
                : "Shared hours storage isn't available. Showing local budget numbers only.";
            ApplyReportFromLocalFallback();
            return;
        }

        if (IsBusy && manageBusy && !forceLedger)
            return;

        if (manageBusy)
            IsBusy = true;

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
                if (!_seededEdit || (forceLedger && !HasPendingChanges))
                    ApplyBudgetToEdit(budget);
            }
            else
            {
                budget = LocalBudget();
                if (!_seededEdit)
                    ApplyBudgetToEdit(budget);
            }

            var report = UsageMath.ComputeBudgetReport(
                _ledger,
                budget.MonthlyOcpuTarget,
                budget.MonthlyGbTarget,
                budget.SoftOcpuCap,
                budget.SoftGbCap);

            ApplyReport(report, budget.ShapeOcpus);
            LastRefreshDisplay = _clock.UtcNow.ToLocalTime().ToString("HH:mm:ss");
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
                IsBusy = false;
        }
    }

    private void ApplyReport(BudgetReport report, double shapeOcpus)
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
        HitSoftCap = report.HitSoftCap;
        var shape = ResolveShapeOcpus(shapeOcpus);
        var dailyHours = report.DailyOcpuAllowance / shape;
        var remainingHours = Math.Max(0, report.MonthlyOcpuTarget - report.MonthOcpu) / shape;
        var rolloverHours = report.LeftoverOcpu / shape;
        RemainingDisplay = $"{remainingHours:F1}h left this month (not rollover)";
        RemainingHoursValue = $"{remainingHours:F1}h";
        UsedHoursValue = $"{report.MonthUptime:F1}h";
        TodayHoursValue = $"{report.TodayUptimeHours:F1}h";
        TodayHoursHint = $"/ {dailyHours:F1}h allowed today";
        TodayOverDaily = report.OcpuOverDaily;
        RolloverHoursValue = $"{(rolloverHours >= 0 ? "+" : "")}{rolloverHours:F1}h";
        RolloverHoursPositive = rolloverHours > 0.05;
        CopyPins(PinnedUsageSnapshot.FromReport(report, shape));
    }

    private void CopyPins(PinnedUsageSnapshot snap)
    {
        if (_main is null)
            return;

        _main.PinTodayValue = snap.TodayValue;
        _main.PinTodayHint = snap.TodayHint;
        _main.PinTodayFraction = snap.TodayFraction;
        _main.PinAvgValue = snap.AvgValue;
        _main.PinAvgHint = snap.AvgHint;
        _main.PinAvgFraction = snap.AvgFraction;
        _main.PinMonthValue = snap.MonthValue;
        _main.PinMonthHint = snap.MonthHint;
        _main.PinMonthFraction = snap.MonthFraction;
        _main.PinRolloverValue = snap.RolloverValue;
        _main.PinRolloverHint = snap.RolloverHint;
        _main.PinRolloverPositive = snap.RolloverPositive;
    }

    private double ResolveShapeOcpus(double shapeOcpus)
    {
        if (shapeOcpus > 0)
            return shapeOcpus;
        if (_config is not null && _config.Vm1.ShapeOcpus > 0)
            return _config.Vm1.ShapeOcpus;
        return 4;
    }

    private BudgetConfigDocument LocalBudget() =>
        _config is not null
            ? BudgetConfigDocument.FromLocal(_config.Budget, _config.Vm1)
            : new BudgetConfigDocument();

    private void ApplyReportFromLocalFallback()
    {
        var budget = LocalBudget();
        ApplyBudgetToEdit(budget);
        var report = UsageMath.ComputeBudgetReport(
            _ledger,
            budget.MonthlyOcpuTarget,
            budget.MonthlyGbTarget,
            budget.SoftOcpuCap,
            budget.SoftGbCap);
        ApplyReport(report, budget.ShapeOcpus);
    }

    private void SeedEditFromLocal() => ApplyBudgetToEdit(LocalBudget());

    private void ApplyBudgetToEdit(BudgetConfigDocument budget)
    {
        _suppressBudgetDirty = true;
        try
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
            CaptureBudgetSnapshot();
        }
        finally
        {
            _suppressBudgetDirty = false;
        }
    }

    private void CaptureBudgetSnapshot()
    {
        _savedBudgetFingerprint = BudgetFingerprint();
        HasPendingChanges = false;
    }

    private void RecalculateBudgetDirty()
    {
        if (_suppressBudgetDirty)
            return;
        HasPendingChanges = BudgetFingerprint() != _savedBudgetFingerprint;
    }

    private string BudgetFingerprint() =>
        string.Join("|",
            EditMonthlyOcpu, EditMonthlyGb, EditSoftOcpu, EditSoftGb,
            EditIdleTimeout, EditBudgetWarn, EditShapeOcpus, EditShapeMemory,
            EditIdleAgentEnabled);

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(EditMonthlyOcpu):
            case nameof(EditMonthlyGb):
            case nameof(EditSoftOcpu):
            case nameof(EditSoftGb):
            case nameof(EditIdleTimeout):
            case nameof(EditBudgetWarn):
            case nameof(EditShapeOcpus):
            case nameof(EditShapeMemory):
            case nameof(EditIdleAgentEnabled):
                RecalculateBudgetDirty();
                break;
            case nameof(HasPendingChanges):
            case nameof(IsBusy):
                OnPropertyChanged(nameof(CanPublish));
                break;
        }
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

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopWatching();
    }
}
