using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Program settings (gear). Bell list is <see cref="NotificationCenterViewModel"/>.
/// </summary>
public sealed partial class ChromeViewModel : ObservableObject
{
    public const string GitHubUrl = ProgramPaths.GitHubUrl;

    private readonly LocalConfigHost _configHost;
    private readonly IClipboard _clipboard;
    private readonly IShell _shell;
    private readonly ManageSession _session;
    private readonly string _settingsPath;
    private bool _loading = true;
    private CancellationTokenSource? _copyCts;

    public ChromeViewModel(
        LocalConfigHost configHost,
        IClipboard clipboard,
        IShell shell,
        ManageSession session)
    {
        _configHost = configHost;
        _clipboard = clipboard;
        _shell = shell;
        _session = session;
        _settingsPath = AppSettingsStore.DefaultFilePath();
        var settings = AppSettingsStore.Load(_settingsPath);
        CheckForUpdates = settings.CheckForUpdates;
        AppVersion = ReadAppVersion();
        RefreshPaths();
        _session.Reloaded += OnSessionReloaded;
        _loading = false;
    }

    public string AppName { get; } = "MCSTool";

    public string Tagline { get; } = "Automated Minecraft server deployment and management tool";

    public string ContactEmail { get; } = "mcstool.contact@gmail.com";

    public string AppVersion { get; }

    [ObservableProperty]
    private bool _settingsOpen;

    [ObservableProperty]
    private bool _checkForUpdates = true;

    [ObservableProperty]
    private string _saveError = "";

    [ObservableProperty]
    private string _copyFeedback = "";

    [ObservableProperty]
    private IReadOnlyList<ProgramPathItem> _paths = [];

    public string? ConfigDirOverride => ProgramPaths.ConfigDirOverride;

    public bool HasConfigDirOverride => !string.IsNullOrWhiteSpace(ConfigDirOverride);

    public string ActiveServerLabel => ServerCatalog.CaptionLabel(_configHost.PlayIp);

    public bool ShowCaptionServerSelect => ServerCatalog.ShowCaptionSwitcher;

    public void RefreshServerLabel()
    {
        OnPropertyChanged(nameof(ActiveServerLabel));
        OnPropertyChanged(nameof(ShowCaptionServerSelect));
        RefreshPaths();
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => RefreshServerLabel();

    public void OpenSettings()
    {
        RefreshPaths();
        CopyFeedback = "";
        SettingsOpen = true;
    }

    public void CloseSettings() => SettingsOpen = false;

    public void ClosePanels() => SettingsOpen = false;

    public void OpenGitHub() => _shell.OpenUrl(GitHubUrl);

    public async Task CopyPathAsync(string id)
    {
        var row = Paths.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (row is null || string.IsNullOrWhiteSpace(row.Path))
            return;

        await _clipboard.SetTextAsync(row.Path);
        CopyFeedback = "Copied " + row.Label.ToLowerInvariant() + ".";
        _copyCts?.Cancel();
        _copyCts = new CancellationTokenSource();
        var token = _copyCts.Token;
        try
        {
            await Task.Delay(1800, token);
            if (!token.IsCancellationRequested)
                CopyFeedback = "";
        }
        catch (TaskCanceledException)
        {
            // a later copy replaced this toast
        }
    }

    public void OpenPath(string id)
    {
        var row = Paths.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.Ordinal));
        if (row is null || string.IsNullOrWhiteSpace(row.Path))
            return;
        _shell.OpenPath(row.Path);
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (_loading)
            return;

        var doc = AppSettingsStore.Load(_settingsPath);
        doc.CheckForUpdates = value;
        var result = AppSettingsStore.Save(doc, _settingsPath);
        SaveError = result.Succeeded ? "" : (result.Error ?? "Could not save program settings.");
    }

    private void RefreshPaths()
    {
        var oci = _configHost.Config?.Oci.ConfigFile;
        Paths = ProgramPaths.Describe(_configHost.LoadResult.DataDirectory, oci);
    }

    private static string ReadAppVersion()
    {
        var asm = typeof(App).Assembly;
        var informational = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            var plus = informational.IndexOf('+', StringComparison.Ordinal);
            return plus > 0 ? informational[..plus] : informational;
        }

        return asm.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
