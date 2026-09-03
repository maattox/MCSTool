using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Server → Settings change-type modal. No tofu. Vanilla/Paper reinstall via driver.sh;
/// Modded reuses pack analyze + the same prepare path.
/// </summary>
public sealed partial class ChangeServerTypeViewModel : ObservableObject
{
    private static readonly FileTypeFilter PackFilter = new("Modpack archives", ".mrpack", ".zip");
    private static readonly FileTypeFilter MrpackFilter = new("Modrinth pack", ".mrpack");
    private static readonly FileTypeFilter ZipFilter = new("ZIP files", ".zip");
    private static readonly FileTypeFilter AllFilesFilter = new("All files", ".*");

    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private readonly IUiDialogs _dialogs;
    private readonly IFilePicker _filePicker;
    private readonly ActionBanner _banner;
    private readonly SetupBootstrapService _bootstrap;
    private readonly PackIdentityCatalogCache _catalogs;
    private readonly PaperFillV3Client _paperCatalog = new();
    private readonly List<string> _versionIds = [];

    private ManagerLocalConfig? _config;
    private InfraMetaStore? _infra;
    private ISshService _ssh = null!;
    private string? _currentKind;
    private string? _currentMinecraftVersion;
    private string _dataDirectory = "";
    private SetupPackPreview? _packPreview;
    private MojangVersionManifest? _manifest;
    private PaperFillProject? _paperProject;
    private string _mojangCatalogNotes = "";
    private string _paperCatalogNotes = "";

    [ObservableProperty]
    private bool _modalOpen;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isAnalyzingPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsModdedTarget))]
    [NotifyPropertyChangedFor(nameof(IsVanillaTarget))]
    [NotifyPropertyChangedFor(nameof(ShowSnapshotToggle))]
    [NotifyPropertyChangedFor(nameof(ShowVersionDropdown))]
    [NotifyPropertyChangedFor(nameof(ShowPackDrop))]
    [NotifyPropertyChangedFor(nameof(DirectionWarning))]
    private string _targetChoice = ChangeServerTypeUx.ChoiceDefaultVanilla;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitDisabledReason))]
    private string _minecraftVersion = "";

    [ObservableProperty]
    private bool _includeSnapshots;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveCompatibilityWarning))]
    [NotifyPropertyChangedFor(nameof(ConfirmHint))]
    private bool _wipeWorld;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveCompatibilityWarning))]
    private string _saveCompatibilityWarning = "";

    [ObservableProperty]
    private string _statusMessage = "";

    [ObservableProperty]
    private string _versionCatalogNotes = "";

    [ObservableProperty]
    private string _packPath = "";

    [ObservableProperty]
    private string _packSummary = "";

    [ObservableProperty]
    private string _packBlockReason = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitDisabledReason))]
    private bool _packCanContinue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitDisabledReason))]
    private bool _packConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSubmit))]
    [NotifyPropertyChangedFor(nameof(SubmitDisabledReason))]
    private bool _clientPackAcknowledged;

    public event EventHandler? Completed;

    public IReadOnlyList<string> VersionIds => _versionIds;

    public bool IsModdedTarget => ChangeServerTypeUx.IsModdedChoice(TargetChoice);

    public bool IsVanillaTarget => !IsModdedTarget;

    public bool IsPaperTarget => ChangeServerTypeUx.IsPaperChoice(TargetChoice);

    public bool ShowSnapshotToggle =>
        string.Equals(ChangeServerTypeUx.NormalizeChoice(TargetChoice), ChangeServerTypeUx.ChoiceDefaultVanilla, StringComparison.Ordinal);

    public bool ShowVersionDropdown => IsVanillaTarget;

    public bool ShowPackDrop => IsModdedTarget;

    public string CurrentKindLabel => ChangeServerTypeUx.KindLabel(_currentKind);

    public string CurrentVersionLabel =>
        string.IsNullOrWhiteSpace(_currentMinecraftVersion) ? "—" : _currentMinecraftVersion;

    public string DirectionWarning =>
        ChangeServerTypeUx.DirectionWarning(_currentKind, TargetChoice) ?? "";

    public bool ShowDirectionWarning => !string.IsNullOrWhiteSpace(DirectionWarning);

    public bool ShowSaveCompatibilityWarning => !string.IsNullOrWhiteSpace(SaveCompatibilityWarning);

    public string ConfirmHint => ChangeServerTypeUx.ConfirmBody(WipeWorld);

    public bool PackNeedsReview =>
        IsModdedTarget
        && _packPreview is not null
        && _packPreview.CanContinue
        && (_packPreview.NeedsAssistedReview
            || _packPreview.NeedsIdentityConfirm
            || !PackReplaceUx.FreezeAllowsContinue(_packPreview.FreezeBlockReason));

    public bool ShowReviewOnMods => PackNeedsReview;

    public bool ShowPackChecks =>
        IsModdedTarget && PackCanContinue && !PackNeedsReview && !string.IsNullOrWhiteSpace(PackSummary);

    public string WipeWorldLabel => ChangeServerTypeUx.WipeWorldLabel;

    public string PackConfirmLabel => PackReplaceUx.PackConfirmLabel;

    public string ClientPackAckLabel => PackReplaceUx.ClientPackAckLabel;

    public string DefaultVanillaHelp => SetupWizardViewModel.DefaultVanillaHelp;

    public string PaperHelp => SetupWizardViewModel.OptimizedVanillaHelp;

    public string ModdedHelp => SetupWizardViewModel.ModdedHelp;

    public bool CanSubmit
    {
        get
        {
            if (IsBusy || IsAnalyzingPack || _config is null)
                return false;
            if (IsModdedTarget)
            {
                return PackCanContinue
                    && PackConfirmed
                    && ClientPackAcknowledged
                    && !PackNeedsReview
                    && !string.IsNullOrWhiteSpace(PackPath);
            }

            return !string.IsNullOrWhiteSpace(MinecraftVersion);
        }
    }

    public string SubmitDisabledReason
    {
        get
        {
            if (IsBusy || IsAnalyzingPack)
                return "Wait until the current action finishes.";
            if (_config is null)
                return "Local config is missing.";
            if (IsModdedTarget)
            {
                if (string.IsNullOrWhiteSpace(PackPath) || !PackCanContinue)
                    return string.IsNullOrWhiteSpace(PackBlockReason)
                        ? ChangeServerTypeUx.MissingPackError
                        : PackBlockReason;
                if (PackNeedsReview)
                    return ChangeServerTypeUx.PackNeedsReview;
                if (!PackConfirmed || !ClientPackAcknowledged)
                    return "Confirm the pack and that players will get the same file.";
                return "";
            }

            if (string.IsNullOrWhiteSpace(MinecraftVersion))
                return ChangeServerTypeUx.MissingVersionError;
            return "";
        }
    }

    public ChangeServerTypeViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        MainViewModel main,
        IUiDialogs dialogs,
        IFilePicker filePicker,
        ActionBanner banner,
        SetupBootstrapService bootstrap,
        PackIdentityCatalogCache catalogs)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _main = main;
        _dialogs = dialogs;
        _filePicker = filePicker;
        _banner = banner;
        _bootstrap = bootstrap;
        _catalogs = catalogs;

        BindFromHost();
        _session.Reloaded += (_, _) => BindFromHost();
    }

    public async Task OpenAsync(string? currentKind, string? currentMinecraftVersion)
    {
        _currentKind = string.IsNullOrWhiteSpace(currentKind) ? null : currentKind.Trim();
        _currentMinecraftVersion = string.IsNullOrWhiteSpace(currentMinecraftVersion)
            || currentMinecraftVersion == "—"
            ? null
            : currentMinecraftVersion.Trim();
        TargetChoice = ChangeServerTypeUx.ChoiceFromServerKind(_currentKind);
        MinecraftVersion = _currentMinecraftVersion ?? "";
        WipeWorld = false;
        ClearPack();
        StatusMessage = "";
        ModalOpen = true;
        OnPropertyChanged(nameof(CurrentKindLabel));
        OnPropertyChanged(nameof(CurrentVersionLabel));
        NotifyWarnings();
        await LoadVersionsAsync().ConfigureAwait(true);
    }

    public void CloseModal()
    {
        if (IsBusy)
            return;
        ModalOpen = false;
    }

    public async Task PickPackAsync()
    {
        if (IsBusy || IsAnalyzingPack)
            return;
        var path = await _filePicker.OpenFileAsync(new FilePickRequest
        {
            Title = "Choose a mod pack (.mrpack or .zip)",
            Filters = [PackFilter, MrpackFilter, ZipFilter, AllFilesFilter],
        });
        if (string.IsNullOrWhiteSpace(path))
            return;
        await AnalyzePackPathAsync(path);
    }

    public async Task ImportDroppedPackAsync(string fileName, Stream content)
    {
        if (IsBusy || IsAnalyzingPack || content is null)
            return;
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "dropped-pack.zip";
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-change-type-drop");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, safeName);
        try
        {
            await using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(fs);
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not save the dropped pack: " + ex.Message;
            return;
        }

        await AnalyzePackPathAsync(dest);
    }

    public async Task ApplyAsync()
    {
        if (!CanSubmit)
        {
            StatusMessage = SubmitDisabledReason;
            return;
        }

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            ChangeServerTypeUx.ConfirmTitle,
            ChangeServerTypeUx.ConfirmBody(WipeWorld),
            ChangeServerTypeUx.ConfirmButton);
        if (!confirmed)
        {
            StatusMessage = "Change type cancelled.";
            return;
        }

        IsBusy = true;
        try
        {
            if (!ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle))
            {
                StatusMessage = "Starting the game VM…";
                var started = await _main.EnsureVm1RunningForPackReplaceAsync();
                if (!started)
                {
                    StatusMessage = string.IsNullOrWhiteSpace(_main.ActionFeedback)
                        ? "Start failed. Server type was not changed."
                        : _main.ActionFeedback;
                    _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                    return;
                }
            }

            StatusMessage = "Disabling the idle timer…";
            var idle = await _ssh.ApplyIdleSettingsAsync(
                _config.Vm1,
                idleAgentEnabled: false,
                _config.Budget.IdleTimeoutMinutes,
                _config.Budget.BudgetWarnMinutes);
            if (!idle.Succeeded)
            {
                StatusMessage = idle.Error ?? "Could not disable the idle timer. Server type was not changed.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            StatusMessage = "Reinstalling Minecraft…";
            var progress = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                StatusMessage = ProgressDockUx.HumanizeOrFallback(line, "Reinstalling Minecraft…");
            });
            var result = await _bootstrap.ChangeServerTypeAsync(
                _config.Vm1,
                new ChangeServerTypeRequest(
                    TargetChoice,
                    MinecraftVersion,
                    WipeWorld,
                    string.IsNullOrWhiteSpace(PackPath) ? null : PackPath,
                    _dataDirectory),
                progress);
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "Change server type failed.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            if (_infra is not null)
            {
                var meta = await _infra.PublishFromLocalAsync(
                    _config,
                    serverKind: result.Value.ServerKind,
                    minecraftVersion: result.Value.MinecraftVersion);
                if (!meta.Succeeded)
                {
                    StatusMessage = ChangeServerTypeUx.SuccessMessage(result.Value)
                        + " Shared game info did not update: "
                        + (meta.Error ?? "publish failed.");
                    _banner.Show(StatusMessage, ActionBannerSeverity.Warning);
                    ModalOpen = false;
                    Completed?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            StatusMessage = ChangeServerTypeUx.SuccessMessage(result.Value);
            _banner.Show(
                string.IsNullOrWhiteSpace(result.Value.QuarantineNotice)
                    ? StatusMessage
                    : result.Value.QuarantineNotice,
                string.IsNullOrWhiteSpace(result.Value.QuarantineNotice)
                    ? ActionBannerSeverity.Success
                    : ActionBannerSeverity.Warning);
            ModalOpen = false;
            Completed?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnTargetChoiceChanged(string value)
    {
        RebuildVersionList(keepSelection: true);
        if (IsVanillaTarget)
            ClearPack();
        NotifyWarnings();
    }

    partial void OnIncludeSnapshotsChanged(bool value) => RebuildVersionList(keepSelection: true);

    partial void OnWipeWorldChanged(bool value) => NotifyWarnings();

    partial void OnMinecraftVersionChanged(string value) => NotifyWarnings();

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
        _dataDirectory = _configHost.LoadResult.DataDirectory ?? "";
        _infra = null;
        if (_config is not null
            && _cloud.Session is not null
            && !string.IsNullOrWhiteSpace(_config.ObjectStorage.Namespace)
            && !string.IsNullOrWhiteSpace(_config.ObjectStorage.Bucket))
        {
            var os = new ObjectStorageService(_cloud.Session, _config.ObjectStorage);
            _infra = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
        }
    }

    private async Task LoadVersionsAsync()
    {
        try
        {
            var mojang = await _catalogs.GetMojangAsync().ConfigureAwait(true);
            _manifest = mojang.Manifest;
            _mojangCatalogNotes = mojang.Notes;
        }
        catch (Exception ex)
        {
            _mojangCatalogNotes = $"Version catalog failed: {ex.Message}";
        }

        try
        {
            var paper = await _paperCatalog.LoadProjectCatalogAsync().ConfigureAwait(true);
            _paperProject = paper.Project;
            _paperCatalogNotes = paper.Notes;
        }
        catch (Exception ex)
        {
            _paperCatalogNotes = $"Paper version list failed: {ex.Message}";
        }

        RebuildVersionList(keepSelection: true);
    }

    private void RebuildVersionList(bool keepSelection)
    {
        if (IsModdedTarget)
        {
            VersionCatalogNotes = "Minecraft version comes from the pack you import.";
            OnPropertyChanged(nameof(VersionIds));
            return;
        }

        var previous = keepSelection ? MinecraftVersion : "";
        if (string.IsNullOrWhiteSpace(previous))
            previous = _currentMinecraftVersion ?? "";
        _versionIds.Clear();

        if (IsPaperTarget)
        {
            VersionCatalogNotes = string.IsNullOrWhiteSpace(_paperCatalogNotes)
                ? "Paper versions (Optimized Vanilla)."
                : _paperCatalogNotes;
            if (_paperProject is not null)
            {
                foreach (var id in PaperFillV3Client.FlattenVersionIds(_paperProject))
                    _versionIds.Add(id);
            }

            var target = !string.IsNullOrWhiteSpace(previous) && _versionIds.Contains(previous)
                ? previous
                : (_paperProject is null
                    ? previous
                    : PaperFillV3Client.DefaultVersionId(_paperProject));
            if (string.IsNullOrWhiteSpace(target) && _versionIds.Count > 0)
                target = _versionIds[0];
            MinecraftVersion = target;
            OnPropertyChanged(nameof(VersionIds));
            return;
        }

        if (_manifest is null)
        {
            VersionCatalogNotes = string.IsNullOrWhiteSpace(_mojangCatalogNotes)
                ? "Loading Minecraft versions…"
                : _mojangCatalogNotes;
            OnPropertyChanged(nameof(VersionIds));
            return;
        }

        VersionCatalogNotes = _mojangCatalogNotes;
        foreach (var v in MojangVersionCatalog.Filter(_manifest, IncludeSnapshots))
            _versionIds.Add(v.Id);
        var mojangTarget = !string.IsNullOrWhiteSpace(previous) && _versionIds.Contains(previous)
            ? previous
            : MojangVersionCatalog.DefaultVersionId(_manifest);
        MinecraftVersion = mojangTarget;
        OnPropertyChanged(nameof(VersionIds));
    }

    private async Task AnalyzePackPathAsync(string path)
    {
        IsAnalyzingPack = true;
        PackBlockReason = "";
        PackSummary = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        StatusMessage = "Analyzing modpack…";
        try
        {
            var result = await Task.Run(() =>
                SetupPackImport.AnalyzeFile(path, ExcludeIncludeListRefresh.Shared));
            if (!result.Succeeded || result.Value is null)
            {
                _packPreview = null;
                PackPath = path;
                PackCanContinue = false;
                PackBlockReason = result.Error ?? "Could not analyze this file.";
                StatusMessage = PackBlockReason;
                return;
            }

            _packPreview = result.Value;
            PackPath = result.Value.SourcePath;
            PackCanContinue = result.Value.CanContinue;
            PackSummary = result.Value.ConfirmableSummary;
            PackBlockReason = result.Value.CanContinue
                ? ""
                : (result.Value.BlockReason ?? "This pack cannot be installed.");
            if (result.Value.CanContinue)
            {
                MinecraftVersion = result.Value.MinecraftVersion;
                StatusMessage = PackNeedsReview
                    ? ChangeServerTypeUx.PackNeedsReview
                    : "Confirm the pack, then continue.";
            }
            else
            {
                StatusMessage = PackBlockReason;
            }
        }
        catch (Exception ex)
        {
            _packPreview = null;
            PackCanContinue = false;
            PackBlockReason = "Analyze failed: " + ex.Message;
            StatusMessage = PackBlockReason;
        }
        finally
        {
            IsAnalyzingPack = false;
            OnPropertyChanged(nameof(PackNeedsReview));
            OnPropertyChanged(nameof(ShowReviewOnMods));
            OnPropertyChanged(nameof(ShowPackChecks));
            OnPropertyChanged(nameof(CanSubmit));
            OnPropertyChanged(nameof(SubmitDisabledReason));
            NotifyWarnings();
        }
    }

    private void ClearPack()
    {
        _packPreview = null;
        PackPath = "";
        PackSummary = "";
        PackBlockReason = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        OnPropertyChanged(nameof(PackNeedsReview));
        OnPropertyChanged(nameof(ShowReviewOnMods));
        OnPropertyChanged(nameof(ShowPackChecks));
    }

    private void NotifyWarnings()
    {
        if (IsModdedTarget)
        {
            SaveCompatibilityWarning = _packPreview is null || WipeWorld
                ? ""
                : PackReplaceUx.VisibleSaveCompatibilityWarning(
                    WipeWorld,
                    PackReplaceSaveCompatibility.Warn(
                        _currentMinecraftVersion,
                        _currentKind,
                        _packPreview.MinecraftVersion,
                        _packPreview.Loader)) ?? "";
        }
        else
        {
            var plan = ChangeServerTypePlanner.TryCreate(
                TargetChoice,
                MinecraftVersion,
                packPath: null,
                WipeWorld,
                _currentMinecraftVersion,
                _currentKind);
            SaveCompatibilityWarning = plan.Succeeded && plan.Value is not null
                ? PackReplaceUx.VisibleSaveCompatibilityWarning(WipeWorld, plan.Value.SaveCompatibilityWarning) ?? ""
                : "";
        }

        OnPropertyChanged(nameof(DirectionWarning));
        OnPropertyChanged(nameof(ShowDirectionWarning));
        OnPropertyChanged(nameof(ShowSaveCompatibilityWarning));
        OnPropertyChanged(nameof(ConfirmHint));
        OnPropertyChanged(nameof(CanSubmit));
        OnPropertyChanged(nameof(SubmitDisabledReason));
        OnPropertyChanged(nameof(IsPaperTarget));
        OnPropertyChanged(nameof(ShowPackChecks));
        OnPropertyChanged(nameof(PackNeedsReview));
        OnPropertyChanged(nameof(ShowReviewOnMods));
    }
}
