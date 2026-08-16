using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Server Management tab: Object Storage world backups + SSH replace when VM1 is RUNNING.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart or dispose <c>OciSession</c>.
/// </summary>
public sealed partial class ServerManagementViewModel : ObservableObject
{
    private static readonly FileTypeFilter ZipFilter = new("ZIP files", ".zip");
    private static readonly FileTypeFilter AllFilesFilter = new("All files", ".*");

    private readonly ManagerLocalConfig? _config;
    private readonly BackupStore? _backups;
    private readonly InfraMetaStore? _infra;
    private readonly ISshService _ssh;
    private readonly IFilePicker _filePicker;
    private readonly IUiDialogs _dialogs;
    private readonly MainViewModel _main;
    private readonly string? _sessionError;
    private long _currentBackupBytes;

    public ObservableCollection<WorldBackupInfo> Backups { get; } = [];

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

    public bool HasObjectStorage => _backups is not null;

    public ServerManagementViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        IFilePicker filePicker,
        IUiDialogs dialogs,
        MainViewModel main)
    {
        _config = configHost.Config;
        _ssh = cloud.Ssh;
        _filePicker = filePicker;
        _dialogs = dialogs;
        _main = main;
        _sessionError = cloud.SessionError;

        if (_config is not null)
        {
            ServerNameDisplay = string.IsNullOrWhiteSpace(_config.Vm1.DisplayName)
                ? "—"
                : _config.Vm1.DisplayName.Trim();
        }

        if (_config is not null && cloud.Session is not null)
        {
            var os = new ObjectStorageService(cloud.Session, _config.ObjectStorage);
            _backups = new BackupStore(os, _config.ObjectStorage);
            _infra = new InfraMetaStore(os, _config.ObjectStorage.Prefixes);
            SoftCapDisplay = _backups.FormatSoftCapLine(0);
            BackupStorageDisplay = $"0.0 / {_backups.SoftCapGb:0.#} GB";
        }
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
            var result = await _backups.ListWorldBackupsAsync();
            if (!result.Succeeded || result.Value is null)
            {
                StatusMessage = result.Error ?? "List failed.";
                return;
            }

            ApplyBackupList(result.Value, preserveSelection: true);
            StatusMessage = Backups.Count == 0
                ? "No world backups stored yet."
                : $"Listed {Backups.Count} backup(s). Select one to download.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task DownloadSelectedAsync() => DownloadToPickerAsync(SelectedBackup);

    public Task DownloadBackupAsync(WorldBackupInfo? backup) => DownloadToPickerAsync(backup);

    public Task DownloadLatestAsync() => DownloadToPickerAsync(Backups.FirstOrDefault());

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

    public async Task RefreshMinecraftVersionAsync()
    {
        if (_infra is null)
            return;

        var read = await _infra.GetAsync();
        if (!read.Succeeded || read.Value?.Document is null)
            return;

        var version = read.Value.Document.Game.MinecraftVersion;
        MinecraftVersionDisplay = string.IsNullOrWhiteSpace(version) ? "—" : version.Trim();
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
}
