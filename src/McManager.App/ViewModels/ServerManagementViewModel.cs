using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using McManager.App.Dialogs;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.App.ViewModels;

public partial class ServerManagementViewModel : ViewModelBase
{
    private static readonly FilePickerFileType ZipFileType = new("ZIP files")
    {
        Patterns = ["*.zip"],
        AppleUniformTypeIdentifiers = ["public.zip-archive"],
        MimeTypes = ["application/zip"],
    };

    private readonly ManagerLocalConfig _config;
    private readonly BackupStore? _backups;
    private readonly ISshService _ssh;
    private readonly Func<string> _getVm1Lifecycle;
    private readonly Action<bool>? _setBusy;
    private long _currentBackupBytes;

    public ObservableCollection<WorldBackupInfo> Backups { get; } = [];

    [ObservableProperty]
    private WorldBackupInfo? _selectedBackup;

    [ObservableProperty]
    private string _statusMessage = "Open this tab to list Object Storage world backups.";

    [ObservableProperty]
    private string _softCapDisplay = "—";

    [ObservableProperty]
    private string _progressDisplay = "";

    [ObservableProperty]
    private bool _isBusy;

    public bool HasObjectStorage => _backups is not null;

    public ServerManagementViewModel(
        ManagerLocalConfig config,
        BackupStore? backups,
        ISshService ssh,
        Func<string> getVm1Lifecycle,
        Action<bool>? setBusy = null)
    {
        _config = config;
        _backups = backups;
        _ssh = ssh;
        _getVm1Lifecycle = getVm1Lifecycle;
        _setBusy = setBusy;

        if (_backups is not null)
            SoftCapDisplay = _backups.FormatSoftCapLine(0);
    }

    public void OnTabSelected(bool selected)
    {
        if (selected)
            _ = RefreshAsync();
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_backups is null)
        {
            StatusMessage = "Object Storage is not configured / OCI session unavailable.";
            return;
        }

        if (IsBusy)
            return;

        IsBusy = true;
        _setBusy?.Invoke(true);
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
                ? "No world-*.zip backups in Object Storage."
                : $"Listed {Backups.Count} backup(s). Select one to download.";
        }
        finally
        {
            IsBusy = false;
            _setBusy?.Invoke(false);
        }
    }

    [RelayCommand]
    private async Task DownloadAsync()
    {
        if (_backups is null || IsBusy)
            return;

        if (SelectedBackup is null)
        {
            StatusMessage = "Select a backup in the list before downloading.";
            return;
        }

        var backup = SelectedBackup;
        var window = GetMainWindow();
        if (window is null)
        {
            StatusMessage = "No window for file picker.";
            return;
        }

        var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Download World Save",
            SuggestedFileName = backup.FileName,
            DefaultExtension = "zip",
            FileTypeChoices =
            [
                ZipFileType,
                new FilePickerFileType("All files") { Patterns = ["*.*"] },
            ],
        });

        if (file is null)
        {
            StatusMessage = "Download cancelled.";
            return;
        }

        var localPath = file.TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath))
        {
            StatusMessage = "Could not resolve local save path.";
            return;
        }

        IsBusy = true;
        _setBusy?.Invoke(true);
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
            _setBusy?.Invoke(false);
        }
    }

    [RelayCommand]
    private async Task UploadAsync()
    {
        if (_backups is null || IsBusy)
            return;

        var window = GetMainWindow();
        if (window is null)
        {
            StatusMessage = "No window for file picker.";
            return;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Upload world zip to Object Storage",
            AllowMultiple = false,
            FileTypeFilter = [ZipFileType],
        });

        if (files.Count == 0)
        {
            StatusMessage = "Upload cancelled.";
            return;
        }

        var localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
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

        var confirmed = await ConfirmDialog.ShowAsync(
            window,
            "Upload backup?",
            $"Upload {Path.GetFileName(localPath)} ({WorldBackupInfo.FormatSize(zipBytes)}) "
            + "to Object Storage as a new backups/world-*.zip?",
            confirmButtonText: "Upload");
        if (!confirmed)
        {
            StatusMessage = "Upload cancelled.";
            return;
        }

        IsBusy = true;
        _setBusy?.Invoke(true);
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
            _setBusy?.Invoke(false);
        }
    }

    [RelayCommand]
    private async Task ReplaceWorldAsync()
    {
        if (IsBusy)
            return;

        var life = (_getVm1Lifecycle() ?? "").ToUpperInvariant();
        if (life != "RUNNING")
        {
            StatusMessage =
                $"VM1 is '{_getVm1Lifecycle()}' — Replace requires RUNNING. "
                + "You can Upload a zip to Object Storage while stopped, then Start and Replace.";
            return;
        }

        var window = GetMainWindow();
        if (window is null)
        {
            StatusMessage = "No window for file picker.";
            return;
        }

        var files = await window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Replace VM1 world from local zip",
            AllowMultiple = false,
            FileTypeFilter = [ZipFileType],
        });

        if (files.Count == 0)
        {
            StatusMessage = "Replace cancelled.";
            return;
        }

        var localPath = files[0].TryGetLocalPath();
        if (string.IsNullOrWhiteSpace(localPath) || !File.Exists(localPath))
        {
            StatusMessage = "Could not resolve local zip path.";
            return;
        }

        var worldPath = _config.Vm1.WorldPath;
        var confirmed = await ConfirmDialog.ShowAsync(
            window,
            "Replace world on VM1?",
            "This STOPS Minecraft, replaces the world at "
            + $"{worldPath} (previous folder moved aside as .bak.*), then starts Minecraft again. "
            + "Zip contents must be world-folder relative (same as SoftStop backups). Continue?",
            confirmButtonText: "Replace");
        if (!confirmed)
        {
            StatusMessage = "Replace cancelled.";
            return;
        }

        IsBusy = true;
        _setBusy?.Invoke(true);
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
            _setBusy?.Invoke(false);
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
            SoftCapDisplay = _backups.FormatSoftCapLine(_currentBackupBytes);

        SelectedBackup = previousName is null
            ? null
            : Backups.FirstOrDefault(b =>
                string.Equals(b.ObjectName, previousName, StringComparison.Ordinal));
    }

    private static Window? GetMainWindow() =>
        (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)
        ?.MainWindow;
}
