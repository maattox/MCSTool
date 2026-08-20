using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Danger Zone VM1 A1 Flex resize. Apply is disabled unless VM1 is STOPPED.
/// Does not rewrite ledger intervals.
/// </summary>
public sealed partial class Vm1ShapeScaleViewModel : ObservableObject
{
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private readonly IUiDialogs _dialogs;

    private ManagerLocalConfig? _config;
    private ComputeService? _compute;
    private UsageBudgetStore? _budgetStore;
    private InfraMetaStore? _infraStore;
    private BudgetConfigDocument? _lastBudget;
    private double _monthOcpuUsed;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Server size is an Always Free setting. Apply only while the server is Stopped.";

    [ObservableProperty]
    private string _vm1Lifecycle = "—";

    [ObservableProperty]
    private double _currentOcpus = Vm1ShapeChoice.DefaultOcpus;

    [ObservableProperty]
    private double _currentMemoryGb = Vm1ShapeChoice.DefaultMemoryGb;

    [ObservableProperty]
    private bool _targetIsDefault = true;

    [ObservableProperty]
    private double _monthlyOcpuTarget = 1400;

    public string CurrentSizeDisplay => Vm1ShapeScaleUx.FormatExact(CurrentOcpus, CurrentMemoryGb);

    public int TargetOcpus => TargetIsDefault
        ? Vm1ShapeChoice.DefaultOcpus
        : Vm1ShapeChoice.SmallerOcpus;

    public int TargetMemoryGb => TargetIsDefault
        ? Vm1ShapeChoice.DefaultMemoryGb
        : Vm1ShapeChoice.SmallerMemoryGb;

    public string PreviewText =>
        Vm1ShapeScaleUx.PreviewBody(
            CurrentOcpus,
            CurrentMemoryGb,
            TargetOcpus,
            TargetMemoryGb,
            MonthlyOcpuTarget,
            _monthOcpuUsed);

    public string BlockedReason =>
        Vm1ShapeScaleUx.ApplyBlockedReason(
            Vm1Lifecycle,
            CurrentOcpus,
            CurrentMemoryGb,
            TargetOcpus,
            TargetMemoryGb);

    public bool CanApply =>
        !IsBusy
        && _compute is not null
        && _config is not null
        && Vm1ShapeScaleUx.CanApply(
            Vm1Lifecycle,
            CurrentOcpus,
            CurrentMemoryGb,
            TargetOcpus,
            TargetMemoryGb);

    public Vm1ShapeScaleViewModel(
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
        Vm1Lifecycle = string.IsNullOrWhiteSpace(_main.Vm1Lifecycle) ? "—" : _main.Vm1Lifecycle;
        NotifyDerived();
        _main.PropertyChanged += OnMainChanged;
        _session.Reloaded += OnSessionReloaded;
    }

    public void SelectDefaultShape() => TargetIsDefault = true;

    public void SelectSmallerShape() => TargetIsDefault = false;

    public async Task RefreshAsync()
    {
        if (IsBusy)
            return;

        BindFromHost();
        if (_compute is null || _config is null)
        {
            StatusMessage = "OCI session unavailable — cannot read VM1 size.";
            SeedFromLocal();
            NotifyDerived();
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading VM1 size from Oracle…";
        try
        {
            var shape = await _compute.GetInstanceShapeAsync(_config.Vm1.InstanceId);
            if (!shape.Succeeded || shape.Value is null)
            {
                StatusMessage = shape.Error ?? "GetInstance failed.";
                SeedFromLocal();
                return;
            }

            CurrentOcpus = shape.Value.Ocpus > 0
                ? shape.Value.Ocpus
                : (_config.Vm1.ShapeOcpus > 0 ? _config.Vm1.ShapeOcpus : Vm1ShapeChoice.DefaultOcpus);
            CurrentMemoryGb = shape.Value.MemoryGb > 0
                ? shape.Value.MemoryGb
                : (_config.Vm1.ShapeMemoryGb > 0 ? _config.Vm1.ShapeMemoryGb : Vm1ShapeChoice.DefaultMemoryGb);
            if (!string.IsNullOrWhiteSpace(shape.Value.LifecycleState))
                Vm1Lifecycle = shape.Value.LifecycleState;

            SeedTargetFromCurrent();
            await PullBudgetAsync();
            StatusMessage =
                $"Current size {CurrentSizeDisplay} (VM1 {Vm1Lifecycle}). "
                + "Apply is disabled unless the server is Stopped.";
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    public async Task ApplyAsync()
    {
        if (IsBusy)
            return;

        BindFromHost();
        if (_compute is null || _config is null)
        {
            StatusMessage = "OCI session unavailable — cannot resize VM1.";
            return;
        }

        if (!CanApply)
        {
            StatusMessage = string.IsNullOrWhiteSpace(BlockedReason)
                ? "Cannot apply size change."
                : BlockedReason;
            return;
        }

        var targetO = TargetOcpus;
        var targetM = TargetMemoryGb;
        var confirmed = await _dialogs.ConfirmAsync(
            "Danger Zone — change server size?",
            Vm1ShapeScaleUx.ConfirmMessage(
                CurrentOcpus,
                CurrentMemoryGb,
                targetO,
                targetM,
                MonthlyOcpuTarget,
                _monthOcpuUsed),
            confirmButtonText: "Apply size change");
        if (!confirmed)
        {
            StatusMessage = "Size change cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Updating VM1 to {Vm1ShapeScaleUx.FormatExact(targetO, targetM)}…";
        NotifyDerived();
        try
        {
            var life = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId);
            var lifeValue = life.Succeeded ? life.Value : Vm1Lifecycle;
            if (!Vm1ShapeScaleUx.IsVm1Stopped(lifeValue))
            {
                Vm1Lifecycle = lifeValue ?? Vm1Lifecycle;
                StatusMessage = Vm1ShapeScaleUx.ApplyBlockedReason(
                    Vm1Lifecycle, CurrentOcpus, CurrentMemoryGb, targetO, targetM);
                return;
            }

            var update = await _compute.UpdateInstanceShapeConfigAsync(
                _config.Vm1.InstanceId,
                targetO,
                targetM);
            if (!update.Succeeded)
            {
                StatusMessage = update.Error ?? "UpdateInstance failed.";
                return;
            }

            StatusMessage = "Waiting for Oracle to finish the size change…";
            var wait = await _compute.WaitForShapeConfigAsync(
                _config.Vm1.InstanceId,
                targetO,
                targetM);
            if (!wait.Succeeded || wait.Value is null)
            {
                StatusMessage = wait.Error
                    ?? "Timed out waiting for the new size. Shared config was not updated.";
                return;
            }

            CurrentOcpus = wait.Value.Ocpus;
            CurrentMemoryGb = wait.Value.MemoryGb;
            if (!string.IsNullOrWhiteSpace(wait.Value.LifecycleState))
                Vm1Lifecycle = wait.Value.LifecycleState;

            _config.Vm1.ShapeOcpus = targetO;
            _config.Vm1.ShapeMemoryGb = targetM;
            var saved = LocalConfigStore.SaveConfig(_config);
            if (!saved.Succeeded)
            {
                StatusMessage =
                    $"Oracle size is now {CurrentSizeDisplay}, but saving config.local.json failed: "
                    + (saved.Error ?? "unknown")
                    + ". Shared budget/meta were not updated.";
                return;
            }

            var published = await PublishSharedShapeAsync(targetO, targetM);
            _session.ReloadFromDisk();
            SeedTargetFromCurrent();
            StatusMessage = published;
        }
        finally
        {
            IsBusy = false;
            NotifyDerived();
        }
    }

    private async Task<string> PublishSharedShapeAsync(int ocpus, int memoryGb)
    {
        var notes = new List<string>
        {
            $"Oracle size is {Vm1ShapeScaleUx.FormatExact(ocpus, memoryGb)}.",
            "config.local.json updated.",
        };

        if (_budgetStore is not null)
        {
            var doc = _lastBudget ?? BudgetConfigDocument.FromLocal(_config!.Budget, _config.Vm1);
            doc.ShapeOcpus = ocpus;
            doc.ShapeMemoryGb = memoryGb;
            var published = await _budgetStore.PublishBudgetAsync(doc);
            if (published.Succeeded && published.Value is not null)
            {
                _lastBudget = published.Value.Budget;
                notes.Add("budget/config.json updated (ledger intervals unchanged).");
            }
            else
            {
                notes.Add("budget/config.json publish failed: " + (published.Error ?? "unknown"));
            }
        }
        else
        {
            notes.Add("Object Storage unavailable — budget/config.json not published.");
        }

        if (_infraStore is not null && _config is not null)
        {
            var meta = await _infraStore.PublishFromLocalAsync(_config);
            notes.Add(meta.Succeeded
                ? "meta/infra.json updated."
                : "meta/infra.json publish failed: " + (meta.Error ?? "unknown"));
        }
        else
        {
            notes.Add("Object Storage unavailable — meta/infra.json not published.");
        }

        notes.Add("On the next VM1 boot, the idle agent re-detects live size.");
        return string.Join(" ", notes);
    }

    private async Task PullBudgetAsync()
    {
        _monthOcpuUsed = 0;
        if (_config is not null && _config.Budget.MonthlyOcpuTarget > 0)
            MonthlyOcpuTarget = _config.Budget.MonthlyOcpuTarget;

        if (_budgetStore is null)
            return;

        var pull = await _budgetStore.PullAsync(forceLedger: true);
        if (!pull.Succeeded || pull.Value is null)
            return;

        if (pull.Value.Budget is { } budget)
        {
            _lastBudget = budget;
            if (budget.MonthlyOcpuTarget > 0)
                MonthlyOcpuTarget = budget.MonthlyOcpuTarget;
        }

        var report = UsageMath.ComputeBudgetReport(
            pull.Value.Ledger,
            MonthlyOcpuTarget,
            _lastBudget?.MonthlyGbTarget ?? _config?.Budget.MonthlyGbTarget ?? 8800,
            _lastBudget?.SoftOcpuCap ?? _config?.Budget.SoftOcpuCap ?? 1375,
            _lastBudget?.SoftGbCap ?? _config?.Budget.SoftGbCap ?? 8600);
        _monthOcpuUsed = report.MonthOcpu;
    }

    private void SeedFromLocal()
    {
        if (_config is null)
        {
            CurrentOcpus = Vm1ShapeChoice.DefaultOcpus;
            CurrentMemoryGb = Vm1ShapeChoice.DefaultMemoryGb;
            SeedTargetFromCurrent();
            return;
        }

        CurrentOcpus = _config.Vm1.ShapeOcpus > 0
            ? _config.Vm1.ShapeOcpus
            : Vm1ShapeChoice.DefaultOcpus;
        CurrentMemoryGb = _config.Vm1.ShapeMemoryGb > 0
            ? _config.Vm1.ShapeMemoryGb
            : Vm1ShapeChoice.DefaultMemoryGb;
        if (_config.Budget.MonthlyOcpuTarget > 0)
            MonthlyOcpuTarget = _config.Budget.MonthlyOcpuTarget;
        SeedTargetFromCurrent();
    }

    private void SeedTargetFromCurrent()
    {
        var ints = Vm1ShapeScaleUx.ToInts(CurrentOcpus, CurrentMemoryGb);
        TargetIsDefault = !Vm1ShapeChoice.IsAllowed(ints.Ocpus, ints.MemoryGb)
            || Vm1ShapeChoice.IsDefault(ints.Ocpus, ints.MemoryGb);
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _compute = _cloud.Compute;
        _budgetStore = _cloud.UsageStore;
        _infraStore = null;
        if (_config is not null && _cloud.Session is not null)
        {
            var os = new ObjectStorageService(_cloud.Session, _config.ObjectStorage);
            _infraStore = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
        }
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        BindFromHost();
        SeedFromLocal();
        NotifyDerived();
    }

    private void OnMainChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle) or null)
        {
            if (!string.IsNullOrWhiteSpace(_main.Vm1Lifecycle))
                Vm1Lifecycle = _main.Vm1Lifecycle;
            NotifyDerived();
        }
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(CurrentSizeDisplay));
        OnPropertyChanged(nameof(TargetOcpus));
        OnPropertyChanged(nameof(TargetMemoryGb));
        OnPropertyChanged(nameof(PreviewText));
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(CanApply));
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName is nameof(IsBusy)
            or nameof(TargetIsDefault)
            or nameof(CurrentOcpus)
            or nameof(CurrentMemoryGb)
            or nameof(Vm1Lifecycle)
            or nameof(MonthlyOcpuTarget))
        {
            if (e.PropertyName is not nameof(CanApply)
                and not nameof(PreviewText)
                and not nameof(BlockedReason)
                and not nameof(CurrentSizeDisplay)
                and not nameof(TargetOcpus)
                and not nameof(TargetMemoryGb))
            {
                NotifyDerived();
            }
        }
    }
}
