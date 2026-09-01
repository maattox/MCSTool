using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Usage tab: dashboard, budget edit/publish, ~2 min poll while the tab is alive.
/// Does not touch manage-chrome power-in-flight. Remaining-in-month is also on the
/// Hours left pin — not the rollover pin.
/// </summary>
public sealed partial class UsageViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(2);

    private ManagerLocalConfig? _config;
    private UsageBudgetStore? _store;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly IUiClock _clock;
    private readonly IUiDialogs _dialogs;
    private readonly ActionBanner _banner;
    private readonly MainViewModel? _main;
    private bool _resumeWatchingAfterReload;

    private UsageLedgerDocument _ledger = UsageLedgerDocument.Empty();
    private CancellationTokenSource? _pollCts;
    private bool _disposed;
    private bool _seededEdit;
    private string _savedBudgetFingerprint = "";
    private bool _suppressBudgetDirty;
    private string _lastParseError = "";
    private BudgetConfigDocument _workingBudget = new();
    private readonly HashSet<DateOnly> _selectedDays = [];
    private readonly Dictionary<string, double> _savedDailyOcpu = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _savedDailyOcpuPlanned = new(StringComparer.Ordinal);
    private string _savedEditMonthlyOcpu = "";
    private string _savedEditMonthlyGb = "";
    private string _savedEditSoftOcpu = "";
    private string _savedEditSoftGb = "";
    private string _savedEditIdleTimeout = "";
    private string _savedEditBudgetWarn = "";
    private string _savedEditShapeOcpus = "";
    private string _savedEditShapeMemory = "";
    private bool _savedEditIdleAgentEnabled = true;

    public IReadOnlyList<string> CalendarWeekdays { get; } =
        ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

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
    private string _unbudgetedHoursValue = "—";

    [ObservableProperty]
    private bool _unbudgetedHoursNegative;

    [ObservableProperty]
    private string _sculptRolloverHoursValue = "—";

    [ObservableProperty]
    private bool _todayOverDaily;

    [ObservableProperty]
    private string _leadCopy = AlwaysOnCapableCopy.UsageLead(false);

    [ObservableProperty]
    private string _remainingHoursLabel = AlwaysOnCapableCopy.RemainingHoursLabel(false);

    [ObservableProperty]
    private string _remainingHoursHint = AlwaysOnCapableCopy.RemainingHoursHint(false);

    [ObservableProperty]
    private string _softCapsHint = AlwaysOnCapableCopy.SoftCapsHint(false);

    [ObservableProperty]
    private string _idleWarningsHint = AlwaysOnCapableCopy.IdleWarningsHint(false);

    [ObservableProperty]
    private string _rolloverHelp = AlwaysOnCapableCopy.PinRolloverHelp(false);

    [ObservableProperty]
    private IReadOnlyList<UsageDayDisplayRow> _dayRows = [];

    [ObservableProperty]
    private IReadOnlyList<UsageCalendarCell> _calendarCells = [];

    [ObservableProperty]
    private string _editDayHours = "";

    [ObservableProperty]
    private bool _useRolloverHours = true;

    [ObservableProperty]
    private string _minRolloverBufferHours = "0";

    [ObservableProperty]
    private string _selectedDaySummary = "";

    [ObservableProperty]
    private string _closedDayCopy = "";

    [ObservableProperty]
    private string _shapeSculptNote =
        "Hours are wall-clock. Stored values are CPU-hours, so switching 2/12 ↔ 4/24 changes the hours shown, not the stored CPU-hours.";

    [ObservableProperty]
    private string _calendarClickHint =
        "Left-click adds or removes days. Right-click selects one day only.";

    [ObservableProperty]
    private string _envelopeWarning = "";

    [ObservableProperty]
    private bool _canSetSelected;

    [ObservableProperty]
    private bool _canZeroSelected;

    [ObservableProperty]
    private bool _canDistributeRemaining;

    [ObservableProperty]
    private bool _canDistributeSelected;

    [ObservableProperty]
    private bool _canDistribute;

    [ObservableProperty]
    private string _distributeHint = "";

    [ObservableProperty]
    private bool _hasRolloverToDistribute;

    [ObservableProperty]
    private string _minRolloverBufferHelp =
        "Hours of rollover to keep unassigned instead of putting them on later days. Idle overage can draw from this leftover. Save refuses a plan that would spend rollover below this buffer.";

    public bool HasObjectStorage => _store is not null;

    public bool CanPublish => HasPendingChanges && !IsBusy && HasObjectStorage;

    public string FormatDistributeAvailable(bool includeRollover) =>
        $"{DistributeAvailableWall(includeRollover):F1}h";

    public string DistributeAvailableInput(bool includeRollover) =>
        DistributeAvailableWall(includeRollover).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public bool HasDistributeHours(bool includeRollover) =>
        DistributeAvailableWall(includeRollover) > BudgetSculpt.Epsilon;

    private double DistributeAvailableWall(bool includeRollover) =>
        _unbudgetedDistributeWall + (includeRollover ? _rolloverDistributeWall : 0);

    private double _unbudgetedDistributeWall;
    private double _rolloverDistributeWall;

    public UsageViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiClock clock,
        IUiDialogs dialogs,
        ActionBanner banner,
        MainViewModel? main = null)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _clock = clock;
        _dialogs = dialogs;
        _banner = banner;
        _main = main;
        BindFromHost();
        _session.ClientsRebuilding += OnClientsRebuilding;
        _session.Reloaded += OnSessionReloaded;
    }

    private void OnClientsRebuilding(object? sender, EventArgs e)
    {
        _resumeWatchingAfterReload = _pollCts is not null;
        StopWatching();
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        BindFromHost();
        if (!_resumeWatchingAfterReload)
            return;
        _resumeWatchingAfterReload = false;
        StartWatching();
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _store = _cloud.UsageStore;
        _ledger = UsageLedgerDocument.Empty();
        _workingBudget = LocalBudget();
        _selectedDays.Clear();
        _seededEdit = false;
        SeedEditFromLocal();
        ApplyReportFromLocalFallback();
        OnPropertyChanged(nameof(HasObjectStorage));
        OnPropertyChanged(nameof(CanPublish));
        if (_store is null)
        {
            StatusMessage = _config is null
                ? "Local config isn't loaded. Showing default budget numbers only."
                : "Shared hours storage isn't available. Showing local budget numbers only.";
        }
        else
            StatusMessage = "Open this tab to refresh hours used.";
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
            ToastError(error);
            return;
        }

        if (!TryParseMinBuffer(out var bufferWall))
        {
            StatusMessage = _lastParseError;
            ToastError(_lastParseError);
            return;
        }

        var used = UsageMath.UsedOcpuGbByDay(_ledger, NowUtc());
        var env = BudgetSculpt.ComputeEnvelope(doc, used, NowUtc());
        var gate = BudgetSculpt.EvaluateSave(
            env, UseRolloverHours, bufferWall, BudgetSculpt.ShapeOcpus(doc));
        EnvelopeWarning = gate.Warning;
        if (!gate.CanSave)
        {
            StatusMessage = gate.Warning;
            ToastError(gate.Warning);
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Save usage budget?",
            AlwaysOnCapableCopy.PublishConfirmBody(AlwaysOnCapableCopy.ForShape(doc.ShapeOcpus)),
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
                var fail = result.Error ?? "Publish failed.";
                StatusMessage = fail;
                ToastError(fail);
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
                var remote = snap.Budget;
                remote.NormalizeSculptMaps();
                if (!HasPendingChanges)
                {
                    var snapshotChanged = BudgetSculpt.SnapshotClosedDays(remote, _clock.UtcNow.UtcDateTime);
                    _workingBudget = remote;
                    if (!_seededEdit || forceLedger)
                        ApplyBudgetToEdit(remote);
                    if (snapshotChanged)
                        await PersistWorkingBudgetQuietAsync();
                }

                budget = _workingBudget;
            }
            else
            {
                budget = LocalBudget();
                budget.NormalizeSculptMaps();
                if (!HasPendingChanges)
                {
                    _workingBudget = budget;
                    if (!_seededEdit)
                        ApplyBudgetToEdit(budget);
                }

                budget = _workingBudget;
            }

            var report = UsageMath.ComputeBudgetReport(_ledger, budget, _clock.UtcNow.UtcDateTime);

            ApplyReport(report, budget.ShapeOcpus, budget.IdleTimeoutMinutes);
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

    private void ApplyReport(BudgetReport report, double shapeOcpus, int idleTimeoutMinutes)
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
        var alwaysOn = AlwaysOnCapableCopy.ForShape(shape);
        LeadCopy = AlwaysOnCapableCopy.UsageLead(alwaysOn);
        RemainingHoursLabel = AlwaysOnCapableCopy.RemainingHoursLabel(alwaysOn);
        RemainingHoursHint = AlwaysOnCapableCopy.RemainingHoursHint(alwaysOn);
        SoftCapsHint = AlwaysOnCapableCopy.SoftCapsHint(alwaysOn);
        IdleWarningsHint = AlwaysOnCapableCopy.IdleWarningsHint(alwaysOn);
        var dailyHours = report.DailyOcpuAllowance / shape;
        var remainingHours = Math.Max(0, report.MonthlyOcpuTarget - report.MonthOcpu) / shape;
        var pinRolloverHours = report.LeftoverOcpu / shape;
        RemainingDisplay = alwaysOn
            ? $"{remainingHours:F1}h available this month (not rollover)"
            : $"{remainingHours:F1}h left this month (not rollover)";
        RemainingHoursValue = $"{remainingHours:F1}h";
        UsedHoursValue = $"{report.MonthUptime:F1}h";
        TodayHoursValue = $"{report.TodayUptimeHours:F1}h";
        TodayHoursHint = AlwaysOnCapableCopy.PinTodayHint(dailyHours, alwaysOn);
        TodayOverDaily = report.OcpuOverDaily;
        RolloverHoursValue = $"{(pinRolloverHours >= 0 ? "+" : "")}{pinRolloverHours:F1}h";
        RolloverHoursPositive = pinRolloverHours > 0.05;
        RolloverHelp = AlwaysOnCapableCopy.PinRolloverHelp(alwaysOn);
        var unbudgetedWall = BudgetSculpt.WallClockHours(report.UnbudgetedOcpu, shape);
        if (Math.Abs(unbudgetedWall) < 0.05)
            unbudgetedWall = 0;
        UnbudgetedHoursValue = $"{unbudgetedWall:F1}h";
        UnbudgetedHoursNegative = unbudgetedWall < 0;
        SculptRolloverHoursValue = $"{BudgetSculpt.WallClockHours(report.RolloverOcpu, shape):F1}h";
        DayRows = BuildDayRows(report);
        CalendarCells = BuildCalendarCells(report);
        RefreshSculptSelectionUi(report);
        CopyPins(PinnedUsageSnapshot.FromReport(report, shape, idleTimeoutMinutes));
    }

    private static IReadOnlyList<UsageDayDisplayRow> BuildDayRows(BudgetReport report)
    {
        if (report.Days.Count == 0)
            return [];

        var today = new DateOnly(report.Year, report.Month, report.DayOfMonth);
        var rows = new List<UsageDayDisplayRow>(report.Days.Count);
        foreach (var day in report.Days)
        {
            var isToday = day.Day == today;
            rows.Add(new UsageDayDisplayRow
            {
                DateLabel = isToday
                    ? "Today"
                    : day.Day.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture),
                BudgetValue = day.IsZeroed ? "Zeroed" : $"{day.BudgetWallClockHours:F1}h",
                UsedValue = $"{day.UptimeHours:F1}h",
                HoursValue = $"{day.UptimeHours:F1}h",
                IsToday = isToday,
                IsClosed = day.IsClosed,
                StillRunning = day.StillRunning,
            });
        }

        return rows;
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
        _main.PinRemainingLabel = snap.RemainingLabel;
        _main.PinRemainingValue = snap.RemainingValue;
        _main.PinRemainingHint = snap.RemainingHint;
        _main.PinRemainingFraction = snap.RemainingFraction;
        _main.PinIdleValue = snap.IdleValue;
        _main.PinIdleHint = snap.IdleHint;
        _main.PinTodayHelp = snap.TodayHelp;
        _main.PinMonthHelp = snap.MonthHelp;
        _main.PinAvgHelp = snap.AvgHelp;
        _main.PinRolloverHelp = snap.RolloverHelp;
        _main.PinRemainingHelp = snap.RemainingHelp;
        _main.PinIdleHelp = snap.IdleHelp;
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
        var report = UsageMath.ComputeBudgetReport(_ledger, budget, _clock.UtcNow.UtcDateTime);
        ApplyReport(report, budget.ShapeOcpus, budget.IdleTimeoutMinutes);
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
            budget.CopySculptMapsTo(_workingBudget);
            _workingBudget.MonthlyOcpuTarget = budget.MonthlyOcpuTarget;
            _workingBudget.MonthlyGbTarget = budget.MonthlyGbTarget;
            _workingBudget.SoftOcpuCap = budget.SoftOcpuCap;
            _workingBudget.SoftGbCap = budget.SoftGbCap;
            _workingBudget.IdleTimeoutMinutes = budget.IdleTimeoutMinutes;
            _workingBudget.BudgetWarnMinutes = budget.BudgetWarnMinutes;
            _workingBudget.ShapeOcpus = budget.ShapeOcpus;
            _workingBudget.ShapeMemoryGb = budget.ShapeMemoryGb;
            _workingBudget.IdleAgentEnabled = budget.IdleAgentEnabled;
            _workingBudget.Mode = budget.Mode;
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
        _workingBudget.NormalizeSculptMaps();
        CopyMap(_workingBudget.DailyOcpu, _savedDailyOcpu);
        CopyMap(_workingBudget.DailyOcpuPlanned, _savedDailyOcpuPlanned);
        _savedEditMonthlyOcpu = EditMonthlyOcpu;
        _savedEditMonthlyGb = EditMonthlyGb;
        _savedEditSoftOcpu = EditSoftOcpu;
        _savedEditSoftGb = EditSoftGb;
        _savedEditIdleTimeout = EditIdleTimeout;
        _savedEditBudgetWarn = EditBudgetWarn;
        _savedEditShapeOcpus = EditShapeOcpus;
        _savedEditShapeMemory = EditShapeMemory;
        _savedEditIdleAgentEnabled = EditIdleAgentEnabled;
        _savedBudgetFingerprint = BudgetFingerprint();
        HasPendingChanges = false;
    }

    private static void CopyMap(Dictionary<string, double> source, Dictionary<string, double> dest)
    {
        dest.Clear();
        foreach (var kv in source)
            dest[kv.Key] = kv.Value;
    }

    private void RecalculateBudgetDirty()
    {
        if (_suppressBudgetDirty)
            return;
        SyncWorkingCapsFromEdit();
        HasPendingChanges = BudgetFingerprint() != _savedBudgetFingerprint;
    }

    private string BudgetFingerprint() =>
        string.Join("|",
            EditMonthlyOcpu, EditMonthlyGb, EditSoftOcpu, EditSoftGb,
            EditIdleTimeout, EditBudgetWarn, EditShapeOcpus, EditShapeMemory,
            EditIdleAgentEnabled,
            _workingBudget.SculptFingerprint());

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
            case nameof(UseRolloverHours):
            case nameof(MinRolloverBufferHours):
                RefreshSculptSelectionUi(UsageMath.ComputeBudgetReport(_ledger, _workingBudget, NowUtc()));
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
        _workingBudget.CopySculptMapsTo(doc);
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

    private bool TryParseMinBuffer(out double bufferWall)
    {
        var text = MinRolloverBufferHours?.Trim() ?? "";
        if (text.Length == 0)
        {
            bufferWall = 0;
            return true;
        }

        if (!double.TryParse(text, out bufferWall) || bufferWall < 0)
        {
            _lastParseError = "Invalid minimum rollover buffer.";
            bufferWall = 0;
            return false;
        }

        return true;
    }

    public void ToggleCalendarDay(DateOnly day)
    {
        if (!_selectedDays.Add(day))
            _selectedDays.Remove(day);
        var report = UsageMath.ComputeBudgetReport(_ledger, _workingBudget, NowUtc());
        CalendarCells = BuildCalendarCells(report);
        RefreshSculptSelectionUi(report);
    }

    public void SelectCalendarDayExclusive(DateOnly day)
    {
        _selectedDays.Clear();
        _selectedDays.Add(day);
        var report = UsageMath.ComputeBudgetReport(_ledger, _workingBudget, NowUtc());
        CalendarCells = BuildCalendarCells(report);
        RefreshSculptSelectionUi(report);
    }

    public void SetSelectedDayHours()
    {
        if (!TryParseDouble(EditDayHours, "day hours", out var hours))
        {
            StatusMessage = _lastParseError;
            ToastError(_lastParseError);
            return;
        }

        ApplySculpt(used => BudgetSculpt.TrySetDays(
            _workingBudget, EditableSelected(), hours, used, NowUtc()));
    }

    public void ZeroSelectedDays() =>
        ApplySculpt(used => BudgetSculpt.TryZeroDays(_workingBudget, EditableSelected(), used, NowUtc()));

    public void DistributeHours(string hoursText, bool ontoSelected, bool includeRollover)
    {
        if (!TryParseDouble(hoursText, "hours", out var hours))
        {
            StatusMessage = _lastParseError;
            ToastError(_lastParseError);
            return;
        }

        ApplySculpt(used =>
        {
            if (hours < 0)
                return "Hours cannot be negative.";

            var env = CurrentEnvelope(used);
            var shape = BudgetSculpt.ShapeOcpus(_workingBudget);
            var availableOcpu = env.UnbudgetedPoolOcpu;
            if (includeRollover)
            {
                if (!TryParseMinBuffer(out var bufferWall))
                    return _lastParseError;
                availableOcpu += BudgetSculpt.AvailableRolloverOcpu(env.RolloverOcpu, bufferWall, shape);
            }

            var requested = BudgetSculpt.OcpuHoursFromWallClock(hours, shape);
            var pool = Math.Min(requested, availableOcpu);
            if (pool <= BudgetSculpt.Epsilon)
                return "No hours to distribute.";

            if (ontoSelected)
            {
                return BudgetSculpt.TryRedistributePoolOntoSelected(
                    _workingBudget, EditableSelected(), used, NowUtc(), pool);
            }

            return BudgetSculpt.TryRedistributePoolOntoUnspecified(
                _workingBudget, used, NowUtc(), pool);
        });
    }

    public void ResetChanges()
    {
        _suppressBudgetDirty = true;
        try
        {
            EditMonthlyOcpu = _savedEditMonthlyOcpu;
            EditMonthlyGb = _savedEditMonthlyGb;
            EditSoftOcpu = _savedEditSoftOcpu;
            EditSoftGb = _savedEditSoftGb;
            EditIdleTimeout = _savedEditIdleTimeout;
            EditBudgetWarn = _savedEditBudgetWarn;
            EditShapeOcpus = _savedEditShapeOcpus;
            EditShapeMemory = _savedEditShapeMemory;
            EditIdleAgentEnabled = _savedEditIdleAgentEnabled;
            _workingBudget.DailyOcpu = new Dictionary<string, double>(_savedDailyOcpu, StringComparer.Ordinal);
            _workingBudget.DailyOcpuPlanned = new Dictionary<string, double>(_savedDailyOcpuPlanned, StringComparer.Ordinal);
            _workingBudget.MonthlyOcpuTarget = ParseOrKeep(EditMonthlyOcpu, _workingBudget.MonthlyOcpuTarget);
            _workingBudget.MonthlyGbTarget = ParseOrKeep(EditMonthlyGb, _workingBudget.MonthlyGbTarget);
            _workingBudget.SoftOcpuCap = ParseOrKeep(EditSoftOcpu, _workingBudget.SoftOcpuCap);
            _workingBudget.SoftGbCap = ParseOrKeep(EditSoftGb, _workingBudget.SoftGbCap);
            _workingBudget.ShapeOcpus = ParseOrKeep(EditShapeOcpus, _workingBudget.ShapeOcpus);
            _workingBudget.ShapeMemoryGb = ParseOrKeep(EditShapeMemory, _workingBudget.ShapeMemoryGb);
            if (int.TryParse(_savedEditIdleTimeout.Trim(), out var idle))
                _workingBudget.IdleTimeoutMinutes = idle;
            if (int.TryParse(_savedEditBudgetWarn.Trim(), out var warn))
                _workingBudget.BudgetWarnMinutes = warn;
            _workingBudget.IdleAgentEnabled = _savedEditIdleAgentEnabled;
        }
        finally
        {
            _suppressBudgetDirty = false;
        }

        RecalculateBudgetDirty();
        ClearDaySelection();
        RebuildFromWorking("Restored last saved hours plan.");
    }

    public void ResetToDefaults()
    {
        SyncWorkingCapsFromEdit();
        BudgetSculpt.ResetTodayAndFutureToDefault(_workingBudget, NowUtc());
        RecalculateBudgetDirty();
        ClearDaySelection();
        RebuildFromWorking("Today and future days reset to the even-split default — Save changes to publish.");
    }

    private static double ParseOrKeep(string text, double fallback) =>
        double.TryParse(text.Trim(), out var value) && value >= 0 ? value : fallback;

    private BudgetSculpt.Envelope CurrentEnvelope(Dictionary<DateOnly, (double Ocpu, double Gb)> used)
    {
        SyncWorkingCapsFromEdit();
        return BudgetSculpt.ComputeEnvelope(_workingBudget, used, NowUtc());
    }

    private void ApplySculpt(Func<Dictionary<DateOnly, (double Ocpu, double Gb)>, string?> mutate)
    {
        SyncWorkingCapsFromEdit();
        var used = UsageMath.UsedOcpuGbByDay(_ledger, NowUtc());
        var error = mutate(used);
        if (error is not null)
        {
            StatusMessage = error;
            ToastError(error);
            return;
        }

        RecalculateBudgetDirty();
        ClearDaySelection();
        RebuildFromWorking("Hours plan updated — Save changes to publish.");
    }

    private void ClearDaySelection() => _selectedDays.Clear();

    private void ToastError(string message) =>
        _banner.Show(message, ActionBannerSeverity.Error);

    private async Task PersistWorkingBudgetQuietAsync()
    {
        if (_store is null)
            return;
        var result = await _store.PublishBudgetAsync(_workingBudget);
        if (result.Succeeded && result.Value is not null)
        {
            _workingBudget = result.Value.Budget;
            _workingBudget.NormalizeSculptMaps();
            CaptureBudgetSnapshot();
        }
    }

    private void RebuildFromWorking(string status)
    {
        var report = UsageMath.ComputeBudgetReport(_ledger, _workingBudget, NowUtc());
        ApplyReport(report, _workingBudget.ShapeOcpus, _workingBudget.IdleTimeoutMinutes);
        StatusMessage = status;
    }

    private DateTime NowUtc() => _clock.UtcNow.UtcDateTime;

    private List<DateOnly> EditableSelected() =>
        _selectedDays.Where(d => BudgetSculpt.IsEditable(d, NowUtc())).OrderBy(d => d).ToList();

    private IReadOnlyList<UsageCalendarCell> BuildCalendarCells(BudgetReport report)
    {
        var cells = new List<UsageCalendarCell>();
        if (report.CalendarDays.Count == 0)
            return cells;

        var first = new DateOnly(report.Year, report.Month, 1);
        var pad = (int)first.DayOfWeek;
        for (var i = 0; i < pad; i++)
            cells.Add(new UsageCalendarCell { IsPad = true });

        foreach (var day in report.CalendarDays)
        {
            var selected = _selectedDays.Contains(day.Day);
            var label = day.IsZeroed
                ? "0h"
                : day.IsFuture || day.BudgetWallClockHours > 0
                    ? $"{day.BudgetWallClockHours:F0}h"
                    : "—";
            cells.Add(new UsageCalendarCell
            {
                Day = day.Day,
                DayNum = day.Day.Day.ToString(System.Globalization.CultureInfo.InvariantCulture),
                HoursLabel = label,
                IsToday = !day.IsClosed && !day.IsFuture,
                IsClosed = day.IsClosed,
                IsZeroed = day.IsZeroed,
                IsSelected = selected,
                IsSculpted = day.IsSculpted,
                IsFuture = day.IsFuture,
                Heat = Math.Clamp(day.BudgetWallClockHours / 24.0, 0, 1),
                Title = day.IsClosed
                    ? $"{day.Day:yyyy-MM-dd} UTC — Budget {day.BudgetWallClockHours:F1}h; Used {day.UptimeHours:F1}h"
                    : day.IsZeroed
                        ? $"{day.Day:yyyy-MM-dd} UTC — zeroed (doorbell will not wake)"
                        : $"{day.Day:yyyy-MM-dd} UTC — {day.BudgetWallClockHours:F1}h planned",
            });
        }

        return cells;
    }

    private void RefreshSculptSelectionUi(BudgetReport report)
    {
        var editable = EditableSelected();
        CanSetSelected = editable.Count > 0;
        CanZeroSelected = editable.Count > 0;

        var unspecified = BudgetSculpt.EditableDays(NowUtc())
            .Count(d => !BudgetSculpt.TryGetExplicit(_workingBudget, d, out _));
        var unbudgeted = Math.Max(0, report.UnbudgetedOcpu);
        var bufferWall = 0.0;
        var bufferOk = TryParseMinBuffer(out bufferWall);
        var shape = BudgetSculpt.ShapeOcpus(_workingBudget);
        var availableRollover = bufferOk
            ? BudgetSculpt.AvailableRolloverOcpu(report.RolloverOcpu, bufferWall, shape)
            : 0;
        _unbudgetedDistributeWall = BudgetSculpt.WallClockHours(unbudgeted, shape);
        _rolloverDistributeWall = BudgetSculpt.WallClockHours(availableRollover, shape);
        HasRolloverToDistribute = availableRollover > BudgetSculpt.Epsilon;
        var anyPool = unbudgeted > BudgetSculpt.Epsilon || availableRollover > BudgetSculpt.Epsilon;
        CanDistributeRemaining = anyPool && unspecified > 0;
        CanDistributeSelected = anyPool && editable.Count > 0;
        CanDistribute = CanDistributeRemaining || CanDistributeSelected;
        DistributeHint = !anyPool
            ? "No unbudgeted or rollover hours to distribute."
            : unspecified == 0 && editable.Count == 0
                ? "Every remaining UTC day already has an hours value. Select days to distribute onto."
                : "Spread available hours across remaining days, or only the days you selected.";

        var used = UsageMath.UsedOcpuGbByDay(_ledger, NowUtc());
        var env = BudgetSculpt.ComputeEnvelope(_workingBudget, used, NowUtc());
        var gate = BudgetSculpt.EvaluateSave(
            env, UseRolloverHours, bufferOk ? bufferWall : 0, shape);
        EnvelopeWarning = gate.Warning;

        if (_selectedDays.Count == 0)
        {
            SelectedDaySummary = "";
            ClosedDayCopy = "";
            return;
        }

        var closed = _selectedDays.Where(d => BudgetSculpt.IsClosed(d, NowUtc())).OrderBy(d => d).ToList();
        var parts = new List<string>();
        if (editable.Count > 0)
        {
            parts.Add(editable.Count == 1
                ? $"{editable[0]:yyyy-MM-dd} UTC"
                : $"{editable.Count} days selected");
            if (editable.Count == 1)
            {
                var row = report.CalendarDays.FirstOrDefault(r => r.Day == editable[0]);
                if (row is not null)
                    EditDayHours = row.BudgetWallClockHours.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        else
            parts.Add($"{_selectedDays.Count} closed day(s) selected");

        if (closed.Count > 0)
        {
            var bits = new List<string>();
            foreach (var day in closed.Take(3))
            {
                var row = report.CalendarDays.FirstOrDefault(r => r.Day == day);
                if (row is null)
                    continue;
                bits.Add($"Budget: {row.BudgetWallClockHours:F1}h; Used: {row.UptimeHours:F1}h");
            }
            ClosedDayCopy = string.Join(" · ", bits);
            if (!string.IsNullOrWhiteSpace(ClosedDayCopy))
                parts.Add(ClosedDayCopy);
        }
        else
            ClosedDayCopy = "";

        SelectedDaySummary = string.Join(" · ", parts);
    }

    private void SyncWorkingCapsFromEdit()
    {
        if (TryParseDouble(EditMonthlyOcpu, "Monthly OCPU target", out var monthlyOcpu))
            _workingBudget.MonthlyOcpuTarget = monthlyOcpu;
        if (TryParseDouble(EditMonthlyGb, "Monthly GB target", out var monthlyGb))
            _workingBudget.MonthlyGbTarget = monthlyGb;
        if (TryParseDouble(EditSoftOcpu, "Soft OCPU cap", out var softOcpu))
            _workingBudget.SoftOcpuCap = softOcpu;
        if (TryParseDouble(EditSoftGb, "Soft GB cap", out var softGb))
            _workingBudget.SoftGbCap = softGb;
        if (TryParseDouble(EditShapeOcpus, "Shape OCPUs", out var shapeOcpus))
            _workingBudget.ShapeOcpus = shapeOcpus;
        if (TryParseDouble(EditShapeMemory, "Shape memory GB", out var shapeMemory))
            _workingBudget.ShapeMemoryGb = shapeMemory;
        if (TryParseInt(EditIdleTimeout, "Idle timeout minutes", out var idleTimeout))
            _workingBudget.IdleTimeoutMinutes = idleTimeout;
        if (TryParseInt(EditBudgetWarn, "Budget warn minutes", out var warnMinutes))
            _workingBudget.BudgetWarnMinutes = warnMinutes;
        _workingBudget.IdleAgentEnabled = EditIdleAgentEnabled;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session.ClientsRebuilding -= OnClientsRebuilding;
        _session.Reloaded -= OnSessionReloaded;
        StopWatching();
    }
}
