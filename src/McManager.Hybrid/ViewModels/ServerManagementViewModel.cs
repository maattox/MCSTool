using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Core.Setup;
using McManager.Core.Usage;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Server tab: Object Storage world backups + SSH replace/wipe. Change pack pick/review
/// works while VM1 is stopped; Install starts VM1 then runs the existing replace.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart or dispose <c>OciSession</c>.
/// </summary>
public sealed partial class ServerManagementViewModel : ObservableObject, IDisposable
{
    private static readonly FileTypeFilter ZipFilter = new("ZIP files", ".zip");
    private static readonly FileTypeFilter PngFilter = new("PNG images", ".png");
    private static readonly FileTypeFilter AllFilesFilter = new("All files", ".*");
    private static readonly FileTypeFilter PackFilter = new("Modpack archives", ".mrpack", ".zip");
    private static readonly FileTypeFilter MrpackFilter = new("Modrinth pack", ".mrpack");
    private static readonly TimeSpan PackElapsedTickPeriod = TimeSpan.FromSeconds(1);

    private ManagerLocalConfig? _config;
    private BackupStore? _backups;
    private InfraMetaStore? _infra;
    private OversizedWorldBackupStore? _oversizedWorld;
    private ChatMessagesStore? _chat;
    private ServerPropertiesStore? _settings;
    private byte[]? _pendingIconPng;
    private bool _clearIcon;
    private ISshService _ssh = null!;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly IFilePicker _filePicker;
    private readonly IUiDialogs _dialogs;
    private readonly MainViewModel _main;
    private readonly NotificationCenter _notices;
    private readonly ActionBanner _banner;
    private readonly SetupBootstrapService _bootstrap;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private bool _forwardBanner;
    private string? _currentMinecraftVersion;
    private string? _currentLoaderOrDistribution;
    private SetupPackPreview? _packPreview;
    private HashSet<string> _operatorSkipTerms = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _operatorKeepTerms = new(StringComparer.OrdinalIgnoreCase);
    private string? _sessionError;
    private long _currentBackupBytes;
    private string _dataDirectory = "";
    private ImportedPackArchiveInfo? _localPack;
    private CancellationTokenSource? _packElapsedCts;
    private DateTimeOffset? _packElapsedRunningSince;
    private TimeSpan _packElapsedAccumulated;
    private bool _packElapsedStarted;
    private bool _packReplaceRunning;
    private bool _opened;
    private bool _openInFlight;
    private bool _identityLoaded;

    public ObservableCollection<WorldBackupInfo> Backups { get; } = [];

    public ObservableCollection<string> ModFiles { get; } = [];

    public ObservableCollection<QuarantinedFileEntry> QuarantinedMods { get; } = [];

    public ObservableCollection<ChatTemplateRow> ChatTemplates { get; } = [];

    [ObservableProperty]
    private WorldBackupInfo? _selectedBackup;

    [ObservableProperty]
    private string _statusMessage = "Open this tab to list world backups.";

    [ObservableProperty]
    private string _softCapDisplay = "—";

    [ObservableProperty]
    private string _progressDisplay = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _serverNameDisplay = "—";

    [ObservableProperty]
    private string _minecraftVersionDisplay = "—";

    [ObservableProperty]
    private string _lastBackupDisplay = "—";

    [ObservableProperty]
    private string _backupStorageDisplay = "—";

    [ObservableProperty]
    private bool _isModdedServer;

    [ObservableProperty]
    private bool _hasLocalPackArchive;

    [ObservableProperty]
    private bool _isModdingBusy;

    [ObservableProperty]
    private string _packIdentityDisplay = "";

    [ObservableProperty]
    private string _moddingSummary = "";

    [ObservableProperty]
    private string _moddingHint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DownloadLatestTitle))]
    [NotifyPropertyChangedFor(nameof(DownloadLatestButtonLabel))]
    private bool _oversizedWorldBlocked;

    [ObservableProperty]
    private string _oversizedBanner = "";

    public bool HasObjectStorage => _backups is not null;

    public bool AnyBusy => IsBusy || IsModdingBusy || IsAnalyzingPack;

    public bool Vm1IsRunning => ManagePowerUx.IsVm1Running(_main.Vm1Lifecycle);

    public bool CanDownloadPack =>
        ModdingPanelLogic.CanDownloadPack(IsModdedServer, HasLocalPackArchive) && !AnyBusy;

    public bool HasQuarantinedMods => QuarantinedMods.Count > 0;

    public bool CanActOnQuarantine => HasQuarantinedMods && Vm1IsRunning && !AnyBusy;

    public bool CanPickPack => PackReplaceUx.CanPick(Vm1IsRunning, AnyBusy);

    public bool CanInstallPack =>
        PackReplaceUx.CanInstall(
            Vm1IsRunning,
            AnyBusy,
            PackCanContinue,
            PackConfirmed,
            ClientPackAcknowledged,
            PackIdentityComplete,
            PackFreezeBlockReason);

    public string ChangePackTitle =>
        CanPickPack
            ? PackReplaceUx.ChangePackPickHint
            : PackReplaceUx.PickDisabledReason(Vm1IsRunning, AnyBusy);

    public string InstallPackTitle =>
        CanInstallPack
            ? PackReplaceUx.ConfirmBody(WipeWorld)
            : PackReplaceUx.InstallDisabledReason(
                Vm1IsRunning,
                AnyBusy,
                PackCanContinue,
                PackConfirmed,
                ClientPackAcknowledged,
                PackIdentityComplete,
                PackFreezeBlockReason);

    public string PackFileNameDisplay =>
        string.IsNullOrWhiteSpace(PackPath) ? "" : Path.GetFileName(PackPath);

    public bool ShowPackSummary =>
        ShowChangePackUi
        && !IsAnalyzingPack
        && (!string.IsNullOrWhiteSpace(PackSummary) || !string.IsNullOrWhiteSpace(PackBlockReason));

    public bool ShowPackConfirmChecks => ShowPackSummary && PackCanContinue;

    public bool ShowPackIdentityFields =>
        ShowPackSummary && PackCanContinue && PackNeedsIdentityConfirm;

    public bool PackIdentityComplete =>
        !PackNeedsIdentityConfirm
        || DerivedPackIdentity.IsComplete(
            PackMinecraftVersion,
            PackLoader,
            PackLoaderVersion,
            PackJavaMajorText);

    public bool ShowDetectionMismatch =>
        ShowPackIdentityFields
        && DerivedPackIdentity.DisagreesWithDetection(
            DetectedMinecraftVersion,
            DetectedLoader,
            PackMinecraftVersion,
            PackLoader);

    public string DetectionMismatchWarning =>
        DerivedPackIdentity.FormatDetectionMismatchWarning(
            DetectedMinecraftVersion,
            DetectedLoader,
            PackMinecraftVersion,
            PackLoader);

    public bool ShowOverrideListWarning =>
        ShowPackConfirmChecks && !string.IsNullOrWhiteSpace(PackOverrideListWarning);

    public bool ShowSkipListWarning =>
        PackReplaceUx.ShouldShowSkipListWarning(ShowPackAssistedReview);

    public bool ShowPackAssistedReview =>
        ShowPackSummary
        && PackCanContinue
        && _packPreview is not null
        && (_packPreview.NeedsAssistedReview
            || !PackReplaceUx.FreezeAllowsContinue(_packPreview.FreezeBlockReason)
            || _packPreview.AssistedReview.WillSkip.Any(i => i.SkipReason == PackFileSkipReason.OperatorSkip)
            || ((_operatorSkipTerms.Count > 0 || _operatorKeepTerms.Count > 0)
                && _packPreview.Kind == SetupPackImport.KindManualZip));

    public PackAssistedReview AssistedReview =>
        _packPreview?.AssistedReview ?? PackAssistedReview.Empty;

    public IReadOnlyList<string> PackJarOrder =>
        _packPreview?.JarRecords.Select(j => j.Path).ToArray() ?? [];

    public string PackFreezeBlockReason => _packPreview?.FreezeBlockReason ?? "";

    public bool PackLooksLikeLauncherInstance { get; private set; }

    public bool IsOperatorSkipped(string path) =>
        PackAssistedReviewActions.IsSkipped(_operatorSkipTerms, path);

    public bool ShowSaveCompatibilityWarning =>
        ShowPackConfirmChecks && !string.IsNullOrWhiteSpace(SaveCompatibilityWarning);

    public string FriendsNeedOneLiner => PackReplaceUx.FriendsNeedOneLiner;

    public string ClientPackAckLabel => PackReplaceUx.ClientPackAckLabel;

    public string PackConfirmLabel => PackReplaceUx.PackConfirmLabel;

    public string WipeWorldLabel => PackReplaceUx.WipeWorldLabel;

    public string SkipWarningBody => PackReplaceUx.SkipWarningBody;

    public string DownloadPackTitle =>
        CanDownloadPack
            ? "Save a copy of the confirmed pack file (manifest added for jar-root zips when you corrected versions)."
            : ModdingPanelLogic.DownloadDisabledReason(IsModdedServer, HasLocalPackArchive);

    public string VanillaEmptyState => ModdingPanelLogic.VanillaEmptyState;

    public string MissingArchiveMessage => ModdingPanelLogic.MissingArchiveMessage;

    public string ModdingHelpTitle => ModdingPanelLogic.HelpTitle;

    public const string PaneIdentity = "identity";
    public const string PaneSettings = "settings";
    public const string PaneWorld = "world";
    public const string PaneModding = "modding";
    public const string PaneChangePack = "pack";

    public bool IsChangePackPane =>
        string.Equals(ServerPane, PaneChangePack, StringComparison.Ordinal);

    public bool IsServerPane(string id) =>
        string.Equals(ServerPane, id, StringComparison.Ordinal);

    public void SelectServerPane(string pane)
    {
        if (string.IsNullOrWhiteSpace(pane)
            || string.Equals(ServerPane, pane, StringComparison.Ordinal))
            return;
        ServerPane = pane;
    }

    public bool ShowChangePackDock(bool onServerTab) =>
        ProgressDockUx.ShowChangePackDock(ShowChangePackUi, onServerTab, IsChangePackPane);

    public bool ShowPackJobProgress =>
        ProgressDockUx.ShowJobProgress(IsAnalyzingPack, _packReplaceRunning);

    public bool ShowPackElapsed => _packElapsedStarted;

    public string PackElapsedDisplay => ProgressDockUx.FormatElapsed(CurrentPackElapsed());

    public string DockStatus
    {
        get
        {
            if (IsAnalyzingPack)
            {
                return ProgressDockUx.OneLineStatus(
                    true,
                    PackAnalyzeCaption,
                    ProgressDockUx.ChangePackAnalyzeFallback);
            }

            if (_packReplaceRunning)
            {
                return ProgressDockUx.OneLineStatus(
                    true,
                    StatusMessage,
                    ProgressDockUx.ChangePackInstallFallback);
            }

            if (ShowPackConfirmChecks)
                return ProgressDockUx.ChangePackReviewStatus;

            return ProgressDockUx.ChangePackPickStatus;
        }
    }

    public string DownloadLatestTitle =>
        OversizedWorldBackupUx.DownloadLatestTitle(
            OversizedWorldBlocked,
            OversizedWorldBackupUx.Vm1IsRunning(_main.Vm1Lifecycle));

    public string DownloadLatestButtonLabel =>
        OversizedWorldBackupUx.DownloadLatestButtonLabel(OversizedWorldBlocked);

    public string WorldBackupsHelpTitle => OversizedWorldBackupUx.HelpTitle;

    public string IdentityHelpTitle =>
        "Name and description show in Minecraft’s server list when the game is running. Each box is one list line (59 characters). Select text and apply colors, or paste a motd= string. The in-game PNG is the list icon while Minecraft is up. Offline / starting / unavailable copies show on the doorbell while the server is off. Automated chat is what the idle timer says in-game before a stop. Save, then Restart Minecraft (or Start) to apply the in-game icon. The doorbell icon updates on Save.";

    public string IconStatesHelp =>
        "In-game is the icon shown while the server is up. Offline, Starting, and Unavailable are shown while the server is off, waking, or cannot start (daily hours or spend-brake).";

    public string MotdPreview =>
        ServerIdentityUx.BuildMotd(IdentityName, IdentityDescription);

    public string SettingsHelpTitle =>
        "Gameplay settings are stored in the cloud and written to server.properties the next time Minecraft starts. Save, then Restart (or Start). Name, icon, and MOTD stay on Identity. PvP and simulation distance hide when this Minecraft version does not have those keys.";

    public IReadOnlyList<string> SettingsDifficulties => ServerPropertiesCatalog.Difficulties;

    public IReadOnlyList<string> SettingsGamemodes => ServerPropertiesCatalog.Gamemodes;

    public bool CanSaveIdentity => HasObjectStorage && !AnyBusy;

    public bool CanSaveSettings => HasObjectStorage && !AnyBusy;

    public bool CanClearIcon =>
        HasObjectStorage && !AnyBusy && (HasCustomIcon || _pendingIconPng is { Length: > 0 });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotdPreview))]
    private string _identityName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MotdPreview))]
    private string _identityDescription = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClearIcon))]
    private bool _hasCustomIcon;

    [ObservableProperty]
    private string _iconPreviewDataUrl = "";

    [ObservableProperty]
    private string _idleIconPreviewDataUrl = "";

    [ObservableProperty]
    private string _startingIconPreviewDataUrl = "";

    [ObservableProperty]
    private string _exhaustedIconPreviewDataUrl = "";

    [ObservableProperty]
    private bool _showChatTemplates;

    [ObservableProperty]
    private string _identityStatus = "";

    [ObservableProperty]
    private string _settingsDifficulty = "normal";

    [ObservableProperty]
    private string _settingsGamemode = "survival";

    [ObservableProperty]
    private int _settingsMaxPlayers = 20;

    [ObservableProperty]
    private bool _settingsPvp = true;

    [ObservableProperty]
    private int _settingsSpawnProtection = 16;

    [ObservableProperty]
    private int _settingsViewDistance = 10;

    [ObservableProperty]
    private int _settingsSimulationDistance = 10;

    [ObservableProperty]
    private bool _settingsHardcore;

    [ObservableProperty]
    private bool _settingsForceGamemode;

    [ObservableProperty]
    private bool _settingsAllowFlight;

    [ObservableProperty]
    private bool _showSettingsPvp;

    [ObservableProperty]
    private bool _showSettingsSimulationDistance;

    [ObservableProperty]
    private string _settingsStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChangePackPane))]
    private string _serverPane = PaneIdentity;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    [NotifyPropertyChangedFor(nameof(ShowPackConfirmChecks))]
    [NotifyPropertyChangedFor(nameof(ShowOverrideListWarning))]
    [NotifyPropertyChangedFor(nameof(ShowSkipListWarning))]
    [NotifyPropertyChangedFor(nameof(ShowSaveCompatibilityWarning))]
    private bool _showChangePackUi;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AnyBusy))]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    private bool _isAnalyzingPack;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackFileNameDisplay))]
    private string _packPath = "";

    [ObservableProperty]
    private string _packName = "";

    [ObservableProperty]
    private string _packMinecraftVersion = "";

    [ObservableProperty]
    private string _packLoader = "";

    [ObservableProperty]
    private string _packLoaderVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PackIdentityComplete))]
    [NotifyPropertyChangedFor(nameof(CanInstallPack))]
    private string _packJavaMajorText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackIdentityFields))]
    private bool _packNeedsIdentityConfirm;

    [ObservableProperty]
    private string _packSourcePath = "";

    [ObservableProperty]
    private string _detectedMinecraftVersion = "";

    [ObservableProperty]
    private string _detectedLoader = "";

    private bool _javaMajorCustomized;
    private bool _applyingJavaFloor;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    private string _packSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    private string _packBlockReason = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverrideListWarning))]
    [NotifyPropertyChangedFor(nameof(ShowSkipListWarning))]
    private string _packOverrideListWarning = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSaveCompatibilityWarning))]
    private string _saveCompatibilityWarning = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackConfirmChecks))]
    [NotifyPropertyChangedFor(nameof(CanInstallPack))]
    private bool _packCanContinue;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallPack))]
    [NotifyPropertyChangedFor(nameof(InstallPackTitle))]
    private bool _packConfirmed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallPack))]
    [NotifyPropertyChangedFor(nameof(InstallPackTitle))]
    private bool _clientPackAcknowledged;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallPackTitle))]
    [NotifyPropertyChangedFor(nameof(ShowSaveCompatibilityWarning))]
    private bool _wipeWorld;

    [ObservableProperty]
    private string _packAnalyzeCaption = "";

    public ServerManagementViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IFilePicker filePicker,
        IUiDialogs dialogs,
        MainViewModel main,
        NotificationCenter notices,
        ActionBanner banner,
        SetupBootstrapService bootstrap,
        IUiClock clock,
        IUiDispatcher dispatcher)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _main = main;
        _notices = notices;
        _banner = banner;
        _bootstrap = bootstrap;
        _clock = clock;
        _dispatcher = dispatcher;

        BindFromHost();
        _forwardBanner = true;
        _session.Reloaded += OnSessionReloaded;
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    partial void OnStatusMessageChanged(string value)
    {
        NotifyDock();
        if (!_forwardBanner || !TabStatusBannerPolicy.ShouldForwardServerManagementStatus(value))
            return;
        _banner.ShowInferred(value);
    }

    partial void OnIdentityStatusChanged(string value)
    {
        if (!_forwardBanner
            || string.IsNullOrWhiteSpace(value)
            || !TabStatusBannerPolicy.ShouldForwardServerManagementIdentityStatus(value))
            return;
        _banner.ShowInferred(value);
    }

    partial void OnProgressDisplayChanged(string value)
    {
        if (!_forwardBanner || string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(StatusMessage))
            return;
        _banner.Show($"{StatusMessage} {value}".Trim(), ActionBannerSeverity.Progress);
    }

    private void OnSessionReloaded(object? sender, EventArgs e)
    {
        var wasOpened = _opened;
        BindFromHost();
        if (wasOpened)
            _ = EnsureOpenedAsync();
        else
            _ = EnsureIdentityLoadedAsync();
    }

    private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle) or null)
            NotifyModdingCommands();
    }

    partial void OnWipeWorldChanged(bool value) => RefreshSaveCompatibilityWarning();

    partial void OnIsAnalyzingPackChanged(bool value) => NotifyModdingCommands();

    private void BindFromHost()
    {
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        BindFromHostCore();
        _forwardBanner = wasForward;
    }

    private void BindFromHostCore()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
        _sessionError = _cloud.SessionError;
        _dataDirectory = _configHost.LoadResult.DataDirectory ?? "";
        _backups = null;
        _infra = null;
        _oversizedWorld = null;
        _chat = null;
        _settings = null;
        _pendingIconPng = null;
        _clearIcon = false;
        IdentityName = "";
        IdentityDescription = "";
        HasCustomIcon = false;
        IconPreviewDataUrl = "";
        IdleIconPreviewDataUrl = "";
        StartingIconPreviewDataUrl = "";
        ExhaustedIconPreviewDataUrl = "";
        IdentityStatus = "";
        SettingsStatus = "";
        _currentMinecraftVersion = null;
        ApplySettingsDefaults();
        ChatTemplates.Clear();
        ServerNameDisplay = "—";
        MinecraftVersionDisplay = "—";
        LastBackupDisplay = "—";
        BackupStorageDisplay = "—";
        SoftCapDisplay = "—";
        OversizedWorldBlocked = false;
        OversizedBanner = "";
        Backups.Clear();
        SelectedBackup = null;
        _currentBackupBytes = 0;
        _opened = false;
        _identityLoaded = false;
        ResetModdingState();

        if (_config is not null)
        {
            ServerNameDisplay = string.IsNullOrWhiteSpace(_config.Vm1.DisplayName)
                ? "—"
                : _config.Vm1.DisplayName.Trim();
        }

        if (_config is not null && _cloud.Session is not null)
        {
            var os = new ObjectStorageService(_cloud.Session, _config.ObjectStorage);
            _backups = new BackupStore(os, _config.ObjectStorage);
            _infra = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
            _oversizedWorld = new OversizedWorldBackupStore(os, _config.ObjectStorage.Prefixes);
            _chat = new ChatMessagesStore(os, _config.ObjectStorage.Prefixes);
            _settings = new ServerPropertiesStore(os, _config.ObjectStorage.Prefixes);
            SoftCapDisplay = _backups.FormatSoftCapLine(0);
            BackupStorageDisplay = $"0.0 / {_backups.SoftCapGb:0.#} GB";
        }

        StatusMessage = _backups is null
            ? (string.IsNullOrWhiteSpace(_sessionError)
                ? "Shared backup storage isn't configured."
                : _sessionError)
            : "Open this tab to list world backups.";
        OnPropertyChanged(nameof(HasObjectStorage));
        OnPropertyChanged(nameof(CanSaveIdentity));
        OnPropertyChanged(nameof(CanSaveSettings));
        OnPropertyChanged(nameof(CanClearIcon));
        BindLocalPack();
        FillDefaultChatRows();
    }

    public Task RefreshAsync() => RefreshCatalogAsync(setBusy: true);

    public async Task EnsureOpenedAsync()
    {
        if (_opened || _openInFlight)
            return;

        _openInFlight = true;
        try
        {
            await RefreshCatalogAsync(setBusy: false);
            await RefreshMinecraftVersionAsync(includeLiveMods: false);
            _opened = true;
        }
        finally
        {
            _openInFlight = false;
        }
    }

    /// <summary>
    /// Load MOTD / list name for Overview without opening the Server tab (no backup listing).
    /// </summary>
    public Task EnsureIdentityLoadedAsync()
    {
        if (_identityLoaded)
            return Task.CompletedTask;
        return LoadIdentityAsync();
    }

    private async Task RefreshCatalogAsync(bool setBusy)
    {
        if (_backups is null)
        {
            StatusMessage = string.IsNullOrWhiteSpace(_sessionError)
                ? "Shared backup storage isn't configured."
                : _sessionError;
            return;
        }

        if (IsBusy)
            return;

        if (setBusy)
            IsBusy = true;
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        if (setBusy)
            StatusMessage = "Listing backups…";
        ProgressDisplay = "";

        try
        {
            await RefreshOversizedFlagAsync();
            await LoadIdentityAsync();
            await LoadSettingsAsync();
            var result = await _backups.ListWorldBackupsAsync();
            if (!result.Succeeded || result.Value is null)
            {
                var error = result.Error ?? "List failed.";
                StatusMessage = error;
                _banner.Show(error, ActionBannerSeverity.Error);
                return;
            }

            ApplyBackupList(result.Value, preserveSelection: true);
            StatusMessage = OversizedWorldBlocked
                ? "Automatic cloud backups are paused. Download latest copies the live world over SSH."
                : (Backups.Count == 0
                    ? "No world backups stored yet."
                    : $"Listed {Backups.Count} backup(s). Select one to download.");
        }
        finally
        {
            _forwardBanner = wasForward;
            if (setBusy)
                IsBusy = false;
        }
    }

    public Task DownloadSelectedAsync() => DownloadToPickerAsync(SelectedBackup);

    public Task DownloadBackupAsync(WorldBackupInfo? backup) => DownloadToPickerAsync(backup);

    public async Task DownloadLatestAsync()
    {
        await RefreshOversizedFlagAsync();
        if (OversizedWorldBlocked)
        {
            await DownloadLiveWorldViaSshAsync();
            return;
        }

        await DownloadToPickerAsync(Backups.FirstOrDefault());
    }

    public async Task UploadAsync()
    {
        if (_backups is null || IsBusy)
            return;

        var localPath = await _filePicker.OpenFileAsync(new FilePickRequest
        {
            Title = "Upload world zip to Object Storage",
            Filters = [ZipFilter],
        });

        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Upload cancelled.";
            return;
        }

        if (!File.Exists(localPath))
        {
            StatusMessage = "Could not resolve local zip path.";
            return;
        }

        var zipBytes = new FileInfo(localPath).Length;
        var check = _backups.EvaluateUpload(zipBytes, _currentBackupBytes);
        if (!check.Allowed)
        {
            StatusMessage = check.Message;
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Upload backup?",
            $"Upload {Path.GetFileName(localPath)} ({WorldBackupInfo.FormatSize(zipBytes)}) "
            + "to Object Storage as a new backups/world-*.zip?",
            "Upload");
        if (!confirmed)
        {
            StatusMessage = "Upload cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Uploading…";
        ProgressDisplay = "0 B";

        try
        {
            var progress = new Progress<long>(bytes =>
                ProgressDisplay = WorldBackupInfo.FormatSize(bytes));
            var result = await _backups.UploadNewBackupAsync(localPath, progress);
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "Upload failed.";
                return;
            }

            StatusMessage = $"Uploaded {result.Value}";
            await RefreshWithoutBusyGuardAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ReplaceWorldAsync()
    {
        if (IsBusy)
            return;

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        var lifeRaw = _main.Vm1Lifecycle ?? "";
        var life = lifeRaw.ToUpperInvariant();
        if (life != "RUNNING")
        {
            StatusMessage =
                $"VM1 is '{lifeRaw}' — Replace requires RUNNING. "
                + "You can Upload a zip to Object Storage while stopped, then Start and Replace.";
            return;
        }

        var localPath = await _filePicker.OpenFileAsync(new FilePickRequest
        {
            Title = "Replace VM1 world from local zip",
            Filters = [ZipFilter],
        });

        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Replace cancelled.";
            return;
        }

        if (!File.Exists(localPath))
        {
            StatusMessage = "Could not resolve local zip path.";
            return;
        }

        var worldPath = _config.Vm1.WorldPath;
        var confirmed = await _dialogs.ConfirmAsync(
            "Replace world on VM1?",
            "This STOPS Minecraft, replaces the world at "
            + $"{worldPath} (previous folder moved aside as .bak.*), then starts Minecraft again. "
            + "Zip contents must be world-folder relative (same as SoftStop backups). Continue?",
            "Replace");
        if (!confirmed)
        {
            StatusMessage = "Replace cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Replacing world via SSH…";
        ProgressDisplay = "";

        try
        {
            var result = await _ssh.ReplaceWorldAsync(_config.Vm1, localPath);
            StatusMessage = result.Succeeded
                ? $"World replaced at {worldPath}. Minecraft start requested."
                : result.Error ?? "Replace failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task WipeWorldAsync()
    {
        if (IsBusy)
            return;

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        var lifeRaw = _main.Vm1Lifecycle ?? "";
        var life = lifeRaw.ToUpperInvariant();
        if (life != "RUNNING")
        {
            StatusMessage =
                $"VM1 is '{lifeRaw}' — Wipe world requires RUNNING. "
                + "Start the game VM first, then wipe. Cloud backups stay until you delete them separately.";
            return;
        }

        if (!WorldWipe.TryCreate(_config.Vm1.WorldPath, out var plan, out var pathError))
        {
            StatusMessage = pathError ?? "vm1.world_path is invalid.";
            return;
        }

        var backupHint = _backups is null
            ? "There is no shared backup storage configured, so this cannot be undone from Manager."
            : "Cloud backups (Download World Save) are kept. Download one first if you might want this world back.";

        var confirmed = await _dialogs.ConfirmAsync(
            "Wipe the live world?",
            "This deletes the current world on the server at "
            + $"{plan.WorldPath}. Minecraft will be stopped, that folder removed, and Minecraft started again "
            + "so a new world generates. This cannot be undone except by restoring a backup. "
            + "Mods, loader files, and server.properties are not deleted. "
            + backupHint,
            "Wipe world");
        if (!confirmed)
        {
            StatusMessage = "Wipe cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Wiping live world via SSH…";
        ProgressDisplay = "";

        try
        {
            var result = await _ssh.WipeWorldAsync(_config.Vm1);
            StatusMessage = result.Succeeded
                ? $"Live world wiped at {plan.WorldPath}. Minecraft start requested. "
                  + "Cloud backups were not deleted."
                : result.Error ?? "Wipe failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RefreshMinecraftVersionAsync(bool includeLiveMods = true)
    {
        BindLocalPack();
        if (_infra is null)
        {
            _currentMinecraftVersion = null;
            MinecraftVersionDisplay = "—";
            ApplyServerKind(null);
            await LoadSettingsAsync();
            return;
        }

        var read = await _infra.GetAsync();
        if (!read.Succeeded || read.Value?.Document is null)
        {
            _currentMinecraftVersion = null;
            ApplyServerKind(null);
            await LoadSettingsAsync();
            return;
        }

        var doc = read.Value.Document;
        var version = doc.Game.MinecraftVersion;
        _currentMinecraftVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        MinecraftVersionDisplay = _currentMinecraftVersion ?? "—";
        ApplyServerKind(doc.Game.ServerKind);
        RefreshSaveCompatibilityWarning();
        await LoadSettingsAsync();
        if (includeLiveMods)
            await RefreshLiveModsAsync();
    }

    public async Task RefreshLiveModsAsync()
    {
        if (!IsModdedServer)
            return;
        if (IsModdingBusy)
            return;
        if (_config is null)
        {
            ModdingHint = "Local config is missing.";
            return;
        }

        var lifeRaw = _main.Vm1Lifecycle ?? "";
        var life = lifeRaw.ToUpperInvariant();
        if (life != "RUNNING")
        {
            ModFiles.Clear();
            QuarantinedMods.Clear();
            ModdingSummary = "";
            ModdingHint = ModdingPanelLogic.VmStoppedHint;
            return;
        }

        IsModdingBusy = true;
        ModdingHint = "Listing mods on the game VM…";
        try
        {
            var run = await _ssh.RunCommandAsync(
                SshTarget.FromVm1(_config.Vm1),
                ServerModsInspect.RemoteCommand);
            if (!run.Succeeded)
            {
                ModFiles.Clear();
                QuarantinedMods.Clear();
                ModdingSummary = "";
                ModdingHint = run.Error ?? "Could not list mods on the game VM.";
                return;
            }

            if (!ServerModsInspect.TryParse(run.Output, out var inspect, out var parseError))
            {
                ModFiles.Clear();
                QuarantinedMods.Clear();
                ModdingSummary = "";
                ModdingHint = parseError ?? "Could not parse the mods listing.";
                return;
            }

            ModFiles.Clear();
            foreach (var name in inspect.FileNames)
                ModFiles.Add(name);
            BindQuarantined(inspect.QuarantinedFiles);
            ModdingSummary = inspect.SummaryLine();
            ModdingHint = inspect.ModsDirectoryMissing
                ? "No mods folder on the server yet."
                : (ModFiles.Count == 0 ? "No files in mods/." : "");
        }
        finally
        {
            IsModdingBusy = false;
            OnPropertyChanged(nameof(HasQuarantinedMods));
            OnPropertyChanged(nameof(CanActOnQuarantine));
        }
    }

    public string FormatQuarantineCopy(QuarantinedFileEntry entry)
    {
        var likely = CrashQuarantine.GuessClientOnlyFromLists(entry.Path, entry.DisplayName);
        return CrashQuarantine.EntryCopy(entry, likely);
    }

    public async Task KeepExcludedAsync(QuarantinedFileEntry entry)
    {
        if (!CanActOnQuarantine || _config is null)
            return;

        var ok = false;
        IsModdingBusy = true;
        try
        {
            var run = await _ssh.RunCommandAsync(
                SshTarget.FromVm1(_config.Vm1),
                CrashQuarantine.RemoteCommand("ack", relativePath: entry.Path));
            var parsed = CrashQuarantine.ParseRemote(run.Output);
            if (!run.Succeeded || !parsed.Ok)
            {
                var err = parsed.Error ?? run.Error ?? "Could not keep that mod excluded.";
                _banner.Show(err, ActionBannerSeverity.Error);
                return;
            }

            var hash = _localPack?.Sha256Hex ?? Layer2LocalOverlay.TryHashFile(_localPack?.ArchivePath);
            if (!string.IsNullOrWhiteSpace(hash) && !string.IsNullOrWhiteSpace(_dataDirectory))
            {
                Layer2LocalOverlay.PromoteExclude(
                    _dataDirectory,
                    hash,
                    string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.FileName : entry.DisplayName);
            }

            _banner.Show(CrashQuarantine.KeepExcludedCopy(entry.DisplayName), ActionBannerSeverity.Success);
            ok = true;
        }
        finally
        {
            IsModdingBusy = false;
        }

        if (ok)
            await RefreshLiveModsAsync();
    }

    public async Task PutBackAsync(QuarantinedFileEntry entry)
    {
        if (!CanActOnQuarantine || _config is null)
            return;

        var ok = false;
        IsModdingBusy = true;
        try
        {
            var run = await _ssh.RunCommandAsync(
                SshTarget.FromVm1(_config.Vm1),
                CrashQuarantine.RemoteCommand("restore", relativePath: entry.Path));
            var parsed = CrashQuarantine.ParseRemote(run.Output);
            if (!run.Succeeded || !parsed.Ok)
            {
                var err = parsed.Error ?? run.Error ?? "Could not put that mod back.";
                _banner.Show(err, ActionBannerSeverity.Error);
                return;
            }

            _banner.Show(CrashQuarantine.PutBackCopy(entry.DisplayName), ActionBannerSeverity.Warning);
            ok = true;
        }
        finally
        {
            IsModdingBusy = false;
        }

        if (ok)
            await RefreshLiveModsAsync();
    }

    private void BindQuarantined(IReadOnlyList<QuarantinedFileEntry> entries)
    {
        QuarantinedMods.Clear();
        foreach (var entry in entries)
        {
            if (entry.NeedsAck)
                QuarantinedMods.Add(entry);
        }

        OnPropertyChanged(nameof(HasQuarantinedMods));
        OnPropertyChanged(nameof(CanActOnQuarantine));
    }

    public async Task DownloadPackAsync()
    {
        if (AnyBusy)
            return;

        if (!IsModdedServer)
        {
            ModdingHint = ModdingPanelLogic.VanillaEmptyState;
            return;
        }

        BindLocalPack();
        if (_localPack is null || !File.Exists(_localPack.ArchivePath))
        {
            HasLocalPackArchive = false;
            ModdingHint = ModdingPanelLogic.MissingArchiveMessage;
            NotifyModdingCommands();
            return;
        }

        var suggested = _localPack.SuggestedDownloadFileName;
        var ext = Path.GetExtension(suggested).TrimStart('.');
        if (string.IsNullOrWhiteSpace(ext))
            ext = "zip";

        var filters = new List<FileTypeFilter>();
        if (ext.Equals("mrpack", StringComparison.OrdinalIgnoreCase))
            filters.Add(new FileTypeFilter("Modrinth pack", ".mrpack"));
        else
            filters.Add(new FileTypeFilter("ZIP files", ".zip"));
        filters.Add(AllFilesFilter);

        var localPath = await _filePicker.SaveFileAsync(new FileSaveRequest
        {
            Title = "Download imported pack",
            FileName = suggested,
            DefaultExtension = ext,
            Filters = filters,
        });

        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Download pack cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Copying original imported pack…";
        ProgressDisplay = "";
        var archivePath = _localPack.ArchivePath;
        try
        {
            await Task.Run(() => File.Copy(archivePath, localPath, overwrite: true));
            StatusMessage = $"Saved confirmed pack to {localPath}";
            ModdingHint = "This is the confirmed pack file saved on this PC, not a zip of server mods.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = "Could not copy the original pack: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginChangePack()
    {
        if (!PackReplaceUx.CanPick(Vm1IsRunning, AnyBusy))
        {
            StatusMessage = PackReplaceUx.PickDisabledReason(Vm1IsRunning, AnyBusy);
            return;
        }

        ShowChangePackUi = true;
    }

    public void CancelChangePack()
    {
        if (IsBusy || IsAnalyzingPack)
            return;
        ClearPackReplaceFields(hidePanel: true);
        StatusMessage = "Change pack cancelled.";
    }

    public async Task PickPackAsync()
    {
        if (IsAnalyzingPack)
            return;
        if (!PackReplaceUx.CanPick(Vm1IsRunning, AnyBusy))
        {
            StatusMessage = PackReplaceUx.PickDisabledReason(Vm1IsRunning, AnyBusy);
            return;
        }

        ShowChangePackUi = true;
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
        if (IsAnalyzingPack || content is null)
            return;
        if (!PackReplaceUx.CanPick(Vm1IsRunning, AnyBusy))
        {
            StatusMessage = PackReplaceUx.PickDisabledReason(Vm1IsRunning, AnyBusy);
            return;
        }

        ShowChangePackUi = true;
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
            safeName = "dropped-pack.zip";

        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-change-pack-drop");
        Directory.CreateDirectory(dir);
        var dest = Path.Combine(dir, safeName);
        try
        {
            await using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await content.CopyToAsync(fs);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Could not save the dropped pack: " + ex.Message;
            return;
        }

        await AnalyzePackPathAsync(dest);
    }

    public async Task InstallPackReplaceAsync()
    {
        if (!CanInstallPack)
        {
            StatusMessage = PackReplaceUx.InstallDisabledReason(
                Vm1IsRunning,
                AnyBusy,
                PackCanContinue,
                PackConfirmed,
                ClientPackAcknowledged,
                PackIdentityComplete,
                PackFreezeBlockReason);
            return;
        }

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        if (string.IsNullOrWhiteSpace(PackPath) || !File.Exists(PackPath))
        {
            StatusMessage = "Could not resolve the pack file.";
            return;
        }

        var installPath = PackPath;
        if (PackNeedsIdentityConfirm)
        {
            if (!PackIdentityComplete)
            {
                StatusMessage = DerivedPackIdentity.IdentityIncompleteReason;
                return;
            }

            var source = string.IsNullOrWhiteSpace(PackSourcePath) ? PackPath : PackSourcePath;
            if (!File.Exists(source))
            {
                StatusMessage = "Original pack file is missing. Choose the pack again.";
                return;
            }

            var dataDir = _dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
            if (string.IsNullOrWhiteSpace(dataDir))
            {
                StatusMessage = "Could not find Manager data directory for the derived pack.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }
        }

        var confirmed = await _dialogs.ConfirmAsync(
            PackReplaceUx.ConfirmTitle,
            PackReplaceUx.ConfirmBody(WipeWorld),
            "Install this pack");
        if (!confirmed)
        {
            StatusMessage = "Change pack cancelled.";
            return;
        }

        IsBusy = true;
        _packReplaceRunning = true;
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        BeginPackElapsed();
        NotifyDock();
        try
        {
            if (PackNeedsIdentityConfirm)
            {
                StatusMessage = ProgressDockUx.ChangePackBuildFallback;
                NotifyDock();
                var source = string.IsNullOrWhiteSpace(PackSourcePath) ? PackPath : PackSourcePath;
                var dataDir = _dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
                var packName = PackName;
                var mc = PackMinecraftVersion;
                var loader = PackLoader;
                var loaderVersion = PackLoaderVersion;
                var javaMajor = PackJavaMajorText;
                var originalName = Path.GetFileName(source);
                var build = await Task.Run(() =>
                    DerivedPackWorkflow.BuildAndRetain(
                        source,
                        packName,
                        null,
                        mc,
                        loader,
                        loaderVersion,
                        javaMajor,
                        dataDir!,
                        originalName));
                if (!build.Succeeded || string.IsNullOrWhiteSpace(build.Value))
                {
                    StatusMessage = build.Error ?? "Could not build the derived pack.";
                    _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                    return;
                }

                installPath = build.Value;
                PackPath = installPath;
            }

            if (!Vm1IsRunning)
            {
                StatusMessage = ProgressDockUx.ChangePackStartFallback;
                NotifyDock();
                var started = await _main.EnsureVm1RunningForPackReplaceAsync();
                if (!started)
                {
                    StatusMessage = string.IsNullOrWhiteSpace(_main.ActionFeedback)
                        ? "Start failed. Pack was not installed."
                        : _main.ActionFeedback;
                    return;
                }
            }

            StatusMessage = ProgressDockUx.ChangePackIdleHoldFallback;
            NotifyDock();
            var idle = await _ssh.ApplyIdleSettingsAsync(
                _config.Vm1,
                idleAgentEnabled: false,
                _config.Budget.IdleTimeoutMinutes,
                _config.Budget.BudgetWarnMinutes);
            if (!idle.Succeeded)
            {
                StatusMessage = idle.Error ?? "Could not disable the idle timer. Pack was not installed.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            StatusMessage = ProgressDockUx.ChangePackInstallFallback;
            NotifyDock();

            var progress = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                StatusMessage = ProgressDockUx.HumanizeOrFallback(
                    line,
                    ProgressDockUx.ChangePackInstallFallback);
            });
            var result = await _bootstrap.ReplacePackAsync(
                _config.Vm1,
                new PackReplaceRequest(installPath, WipeWorld, _dataDirectory),
                progress);
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "Pack replace failed.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            var meta = await PublishGameMetaAfterReplaceAsync(result.Value);
            BindLocalPack();
            await RefreshMinecraftVersionAsync();
            StatusMessage = PackReplaceUx.SuccessMessage(result.Value);
            if (!meta.Succeeded)
            {
                StatusMessage += " Shared game info did not update: "
                    + (meta.Error ?? "publish failed.");
            }

            _banner.Show(
                string.IsNullOrWhiteSpace(result.Value.QuarantineNotice)
                    ? StatusMessage
                    : result.Value.QuarantineNotice,
                string.IsNullOrWhiteSpace(result.Value.QuarantineNotice)
                    ? ActionBannerSeverity.Success
                    : ActionBannerSeverity.Warning);
            ClearPackReplaceFields(hidePanel: true);
        }
        finally
        {
            PausePackElapsed();
            _packReplaceRunning = false;
            _forwardBanner = wasForward;
            IsBusy = false;
            NotifyDock();
        }
    }

    private async Task AnalyzePackPathAsync(string path, bool keepConfirm = false)
    {
        IsAnalyzingPack = true;
        PackAnalyzeCaption = ProgressDockUx.ChangePackAnalyzeFallback;
        PackBlockReason = "";
        PackSummary = "";
        PackOverrideListWarning = "";
        SaveCompatibilityWarning = "";
        PackCanContinue = false;
        if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }
        StatusMessage = ProgressDockUx.ChangePackAnalyzeFallback;
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        BeginPackElapsed();
        NotifyDock();
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
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            ApplyPackPreview(result.Value, keepConfirm);
            if (result.Value.CanContinue)
            {
                StatusMessage = result.Value.NeedsAssistedReview
                    ? "Review unknown jars, then confirm the pack."
                    : ProgressDockUx.ChangePackReviewStatus;
            }
            else
            {
                StatusMessage = result.Value.BlockReason ?? "This pack cannot be installed.";
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            _packPreview = null;
            PackCanContinue = false;
            PackConfirmed = false;
            PackBlockReason = "Analyze failed: " + ex.Message;
            StatusMessage = PackBlockReason;
            _banner.Show(StatusMessage, ActionBannerSeverity.Error);
        }
        finally
        {
            PausePackElapsed();
            IsAnalyzingPack = false;
            PackAnalyzeCaption = "";
            _forwardBanner = wasForward;
            NotifyDock();
        }
    }

    public Task OnAssistedSkipChanged(PackAssistedReviewActions.OperatorSkipChange change) =>
        SetOperatorSkipAsync(change.Path, change.Skip);

    public async Task SetOperatorSkipAsync(string path, bool skip)
    {
        if (_packPreview is null || AnyBusy)
            return;

        var result = PackAssistedReviewActions.ApplySkip(
            _packPreview,
            _operatorSkipTerms,
            path,
            skip,
            keepTerms: _operatorKeepTerms);
        if (result.NeedsReanalyze)
        {
            await AnalyzePackPathAsync(_packPreview.SourcePath, keepConfirm: true).ConfigureAwait(true);
            return;
        }

        ApplyReviewPreview(result.Preview);
        if (!PackReplaceUx.FreezeAllowsContinue(result.Preview.FreezeBlockReason))
            StatusMessage = result.Preview.FreezeBlockReason ?? "";
    }

    private void ApplyReviewPreview(SetupPackPreview preview)
    {
        _packPreview = preview;
        PackSummary = preview.ConfirmableSummary;
        PackOverrideListWarning = preview.OverrideListWarning ?? "";
        NotifyAssistedReviewUi();
        NotifyModdingCommands();
    }

    private void ApplyPackPreview(SetupPackPreview preview, bool keepConfirm = false)
    {
        _operatorSkipTerms = PackAssistedReviewActions.LoadPersistedSkipTerms(preview.SourcePath);
        _operatorKeepTerms = PackAssistedReviewActions.LoadPersistedKeepTerms(preview.SourcePath);
        PackLooksLikeLauncherInstance = SetupPackImport.LooksLikeLauncherInstance(preview.SourcePath);
        var bound = _operatorSkipTerms.Count > 0 || _operatorKeepTerms.Count > 0
            ? preview.ApplyOperatorSkips(_operatorSkipTerms, _operatorKeepTerms)
            : preview;
        _packPreview = bound;
        preview = bound;
        PackPath = preview.SourcePath;
        PackName = preview.PackName;
        PackMinecraftVersion = string.Equals(preview.MinecraftVersion, "(unknown)", StringComparison.OrdinalIgnoreCase)
            ? ""
            : preview.MinecraftVersion;
        PackLoader = preview.Loader;
        PackLoaderVersion = preview.LoaderVersion;
        PackNeedsIdentityConfirm = preview.NeedsIdentityConfirm;
        DetectedMinecraftVersion = preview.DetectedMinecraftVersion;
        DetectedLoader = preview.DetectedLoader;
        if (!preview.IsDerived)
            PackSourcePath = preview.SourcePath;
        PackJavaMajorText = preview.JavaMajor?.ToString() ?? "";
        _javaMajorCustomized = false;
        PackSummary = preview.ConfirmableSummary;
        PackOverrideListWarning = preview.OverrideListWarning ?? "";
        PackBlockReason = preview.BlockReason ?? "";
        PackCanContinue = preview.CanContinue;
        if (!keepConfirm)
        {
            PackConfirmed = false;
            ClientPackAcknowledged = false;
        }
        RefreshSaveCompatibilityWarning();
        NotifyModdingCommands();
        NotifyPackIdentityUi();
        NotifyAssistedReviewUi();
    }

    private void RefreshSaveCompatibilityWarning()
    {
        if (_packPreview is null || !_packPreview.CanContinue)
        {
            SaveCompatibilityWarning = "";
            return;
        }

        var warning = PackReplaceSaveCompatibility.Warn(
            _currentMinecraftVersion,
            _currentLoaderOrDistribution,
            PackMinecraftVersion,
            PackLoader);
        SaveCompatibilityWarning = PackReplaceUx.VisibleSaveCompatibilityWarning(WipeWorld, warning) ?? "";
        OnPropertyChanged(nameof(ShowSaveCompatibilityWarning));
        OnPropertyChanged(nameof(InstallPackTitle));
    }

    private void ClearPackReplaceFields(bool hidePanel)
    {
        _packPreview = null;
        _operatorSkipTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _operatorKeepTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        PackLooksLikeLauncherInstance = false;
        PackPath = "";
        PackName = "";
        PackMinecraftVersion = "";
        PackLoader = "";
        PackLoaderVersion = "";
        PackJavaMajorText = "";
        PackNeedsIdentityConfirm = false;
        PackSourcePath = "";
        DetectedMinecraftVersion = "";
        DetectedLoader = "";
        _javaMajorCustomized = false;
        PackSummary = "";
        PackBlockReason = "";
        PackOverrideListWarning = "";
        SaveCompatibilityWarning = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        WipeWorld = false;
        PackAnalyzeCaption = "";
        ResetPackElapsed();
        if (hidePanel)
            ShowChangePackUi = false;
        NotifyModdingCommands();
    }

    private async Task<ServiceResult> PublishGameMetaAfterReplaceAsync(PackReplaceResult result)
    {
        if (_infra is null || _config is null)
            return ServiceResult.Ok();

        var kind = PackReplaceUx.ServerKindForMeta(result.Loader);
        var published = await _infra.PublishFromLocalAsync(
            _config,
            serverKind: string.IsNullOrWhiteSpace(kind) ? null : kind,
            minecraftVersion: result.MinecraftVersion);
        if (!published.Succeeded)
            return ServiceResult.Fail(published.Error ?? "Could not update shared game info.");
        return ServiceResult.Ok();
    }

    private void BindLocalPack()
    {
        _localPack = ImportedPackArchiveStore.TryFindLatest(_dataDirectory);
        HasLocalPackArchive = _localPack is not null && File.Exists(_localPack.ArchivePath);
        PackIdentityDisplay = HasLocalPackArchive && _localPack is not null
            ? FormatPackIdentity(_localPack)
            : "";
        NotifyModdingCommands();
    }

    private void ApplyServerKind(string? serverKind)
    {
        _currentLoaderOrDistribution = string.IsNullOrWhiteSpace(serverKind) ? null : serverKind.Trim();
        IsModdedServer = ModdingPanelLogic.IsModdedServerKind(serverKind);
        if (!IsModdedServer)
        {
            ModFiles.Clear();
            QuarantinedMods.Clear();
            ModdingHint = ModdingPanelLogic.VanillaEmptyState;
        }

        NotifyModdingCommands();
    }

    private void ResetModdingState()
    {
        _localPack = null;
        _packPreview = null;
        _currentMinecraftVersion = null;
        _currentLoaderOrDistribution = null;
        IsModdedServer = false;
        HasLocalPackArchive = false;
        PackIdentityDisplay = "";
        ModdingSummary = "";
        ModdingHint = "";
        ModFiles.Clear();
        QuarantinedMods.Clear();
        ClearPackReplaceFields(hidePanel: true);
        NotifyModdingCommands();
    }

    private void NotifyModdingCommands()
    {
        OnPropertyChanged(nameof(AnyBusy));
        OnPropertyChanged(nameof(Vm1IsRunning));
        OnPropertyChanged(nameof(CanDownloadPack));
        OnPropertyChanged(nameof(DownloadPackTitle));
        OnPropertyChanged(nameof(CanPickPack));
        OnPropertyChanged(nameof(ChangePackTitle));
        OnPropertyChanged(nameof(CanInstallPack));
        OnPropertyChanged(nameof(InstallPackTitle));
        OnPropertyChanged(nameof(CanSaveIdentity));
        OnPropertyChanged(nameof(CanSaveSettings));
        OnPropertyChanged(nameof(CanClearIcon));
        OnPropertyChanged(nameof(HasQuarantinedMods));
        OnPropertyChanged(nameof(CanActOnQuarantine));
        NotifyPackIdentityUi();
        NotifyDock();
    }

    private void NotifyPackIdentityUi()
    {
        OnPropertyChanged(nameof(PackIdentityComplete));
        OnPropertyChanged(nameof(ShowPackIdentityFields));
        OnPropertyChanged(nameof(ShowDetectionMismatch));
        OnPropertyChanged(nameof(DetectionMismatchWarning));
        OnPropertyChanged(nameof(ShowPackConfirmChecks));
        OnPropertyChanged(nameof(ShowPackAssistedReview));
        OnPropertyChanged(nameof(ShowSkipListWarning));
    }

    private void NotifyAssistedReviewUi()
    {
        OnPropertyChanged(nameof(ShowPackAssistedReview));
        OnPropertyChanged(nameof(ShowSkipListWarning));
        OnPropertyChanged(nameof(AssistedReview));
        OnPropertyChanged(nameof(PackJarOrder));
        OnPropertyChanged(nameof(PackFreezeBlockReason));
        OnPropertyChanged(nameof(PackLooksLikeLauncherInstance));
        OnPropertyChanged(nameof(CanInstallPack));
        OnPropertyChanged(nameof(InstallPackTitle));
    }

    partial void OnPackMinecraftVersionChanged(string value)
    {
        if (PackNeedsIdentityConfirm && !_javaMajorCustomized)
        {
            _applyingJavaFloor = true;
            PackJavaMajorText = DerivedPackIdentity.JavaMajorForMinecraftOrNull(value)?.ToString() ?? "";
            _applyingJavaFloor = false;
        }
        RefreshSaveCompatibilityWarning();
        NotifyPackIdentityUi();
    }

    partial void OnPackLoaderChanged(string value)
    {
        RefreshSaveCompatibilityWarning();
        NotifyPackIdentityUi();
    }

    partial void OnPackLoaderVersionChanged(string value) => NotifyPackIdentityUi();

    partial void OnPackJavaMajorTextChanged(string value)
    {
        if (!_applyingJavaFloor)
            _javaMajorCustomized = true;
        NotifyPackIdentityUi();
    }

    partial void OnServerPaneChanged(string value) => NotifyDock();

    partial void OnShowChangePackUiChanged(bool value) => NotifyDock();

    partial void OnPackAnalyzeCaptionChanged(string value) => NotifyDock();

    private void NotifyDock()
    {
        OnPropertyChanged(nameof(IsChangePackPane));
        OnPropertyChanged(nameof(ShowPackJobProgress));
        OnPropertyChanged(nameof(ShowPackElapsed));
        OnPropertyChanged(nameof(PackElapsedDisplay));
        OnPropertyChanged(nameof(DockStatus));
    }

    private void BeginPackElapsed()
    {
        _packElapsedStarted = true;
        _packElapsedAccumulated = TimeSpan.Zero;
        _packElapsedRunningSince = _clock.UtcNow;
        StartPackElapsedTicker();
        NotifyDock();
    }

    private void PausePackElapsed()
    {
        if (_packElapsedRunningSince is DateTimeOffset start)
        {
            var next = _packElapsedAccumulated + (_clock.UtcNow - start);
            _packElapsedAccumulated = next < TimeSpan.Zero ? TimeSpan.Zero : next;
            _packElapsedRunningSince = null;
        }

        StopPackElapsedTicker();
        NotifyDock();
    }

    private void ResetPackElapsed()
    {
        StopPackElapsedTicker();
        _packElapsedStarted = false;
        _packElapsedAccumulated = TimeSpan.Zero;
        _packElapsedRunningSince = null;
        NotifyDock();
    }

    private void StartPackElapsedTicker()
    {
        if (_packElapsedCts is not null)
            return;
        _packElapsedCts = new CancellationTokenSource();
        _ = RunPackElapsedTickLoopAsync(_packElapsedCts.Token);
    }

    private void StopPackElapsedTicker()
    {
        _packElapsedCts?.Cancel();
        _packElapsedCts?.Dispose();
        _packElapsedCts = null;
    }

    private TimeSpan CurrentPackElapsed()
    {
        var value = _packElapsedAccumulated;
        if (_packElapsedRunningSince is DateTimeOffset start)
            value += _clock.UtcNow - start;
        return value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    private async Task RunPackElapsedTickLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = _clock.CreatePeriodicTimer(PackElapsedTickPeriod);
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await _dispatcher.InvokeAsync(NotifyDock, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        StopPackElapsedTicker();
        _session.Reloaded -= OnSessionReloaded;
        _main.PropertyChanged -= OnMainPropertyChanged;
    }

    partial void OnIsBusyChanged(bool value) => NotifyModdingCommands();

    partial void OnIsModdingBusyChanged(bool value) => NotifyModdingCommands();

    private static string FormatPackIdentity(ImportedPackArchiveInfo pack)
    {
        var bits = new List<string>();
        if (!string.IsNullOrWhiteSpace(pack.PackName))
            bits.Add(pack.PackName.Trim());
        var loader = ServerModsInspectResult.DisplayLoader(pack.Loader);
        if (!string.IsNullOrWhiteSpace(loader))
            bits.Add(loader);
        if (!string.IsNullOrWhiteSpace(pack.MinecraftVersion))
            bits.Add("Minecraft " + pack.MinecraftVersion.Trim());
        return bits.Count == 0 ? pack.SuggestedDownloadFileName : string.Join(" · ", bits);
    }

    public async Task RefreshOversizedFlagAsync()
    {
        if (_oversizedWorld is null)
        {
            ApplyOversizedRead(null);
            return;
        }

        var got = await _oversizedWorld.GetAsync();
        if (!got.Succeeded || got.Value is null)
            return;

        ApplyOversizedRead(got.Value);
    }

    private void ApplyOversizedRead(OversizedWorldBackupReadResult? read)
    {
        OversizedWorldBlocked = OversizedWorldBackupUx.IsBlocked(read);
        OversizedBanner = OversizedWorldBackupUx.Banner(read);
        OnPropertyChanged(nameof(DownloadLatestTitle));
        OnPropertyChanged(nameof(DownloadLatestButtonLabel));
        if (read is not null)
            OversizedWorldBackupUx.SyncBell(_notices, read);
    }

    private async Task DownloadLiveWorldViaSshAsync()
    {
        if (IsBusy)
            return;

        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        if (!OversizedWorldBackupUx.Vm1IsRunning(_main.Vm1Lifecycle))
        {
            StatusMessage = OversizedWorldBackupUx.StartVmFirstMessage;
            return;
        }

        var localPath = await _filePicker.SaveFileAsync(new FileSaveRequest
        {
            Title = "Download live world (SSH)",
            FileName = OversizedWorldBackupUx.SuggestedFileName(),
            DefaultExtension = "zip",
            Filters = [ZipFilter, AllFilesFilter],
        });

        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Copying the live world over SSH…";
        ProgressDisplay = "0 B";

        try
        {
            var progress = new Progress<long>(bytes =>
                ProgressDisplay = WorldBackupInfo.FormatSize(bytes));
            var result = await _ssh.DownloadLiveWorldZipAsync(_config.Vm1, localPath, progress);
            StatusMessage = result.Succeeded
                ? $"Saved live world to {localPath}. It was not uploaded to cloud storage."
                : result.Error ?? "SSH world download failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task DownloadToPickerAsync(WorldBackupInfo? backup)
    {
        if (_backups is null || IsBusy)
            return;

        if (backup is null)
        {
            StatusMessage = "No backup to download yet.";
            return;
        }

        var localPath = await _filePicker.SaveFileAsync(new FileSaveRequest
        {
            Title = "Download World Save",
            FileName = backup.FileName,
            DefaultExtension = "zip",
            Filters = [ZipFilter, AllFilesFilter],
        });

        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Downloading {backup.FileName}…";
        ProgressDisplay = "0 B";

        try
        {
            var progress = new Progress<long>(bytes =>
                ProgressDisplay = WorldBackupInfo.FormatSize(bytes));
            var result = await _backups.DownloadAsync(backup.ObjectName, localPath, progress);
            StatusMessage = result.Succeeded
                ? $"Downloaded to {localPath}"
                : result.Error ?? "Download failed.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshWithoutBusyGuardAsync()
    {
        if (_backups is null)
            return;

        var result = await _backups.ListWorldBackupsAsync();
        if (!result.Succeeded || result.Value is null)
            return;

        ApplyBackupList(result.Value, preserveSelection: true);
    }

    private void ApplyBackupList(IReadOnlyList<WorldBackupInfo> items, bool preserveSelection)
    {
        var previousName = preserveSelection ? SelectedBackup?.ObjectName : null;
        Backups.Clear();
        foreach (var item in items)
            Backups.Add(item);

        _currentBackupBytes = BackupStore.SumBackupBytes(items);
        if (_backups is not null)
        {
            SoftCapDisplay = _backups.FormatSoftCapLine(_currentBackupBytes);
            var usedGb = _currentBackupBytes / (1024d * 1024d * 1024d);
            BackupStorageDisplay = $"{usedGb:F1} / {_backups.SoftCapGb:0.#} GB";
        }

        LastBackupDisplay = Backups.Count == 0
            ? "—"
            : Backups[0].RelativeTimeDisplay;

        SelectedBackup = previousName is null
            ? null
            : Backups.FirstOrDefault(b =>
                string.Equals(b.ObjectName, previousName, StringComparison.Ordinal));
    }

    public async Task PickIconAsync()
    {
        var path = await _filePicker.OpenFileAsync(new FilePickRequest
        {
            Title = "Choose a PNG server icon",
            Filters = [PngFilter, AllFilesFilter],
        });
        if (string.IsNullOrWhiteSpace(path))
            return;
        if (!File.Exists(path))
        {
            IdentityStatus = "Could not read that icon file.";
            return;
        }

        var bytes = await File.ReadAllBytesAsync(path);
        var composed = ServerIconComposer.Compose(bytes);
        if (!composed.Succeeded || composed.Value is null)
        {
            IdentityStatus = composed.Error ?? "Could not use that PNG.";
            return;
        }

        _pendingIconPng = bytes;
        _clearIcon = false;
        ApplyIconSet(composed.Value);
        HasCustomIcon = true;
        IdentityStatus = "Icon selected. Save to store it.";
        OnPropertyChanged(nameof(CanClearIcon));
    }

    public void ClearIcon()
    {
        _pendingIconPng = null;
        _clearIcon = HasCustomIcon || !string.IsNullOrWhiteSpace(IconPreviewDataUrl);
        ApplyDefaultIconSet();
        HasCustomIcon = false;
        IdentityStatus = _clearIcon ? "Default icon will be used on Save." : "";
        OnPropertyChanged(nameof(CanClearIcon));
    }

    public async Task SaveSettingsAsync()
    {
        if (_settings is null || AnyBusy)
            return;

        IsBusy = true;
        SettingsStatus = "Saving…";
        try
        {
            var put = await _settings.PublishAsync(CollectSettingsProperties(), _currentMinecraftVersion);
            if (!put.Succeeded)
            {
                SettingsStatus = put.Error ?? "Save failed.";
                return;
            }

            BindSettingsDocument(put.Value!.Document, _currentMinecraftVersion);
            SettingsStatus =
                "Saved. Restart Minecraft (or Start) to apply these settings in-game.";
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(CanSaveSettings));
        }
    }

    private async Task LoadSettingsAsync()
    {
        ApplySettingsVersion(_currentMinecraftVersion);
        if (_settings is null)
        {
            ApplySettingsDefaults();
            return;
        }

        var got = await _settings.GetAsync();
        if (!got.Succeeded || got.Value is null)
        {
            SettingsStatus = got.Error ?? "Could not load server settings.";
            ApplySettingsDefaults();
            return;
        }

        BindSettingsDocument(got.Value.Document, _currentMinecraftVersion);
        SettingsStatus = got.Value.Present
            ? ""
            : "No saved settings yet — defaults are shown. Save to store them in the cloud.";
    }

    private void ApplySettingsVersion(string? minecraftVersion)
    {
        ShowSettingsPvp = ServerPropertiesCatalog.SupportsPvpProperty(minecraftVersion);
        ShowSettingsSimulationDistance = ServerPropertiesCatalog.SupportsSimulationDistance(minecraftVersion);
    }

    private void ApplySettingsDefaults()
    {
        var defaults = ServerPropertiesDocument.Defaults();
        BindSettingsDocument(defaults, _currentMinecraftVersion);
    }

    private void BindSettingsDocument(ServerPropertiesDocument document, string? minecraftVersion)
    {
        ApplySettingsVersion(minecraftVersion);
        var props = document.Properties ?? new Dictionary<string, string>(StringComparer.Ordinal);
        SettingsDifficulty = ReadEnum(props, ServerPropertiesCatalog.Difficulty, ServerPropertiesCatalog.Difficulties, "normal");
        SettingsGamemode = ReadEnum(props, ServerPropertiesCatalog.Gamemode, ServerPropertiesCatalog.Gamemodes, "survival");
        SettingsMaxPlayers = ReadInt(props, ServerPropertiesCatalog.MaxPlayers, 20, ServerPropertiesCatalog.MaxPlayersMin, ServerPropertiesCatalog.MaxPlayersMax);
        SettingsPvp = ReadBool(props, ServerPropertiesCatalog.Pvp, defaultValue: true);
        SettingsSpawnProtection = ReadInt(props, ServerPropertiesCatalog.SpawnProtection, 16, ServerPropertiesCatalog.SpawnProtectionMin, ServerPropertiesCatalog.SpawnProtectionMax);
        SettingsViewDistance = ReadInt(props, ServerPropertiesCatalog.ViewDistance, 10, ServerPropertiesCatalog.DistanceMin, ServerPropertiesCatalog.DistanceMax);
        SettingsSimulationDistance = ReadInt(props, ServerPropertiesCatalog.SimulationDistance, 10, ServerPropertiesCatalog.DistanceMin, ServerPropertiesCatalog.DistanceMax);
        SettingsHardcore = ReadBool(props, ServerPropertiesCatalog.Hardcore, defaultValue: false);
        SettingsForceGamemode = ReadBool(props, ServerPropertiesCatalog.ForceGamemode, defaultValue: false);
        SettingsAllowFlight = ReadBool(props, ServerPropertiesCatalog.AllowFlight, defaultValue: false);
    }

    private Dictionary<string, string> CollectSettingsProperties()
    {
        var props = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ServerPropertiesCatalog.Difficulty] = SettingsDifficulty,
            [ServerPropertiesCatalog.Gamemode] = SettingsGamemode,
            [ServerPropertiesCatalog.MaxPlayers] = SettingsMaxPlayers.ToString(),
            [ServerPropertiesCatalog.SpawnProtection] = SettingsSpawnProtection.ToString(),
            [ServerPropertiesCatalog.ViewDistance] = SettingsViewDistance.ToString(),
            [ServerPropertiesCatalog.Hardcore] = SettingsHardcore ? "true" : "false",
            [ServerPropertiesCatalog.ForceGamemode] = SettingsForceGamemode ? "true" : "false",
            [ServerPropertiesCatalog.AllowFlight] = SettingsAllowFlight ? "true" : "false",
        };
        if (ShowSettingsPvp)
            props[ServerPropertiesCatalog.Pvp] = SettingsPvp ? "true" : "false";
        if (ShowSettingsSimulationDistance)
            props[ServerPropertiesCatalog.SimulationDistance] = SettingsSimulationDistance.ToString();
        return props;
    }

    private static string ReadEnum(
        IReadOnlyDictionary<string, string> props,
        string key,
        IReadOnlyList<string> names,
        string fallback)
    {
        if (!props.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
            return fallback;
        foreach (var name in names)
        {
            if (string.Equals(name, raw.Trim(), StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return fallback;
    }

    private static int ReadInt(
        IReadOnlyDictionary<string, string> props,
        string key,
        int fallback,
        int min,
        int max)
    {
        if (!props.TryGetValue(key, out var raw) || !int.TryParse(raw, out var value))
            return fallback;
        if (value < min || value > max)
            return fallback;
        return value;
    }

    private static bool ReadBool(
        IReadOnlyDictionary<string, string> props,
        string key,
        bool defaultValue)
    {
        if (!props.TryGetValue(key, out var raw))
            return defaultValue;
        return string.Equals(raw.Trim(), "true", StringComparison.OrdinalIgnoreCase)
               || raw.Trim() == "1";
    }

    public async Task SaveIdentityAsync()
    {
        if (_chat is null || AnyBusy)
            return;

        IsBusy = true;
        IdentityStatus = "Saving…";
        try
        {
            var doc = ChatMessagesDocument.Defaults();
            doc.ServerName = MotdFormatting.ClipToListLine(IdentityName);
            doc.Description = MotdFormatting.ClipToListLine(IdentityDescription);
            doc.MotdOmitName = false;
            doc.ChatMessages = ChatTemplates
                .Where(row => !string.IsNullOrWhiteSpace(row.Key))
                .ToDictionary(row => row.Key, row => row.Text ?? "", StringComparer.Ordinal);

            var put = await _chat.PublishAsync(doc, _pendingIconPng, _clearIcon);
            if (!put.Succeeded)
            {
                IdentityStatus = put.Error ?? "Save failed.";
                return;
            }

            _pendingIconPng = null;
            _clearIcon = false;
            await LoadIdentityAsync();
            var doorNote = await TryRefreshDoorIconsAsync();
            IdentityStatus =
                "Saved. Restart Minecraft (or Start) to apply the in-game name and icon. "
                + (string.IsNullOrWhiteSpace(doorNote)
                    ? "Doorbell list icons update on Save."
                    : doorNote);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadIdentityAsync()
    {
        try
        {
            if (_chat is null)
            {
                FillDefaultChatRows();
                ApplyDefaultIconSet();
                return;
            }

            var got = await _chat.GetAsync();
            if (!got.Succeeded || got.Value is null)
            {
                IdentityStatus = got.Error ?? "Could not load server identity.";
                FillDefaultChatRows();
                ApplyDefaultIconSet();
                return;
            }

            var doc = got.Value.Document;
            IdentityName = MotdFormatting.ClipToListLine(doc.ServerName);
            IdentityDescription = MotdFormatting.ClipToListLine(doc.Description);
            ServerNameDisplay = ServerIdentityUx.DisplayName(IdentityName, _config?.Vm1.DisplayName);
            ApplyChatRows(doc.ChatMessages);
            _pendingIconPng = null;
            _clearIcon = false;
            ApplyIconSetFromPng(got.Value.IconPng);

            IdentityStatus = got.Value.Present
                ? ""
                : "No saved identity yet — defaults are shown. Save to create the shared file.";
        }
        finally
        {
            _identityLoaded = true;
        }
    }

    private async Task<string> TryRefreshDoorIconsAsync()
    {
        if (_cloud.Door is null)
            return "Could not reach the doorbell to load list icons; they apply on the next wake.";
        try
        {
            var refresh = await _cloud.Door.RefreshOsAsync();
            if (!refresh.Succeeded)
                return "Doorbell icon refresh failed: " + (refresh.Error ?? "unknown") + " They apply on the next wake.";
            return "Doorbell list icons updated.";
        }
        catch (Exception ex)
        {
            return "Doorbell icon refresh failed: " + ex.Message;
        }
    }

    private void FillDefaultChatRows() => ApplyChatRows(ServerIdentityUx.DefaultChatMessages);

    private void ApplyChatRows(IReadOnlyDictionary<string, string>? stored)
    {
        ChatTemplates.Clear();
        foreach (var field in ServerIdentityUx.ChatTemplateFields)
        {
            var text = "";
            if (stored is not null && stored.TryGetValue(field.Key, out var storedText)
                && !string.IsNullOrWhiteSpace(storedText))
            {
                text = storedText;
            }
            else if (ServerIdentityUx.DefaultChatMessages.TryGetValue(field.Key, out var fallback))
            {
                text = fallback;
            }

            ChatTemplates.Add(new ChatTemplateRow
            {
                Key = field.Key,
                Label = field.Label,
                Placeholders = field.Placeholders,
                Text = text,
            });
        }
    }

    private void ApplyIconSetFromPng(byte[]? png)
    {
        var composed = ServerIconComposer.Compose(png is { Length: > 0 } ? png : null);
        if (!composed.Succeeded || composed.Value is null)
        {
            ApplyDefaultIconSet();
            HasCustomIcon = png is { Length: > 0 };
            return;
        }

        ApplyIconSet(composed.Value);
        HasCustomIcon = png is { Length: > 0 };
    }

    private void ApplyDefaultIconSet()
    {
        var composed = ServerIconComposer.Compose();
        if (composed.Succeeded && composed.Value is not null)
            ApplyIconSet(composed.Value);
        else
            ApplyIconSet(null);
        HasCustomIcon = false;
    }

    private void ApplyIconSet(ServerIconSet? set)
    {
        if (set is null)
        {
            IconPreviewDataUrl = "";
            IdleIconPreviewDataUrl = "";
            StartingIconPreviewDataUrl = "";
            ExhaustedIconPreviewDataUrl = "";
            return;
        }

        IconPreviewDataUrl = set.ColorDataUrl;
        IdleIconPreviewDataUrl = set.IdleDataUrl;
        StartingIconPreviewDataUrl = set.StartingDataUrl;
        ExhaustedIconPreviewDataUrl = set.ExhaustedDataUrl;
    }
}

public sealed class ChatTemplateRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Placeholders { get; init; }
    public string Text { get; set; } = "";
}
