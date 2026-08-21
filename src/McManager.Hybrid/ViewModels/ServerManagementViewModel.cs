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
/// Server Management tab: Object Storage world backups + SSH replace/wipe when VM1 is RUNNING.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart or dispose <c>OciSession</c>.
/// </summary>
public sealed partial class ServerManagementViewModel : ObservableObject
{
    private static readonly FileTypeFilter ZipFilter = new("ZIP files", ".zip");
    private static readonly FileTypeFilter PngFilter = new("PNG images", ".png");
    private static readonly FileTypeFilter AllFilesFilter = new("All files", ".*");
    private static readonly FileTypeFilter PackFilter = new("Modpack archives", ".mrpack", ".zip");
    private static readonly FileTypeFilter MrpackFilter = new("Modrinth pack", ".mrpack");

    private ManagerLocalConfig? _config;
    private BackupStore? _backups;
    private InfraMetaStore? _infra;
    private OversizedWorldBackupStore? _oversizedWorld;
    private ChatMessagesStore? _chat;
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
    private bool _forwardBanner;
    private string? _currentMinecraftVersion;
    private string? _currentLoaderOrDistribution;
    private SetupPackPreview? _packPreview;
    private string? _sessionError;
    private long _currentBackupBytes;
    private string _dataDirectory = "";
    private ImportedPackArchiveInfo? _localPack;

    public ObservableCollection<WorldBackupInfo> Backups { get; } = [];

    public ObservableCollection<string> ModFiles { get; } = [];

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

    public bool CanPickPack => PackReplaceUx.CanPick(Vm1IsRunning, AnyBusy);

    public bool CanInstallPack =>
        PackReplaceUx.CanInstall(
            Vm1IsRunning,
            AnyBusy,
            PackCanContinue,
            PackConfirmed,
            ClientPackAcknowledged);

    public string ChangePackTitle =>
        CanPickPack
            ? "Reinstall Minecraft from a new .mrpack or server-pack zip. The world is kept unless you also wipe."
            : PackReplaceUx.PickDisabledReason(Vm1IsRunning, AnyBusy);

    public string InstallPackTitle =>
        CanInstallPack
            ? PackReplaceUx.ConfirmBody(WipeWorld)
            : PackReplaceUx.InstallDisabledReason(
                Vm1IsRunning,
                AnyBusy,
                PackCanContinue,
                PackConfirmed,
                ClientPackAcknowledged);

    public string PackFileNameDisplay =>
        string.IsNullOrWhiteSpace(PackPath) ? "" : Path.GetFileName(PackPath);

    public bool ShowPackSummary =>
        ShowChangePackUi
        && !IsAnalyzingPack
        && (!string.IsNullOrWhiteSpace(PackSummary) || !string.IsNullOrWhiteSpace(PackBlockReason));

    public bool ShowPackConfirmChecks => ShowPackSummary && PackCanContinue;

    public bool ShowOverrideListWarning =>
        ShowPackConfirmChecks && !string.IsNullOrWhiteSpace(PackOverrideListWarning);

    public bool ShowSaveCompatibilityWarning =>
        ShowPackConfirmChecks && !string.IsNullOrWhiteSpace(SaveCompatibilityWarning);

    public string ClientPackTitle => SetupPackImport.ClientPackTitle;

    public string ClientPackCopy => SetupPackImport.ClientPackCopy;

    public string ClientPackAckLabel => SetupPackImport.ClientPackAckLabel;

    public string ClientPackFriendsNeed =>
        SetupPackImport.FriendsNeedLine(PackName, PackMinecraftVersion, PackLoader, PackLoaderVersion);

    public string PackConfirmLabel => PackReplaceUx.PackConfirmLabel;

    public string WipeWorldLabel => PackReplaceUx.WipeWorldLabel;

    public string DownloadPackTitle =>
        CanDownloadPack
            ? "Save a copy of the original pack file imported in Setup (not a zip of server mods)."
            : ModdingPanelLogic.DownloadDisabledReason(IsModdedServer, HasLocalPackArchive);

    public string VanillaEmptyState => ModdingPanelLogic.VanillaEmptyState;

    public string MissingArchiveMessage => ModdingPanelLogic.MissingArchiveMessage;

    public string ModdingHelpTitle => ModdingPanelLogic.HelpTitle;

    public string DownloadLatestTitle =>
        OversizedWorldBackupUx.DownloadLatestTitle(
            OversizedWorldBlocked,
            OversizedWorldBackupUx.Vm1IsRunning(_main.Vm1Lifecycle));

    public string DownloadLatestButtonLabel =>
        OversizedWorldBackupUx.DownloadLatestButtonLabel(OversizedWorldBlocked);

    public string WorldBackupsHelpTitle => OversizedWorldBackupUx.HelpTitle;

    public string IdentityHelpTitle =>
        "Name and description show in Minecraft’s server list when the game is running (plain text, two lines). The 64×64 PNG is the list icon. Automated chat is what the idle timer says in-game before a stop. Save, then Restart Minecraft (or Start) to apply. The doorbell MOTD while the server is off is not edited here.";

    public string MotdPreview => ServerIdentityUx.BuildMotd(IdentityName, IdentityDescription);

    public bool CanSaveIdentity => HasObjectStorage && !AnyBusy;

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
    private bool _showChatTemplates;

    [ObservableProperty]
    private string _identityStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    [NotifyPropertyChangedFor(nameof(ShowPackConfirmChecks))]
    [NotifyPropertyChangedFor(nameof(ShowOverrideListWarning))]
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
    [NotifyPropertyChangedFor(nameof(ClientPackFriendsNeed))]
    private string _packName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientPackFriendsNeed))]
    private string _packMinecraftVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientPackFriendsNeed))]
    private string _packLoader = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ClientPackFriendsNeed))]
    private string _packLoaderVersion = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    private string _packSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowPackSummary))]
    private string _packBlockReason = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOverrideListWarning))]
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
        SetupBootstrapService bootstrap)
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

        BindFromHost();
        _forwardBanner = true;
        _session.Reloaded += OnSessionReloaded;
        _main.PropertyChanged += OnMainPropertyChanged;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!_forwardBanner)
            return;
        _banner.ShowInferred(value);
    }

    partial void OnIdentityStatusChanged(string value)
    {
        if (!_forwardBanner || string.IsNullOrWhiteSpace(value))
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
        BindFromHost();
        _ = RefreshMinecraftVersionAsync();
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
        _pendingIconPng = null;
        _clearIcon = false;
        IdentityName = "";
        IdentityDescription = "";
        HasCustomIcon = false;
        IconPreviewDataUrl = "";
        IdentityStatus = "";
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
        OnPropertyChanged(nameof(CanClearIcon));
        BindLocalPack();
        FillDefaultChatRows();
    }

    public async Task RefreshAsync()
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

        IsBusy = true;
        StatusMessage = "Listing backups…";
        ProgressDisplay = "";

        try
        {
            await RefreshOversizedFlagAsync();
            await LoadIdentityAsync();
            var result = await _backups.ListWorldBackupsAsync();
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "List failed.";
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

    public async Task RefreshMinecraftVersionAsync()
    {
        BindLocalPack();
        if (_infra is null)
        {
            _currentMinecraftVersion = null;
            MinecraftVersionDisplay = "—";
            ApplyServerKind(null);
            return;
        }

        var read = await _infra.GetAsync();
        if (!read.Succeeded || read.Value?.Document is null)
        {
            _currentMinecraftVersion = null;
            ApplyServerKind(null);
            return;
        }

        var doc = read.Value.Document;
        var version = doc.Game.MinecraftVersion;
        _currentMinecraftVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        MinecraftVersionDisplay = _currentMinecraftVersion ?? "—";
        ApplyServerKind(doc.Game.ServerKind);
        RefreshSaveCompatibilityWarning();
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
                ModdingSummary = "";
                ModdingHint = run.Error ?? "Could not list mods on the game VM.";
                return;
            }

            if (!ServerModsInspect.TryParse(run.Output, out var inspect, out var parseError))
            {
                ModFiles.Clear();
                ModdingSummary = "";
                ModdingHint = parseError ?? "Could not parse the mods listing.";
                return;
            }

            ModFiles.Clear();
            foreach (var name in inspect.FileNames)
                ModFiles.Add(name);
            ModdingSummary = inspect.SummaryLine();
            ModdingHint = inspect.ModsDirectoryMissing
                ? "No mods folder on the server yet."
                : (ModFiles.Count == 0 ? "No files in mods/." : "");
        }
        finally
        {
            IsModdingBusy = false;
        }
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
            StatusMessage = $"Saved original pack to {localPath}";
            ModdingHint = "This is the original imported file, not a zip of server mods.";
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
            Title = "Choose a modpack file (.mrpack or server-pack zip)",
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
                ClientPackAcknowledged);
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
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        StatusMessage = "Reinstalling Minecraft from this pack…";
        _banner.Show(StatusMessage, ActionBannerSeverity.Progress);
        try
        {
            var progress = new Progress<string>(line =>
            {
                if (string.IsNullOrWhiteSpace(line))
                    return;
                StatusMessage = line.Trim();
                _banner.Show(StatusMessage, ActionBannerSeverity.Progress);
            });
            var result = await _bootstrap.ReplacePackAsync(
                _config.Vm1,
                new PackReplaceRequest(PackPath, WipeWorld, _dataDirectory),
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

            _banner.Show(StatusMessage, ActionBannerSeverity.Success);
            ClearPackReplaceFields(hidePanel: true);
        }
        finally
        {
            _forwardBanner = wasForward;
            IsBusy = false;
        }
    }

    private async Task AnalyzePackPathAsync(string path)
    {
        IsAnalyzingPack = true;
        PackAnalyzeCaption = "Analyzing modpack…";
        PackBlockReason = "";
        PackSummary = "";
        PackOverrideListWarning = "";
        SaveCompatibilityWarning = "";
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
                _banner.Show(StatusMessage, ActionBannerSeverity.Error);
                return;
            }

            ApplyPackPreview(result.Value);
            if (result.Value.CanContinue)
            {
                StatusMessage = "Review the pack summary, then confirm before installing.";
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
            IsAnalyzingPack = false;
            PackAnalyzeCaption = "";
        }
    }

    private void ApplyPackPreview(SetupPackPreview preview)
    {
        _packPreview = preview;
        PackPath = preview.SourcePath;
        PackName = preview.PackName;
        PackMinecraftVersion = preview.MinecraftVersion;
        PackLoader = preview.Loader;
        PackLoaderVersion = preview.LoaderVersion;
        PackSummary = preview.ConfirmableSummary;
        PackOverrideListWarning = preview.OverrideListWarning ?? "";
        PackBlockReason = preview.BlockReason ?? "";
        PackCanContinue = preview.CanContinue;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        RefreshSaveCompatibilityWarning();
        NotifyModdingCommands();
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
            _packPreview.MinecraftVersion,
            _packPreview.Loader);
        SaveCompatibilityWarning = PackReplaceUx.VisibleSaveCompatibilityWarning(WipeWorld, warning) ?? "";
        OnPropertyChanged(nameof(ShowSaveCompatibilityWarning));
        OnPropertyChanged(nameof(InstallPackTitle));
    }

    private void ClearPackReplaceFields(bool hidePanel)
    {
        _packPreview = null;
        PackPath = "";
        PackName = "";
        PackMinecraftVersion = "";
        PackLoader = "";
        PackLoaderVersion = "";
        PackSummary = "";
        PackBlockReason = "";
        PackOverrideListWarning = "";
        SaveCompatibilityWarning = "";
        PackCanContinue = false;
        PackConfirmed = false;
        ClientPackAcknowledged = false;
        WipeWorld = false;
        PackAnalyzeCaption = "";
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
            ModdingSummary = "";
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
        OnPropertyChanged(nameof(CanClearIcon));
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
            Title = "Choose a 64×64 PNG server icon",
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
        var error = ServerIdentityUx.ValidateIcon(bytes);
        if (error is not null)
        {
            IdentityStatus = error;
            return;
        }

        _pendingIconPng = bytes;
        _clearIcon = false;
        ApplyIconPreview(bytes);
        IdentityStatus = "Icon selected. Save to store it.";
        OnPropertyChanged(nameof(CanClearIcon));
    }

    public void ClearIcon()
    {
        _pendingIconPng = null;
        _clearIcon = HasCustomIcon || !string.IsNullOrWhiteSpace(IconPreviewDataUrl);
        ApplyIconPreview(null);
        IdentityStatus = _clearIcon ? "Icon will be removed on Save." : "";
        OnPropertyChanged(nameof(CanClearIcon));
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
            doc.ServerName = IdentityName?.Trim() ?? "";
            doc.Description = IdentityDescription?.Trim() ?? "";
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
            IdentityStatus =
                "Saved. Restart Minecraft (or Start) to apply the name, icon, and chat lines.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadIdentityAsync()
    {
        if (_chat is null)
        {
            FillDefaultChatRows();
            return;
        }

        var got = await _chat.GetAsync();
        if (!got.Succeeded || got.Value is null)
        {
            IdentityStatus = got.Error ?? "Could not load server identity.";
            FillDefaultChatRows();
            return;
        }

        var doc = got.Value.Document;
        IdentityName = doc.ServerName ?? "";
        IdentityDescription = doc.Description ?? "";
        ServerNameDisplay = ServerIdentityUx.DisplayName(IdentityName, _config?.Vm1.DisplayName);
        ApplyChatRows(doc.ChatMessages);
        _pendingIconPng = null;
        _clearIcon = false;
        ApplyIconPreview(got.Value.IconPng is { Length: > 0 } ? got.Value.IconPng : null);

        IdentityStatus = got.Value.Present
            ? ""
            : "No saved identity yet — defaults are shown. Save to create the shared file.";
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

    private void ApplyIconPreview(byte[]? png)
    {
        if (png is { Length: > 0 })
        {
            HasCustomIcon = true;
            IconPreviewDataUrl = "data:image/png;base64," + Convert.ToBase64String(png);
        }
        else
        {
            HasCustomIcon = false;
            IconPreviewDataUrl = "";
        }
    }
}

public sealed class ChatTemplateRow
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Placeholders { get; init; }
    public string Text { get; set; } = "";
}
