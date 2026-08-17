using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Setup;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

public enum DestroyInfrastructurePhase
{
    Confirm,
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// Danger Zone typed-confirm destroy. Stays open until tofu destroy returns
/// (OpenTofu waits for OCI resource deletion). Dry-run if MCMANAGER_TOFU_DRY_RUN=1.
/// </summary>
public sealed partial class DestroyInfrastructureViewModel : ObservableObject
{
    public const string ConfirmPhrase = InfrastructureDestroyOrchestrator.ConfirmPhrase;

    private static readonly TimeSpan LogFlushPeriod = TimeSpan.FromMilliseconds(250);

    private readonly LocalConfigHost _configHost;
    private readonly HybridShell _shell;
    private readonly MainViewModel _main;
    private readonly IUiClock _clock;
    private readonly IUiDispatcher _dispatcher;
    private readonly StringBuilder _logBuffer = new();
    private readonly object _logLock = new();
    private CancellationTokenSource? _logFlushCts;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDestroy))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(ShowConfirm))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    private bool _isOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDestroy))]
    [NotifyPropertyChangedFor(nameof(CanClose))]
    [NotifyPropertyChangedFor(nameof(ShowConfirm))]
    [NotifyPropertyChangedFor(nameof(ShowProgress))]
    [NotifyPropertyChangedFor(nameof(IsFinished))]
    [NotifyPropertyChangedFor(nameof(Title))]
    [NotifyPropertyChangedFor(nameof(CloseButtonText))]
    private DestroyInfrastructurePhase _phase = DestroyInfrastructurePhase.Confirm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStartDestroy))]
    private string _typedConfirm = "";

    [ObservableProperty]
    private string _logText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressPercentDisplay))]
    private double _progressPercent;

    [ObservableProperty]
    private string _progressCaption = "Waiting to start";

    [ObservableProperty]
    private string _resultMessage = "";

    public bool ShowConfirm => IsOpen && Phase == DestroyInfrastructurePhase.Confirm;

    public bool ShowProgress => IsOpen && Phase != DestroyInfrastructurePhase.Confirm;

    public bool IsFinished =>
        Phase is DestroyInfrastructurePhase.Succeeded or DestroyInfrastructurePhase.Failed;

    public bool CanStartDestroy =>
        Phase == DestroyInfrastructurePhase.Confirm
        && string.Equals(TypedConfirm.Trim(), ConfirmPhrase, StringComparison.Ordinal);

    public bool CanClose => Phase != DestroyInfrastructurePhase.Running;

    public string Title => Phase switch
    {
        DestroyInfrastructurePhase.Running => "Deleting infrastructure",
        DestroyInfrastructurePhase.Succeeded => "Infrastructure deleted",
        DestroyInfrastructurePhase.Failed => "Deletion failed",
        _ => "Delete all cloud infrastructure",
    };

    public string CloseButtonText => Phase == DestroyInfrastructurePhase.Succeeded
        ? "Close Manager and continue"
        : "Close";

    public string ProgressPercentDisplay => $"{(int)Math.Round(ProgressPercent)}%";

    public bool IsTofuDryRun { get; } = ProductPaths.IsTofuDryRun();

    public DestroyInfrastructureViewModel(
        LocalConfigHost configHost,
        HybridShell shell,
        MainViewModel main,
        IUiClock clock,
        IUiDispatcher dispatcher)
    {
        _configHost = configHost;
        _shell = shell;
        _main = main;
        _clock = clock;
        _dispatcher = dispatcher;
    }

    public void Open()
    {
        if (Phase == DestroyInfrastructurePhase.Running)
        {
            IsOpen = true;
            return;
        }

        Phase = DestroyInfrastructurePhase.Confirm;
        TypedConfirm = "";
        LogText = "";
        ProgressPercent = 0;
        ProgressCaption = "Waiting to start";
        ResultMessage = "";
        IsOpen = true;
    }

    public void Close()
    {
        if (!CanClose)
            return;

        IsOpen = false;
        if (Phase == DestroyInfrastructurePhase.Succeeded)
        {
            _main.StopChrome();
            _configHost.Reload();
            _shell.EnterFirstRun();
        }
    }

    public async Task StartDestroyAsync()
    {
        if (!CanStartDestroy)
            return;

        Phase = DestroyInfrastructurePhase.Running;
        ProgressPercent = 1;
        ProgressCaption = "Starting…";
        QueueLog(IsTofuDryRun
            ? "Dry-run: no Oracle resources will be deleted."
            : "Starting deletion. This window stays open until Oracle finishes.");
        StartLogFlushTimer();

        try
        {
            var log = new BufferedProgress(QueueLog);
            var progress = new Progress<DestroyProgressUpdate>(ApplyProgress);
            var orch = new InfrastructureDestroyOrchestrator();
            var result = await Task.Run(async () =>
                    await orch.RunAsync(log, CancellationToken.None, progress).ConfigureAwait(false))
                .ConfigureAwait(true);
            FlushLog();
            ResultMessage = result.Message;
            if (result.Succeeded)
            {
                Phase = DestroyInfrastructurePhase.Succeeded;
                ProgressPercent = 100;
                ProgressCaption = "Deletion finished";
                QueueLog(result.Message);
            }
            else
            {
                Phase = DestroyInfrastructurePhase.Failed;
                ProgressCaption = "Deletion failed";
                QueueLog(result.Message);
            }

            FlushLog();
        }
        catch (Exception ex)
        {
            Phase = DestroyInfrastructurePhase.Failed;
            ProgressCaption = "Deletion failed";
            ResultMessage = ex.Message;
            QueueLog(ex.ToString());
            FlushLog();
        }
        finally
        {
            StopLogFlushTimer();
        }
    }

    private void ApplyProgress(DestroyProgressUpdate update)
    {
        void Apply()
        {
            ProgressPercent = update.Percent;
            if (!string.IsNullOrWhiteSpace(update.Caption))
                ProgressCaption = update.Caption;
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _ = _dispatcher.InvokeAsync(Apply);
    }

    private void QueueLog(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        lock (_logLock)
        {
            foreach (var raw in line.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                _logBuffer.Append('[').Append(stamp).Append("] ").AppendLine(raw.TrimEnd());
            }
        }
    }

    private void FlushLog()
    {
        string chunk;
        lock (_logLock)
        {
            if (_logBuffer.Length == 0)
                return;
            chunk = _logBuffer.ToString();
            _logBuffer.Clear();
        }

        void Apply()
        {
            if (LogText.Length == 0)
                LogText = chunk.TrimEnd();
            else
                LogText += chunk;

            if (LogText.Length > 80_000)
                LogText = LogText[^60_000..];
        }

        if (_dispatcher.CheckAccess())
            Apply();
        else
            _ = _dispatcher.InvokeAsync(Apply);
    }

    private void StartLogFlushTimer()
    {
        if (_logFlushCts is not null)
            return;
        _logFlushCts = new CancellationTokenSource();
        _ = RunLogFlushLoopAsync(_logFlushCts.Token);
    }

    private void StopLogFlushTimer()
    {
        _logFlushCts?.Cancel();
        _logFlushCts?.Dispose();
        _logFlushCts = null;
        FlushLog();
    }

    private async Task RunLogFlushLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = _clock.CreatePeriodicTimer(LogFlushPeriod);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                FlushLog();
        }
        catch (OperationCanceledException)
        {
            // expected
        }
    }

    private sealed class BufferedProgress : IProgress<string>
    {
        private readonly Action<string> _append;

        public BufferedProgress(Action<string> append) => _append = append;

        public void Report(string value) => _append(value);
    }
}
