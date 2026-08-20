using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Console tab: RCON commands + recent Minecraft logs over SSH. Not a PTY.
/// Own <see cref="IsBusy"/> only — does not grey Start/Stop/Restart.
/// </summary>
public sealed partial class ConsoleViewModel : ObservableObject, IDisposable
{
    private const int LogSoftCap = 80_000;
    private const int LogTrimTo = 60_000;

    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly MainViewModel _main;
    private ManagerLocalConfig? _config;
    private ISshService _ssh = null!;
    private bool _disposed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRefresh))]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(SendTitle))]
    [NotifyPropertyChangedFor(nameof(RefreshTitle))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSend))]
    [NotifyPropertyChangedFor(nameof(SendTitle))]
    private string _commandText = "";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    private string _statusMessage = MinecraftConsoleRemote.Intro;

    public string HelpTitle => MinecraftConsoleRemote.HelpTitle;

    public bool Vm1IsRunning =>
        string.Equals(_main.Vm1Lifecycle, "RUNNING", StringComparison.OrdinalIgnoreCase);

    public bool MinecraftJoinable => _main.StatusIsRunning;

    public bool CanRefresh => MinecraftConsoleRemote.CanRefresh(Vm1IsRunning, IsBusy);

    public bool CanSend =>
        MinecraftConsoleRemote.CanSend(MinecraftJoinable, IsBusy, CommandText);

    public string RefreshTitle =>
        CanRefresh
            ? "Reload recent Minecraft logs from the server."
            : (Vm1IsRunning ? "Working…" : MinecraftConsoleRemote.VmStoppedHint);

    public string SendTitle
    {
        get
        {
            var reason = MinecraftConsoleRemote.SendDisabledReason(
                Vm1IsRunning,
                MinecraftJoinable,
                IsBusy,
                CommandText);
            return string.IsNullOrEmpty(reason)
                ? "Send this command to Minecraft."
                : reason;
        }
    }

    public ConsoleViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        MainViewModel main)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _main = main;

        BindFromHost();
        _session.Reloaded += OnSessionReloaded;
        _main.PropertyChanged += OnMainChanged;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _session.Reloaded -= OnSessionReloaded;
        _main.PropertyChanged -= OnMainChanged;
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void OnMainChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.Vm1Lifecycle)
            or nameof(MainViewModel.StatusIsRunning)
            or null)
        {
            OnPropertyChanged(nameof(Vm1IsRunning));
            OnPropertyChanged(nameof(MinecraftJoinable));
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(SendTitle));
            OnPropertyChanged(nameof(RefreshTitle));
        }
    }

    private void BindFromHost()
    {
        _config = _configHost.Config;
        _ssh = _cloud.Ssh;
        if (_config is null)
            StatusMessage = "Local config is missing.";
        else if (string.IsNullOrWhiteSpace(_config.Vm1.SshHost))
            StatusMessage = "No SSH host for the server.";
        else
            StatusMessage = MinecraftConsoleRemote.Intro;
        NotifyDerived();
    }

    private void NotifyDerived()
    {
        OnPropertyChanged(nameof(Vm1IsRunning));
        OnPropertyChanged(nameof(MinecraftJoinable));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(SendTitle));
        OnPropertyChanged(nameof(RefreshTitle));
    }

    public async Task RefreshLogsAsync()
    {
        if (IsBusy)
            return;
        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        if (!Vm1IsRunning)
        {
            if (string.IsNullOrWhiteSpace(LogText))
                LogText = "";
            StatusMessage = MinecraftConsoleRemote.VmStoppedHint;
            return;
        }

        IsBusy = true;
        StatusMessage = "Loading logs…";
        try
        {
            var run = await _ssh.FetchMinecraftLogsAsync(_config.Vm1).ConfigureAwait(true);
            if (!run.Succeeded)
            {
                StatusMessage = run.Error ?? "Could not load logs.";
                return;
            }

            var body = (run.Output ?? "").TrimEnd();
            LogText = string.IsNullOrEmpty(body) ? "" : TrimLog(body);
            StatusMessage = string.IsNullOrEmpty(body)
                ? MinecraftConsoleRemote.EmptyLogs
                : MinecraftConsoleRemote.Intro;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SendAsync()
    {
        if (IsBusy)
            return;
        if (_config is null)
        {
            StatusMessage = "Local config is missing.";
            return;
        }

        if (!MinecraftConsoleRemote.TryNormalizeCommand(CommandText, out var command, out var error))
        {
            StatusMessage = error ?? MinecraftConsoleRemote.EmptyCommandHint;
            return;
        }

        if (!MinecraftJoinable)
        {
            StatusMessage = MinecraftConsoleRemote.SendDisabledReason(
                Vm1IsRunning, MinecraftJoinable, busy: false, command);
            return;
        }

        IsBusy = true;
        StatusMessage = "Sending…";
        try
        {
            var run = await _ssh.SendMinecraftRconAsync(_config.Vm1, command).ConfigureAwait(true);
            var reply = run.Succeeded
                ? (run.Output ?? "").TrimEnd()
                : MinecraftConsoleRemote.OperatorHintFromRcon(run);
            AppendTranscript(MinecraftConsoleRemote.FormatTranscriptLine(command, reply));
            if (run.Succeeded)
            {
                CommandText = "";
                StatusMessage = MinecraftConsoleRemote.Intro;
            }
            else
            {
                StatusMessage = string.IsNullOrWhiteSpace(reply)
                    ? "Command failed."
                    : reply;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendTranscript(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
            return;
        if (string.IsNullOrWhiteSpace(LogText))
            LogText = block;
        else
            LogText = LogText.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
        LogText = TrimLog(LogText);
    }

    private static string TrimLog(string text)
    {
        if (text.Length <= LogSoftCap)
            return text;
        var cut = text.Length - LogTrimTo;
        var nl = text.IndexOf('\n', cut);
        return "…\n" + (nl >= 0 ? text[(nl + 1)..] : text[cut..]);
    }
}
