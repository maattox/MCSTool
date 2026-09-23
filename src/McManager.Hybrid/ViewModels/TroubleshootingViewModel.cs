using CommunityToolkit.Mvvm.ComponentModel;
using McManager.Core.Notifications;
using McManager.Core.Services;
using McManager.Hybrid.Ui;

namespace McManager.Hybrid.ViewModels;

/// <summary>
/// Troubleshooting tab: confirm-gated one-shots. Own IsBusy only — does not
/// grey manage-chrome Start/Stop/Restart.
/// </summary>
public sealed partial class TroubleshootingViewModel : ObservableObject
{
    private const int LogSoftCap = 80_000;
    private const int LogTrimTo = 60_000;
    private const string DefaultLog =
        "One-shot repairs. Output from SSH/OCI appears here — Copy and paste it into chat if something fails.";

    private TroubleshootingService? _service;
    private readonly LocalConfigHost _configHost;
    private readonly ManageCloudServices _cloud;
    private readonly ManageSession _session;
    private readonly IUiDialogs _dialogs;
    private readonly IClipboard _clipboard;
    private readonly ActionBanner _banner;
    private bool _forwardBanner;

    [ObservableProperty]
    private string _resultLog = DefaultLog;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage =
        "These fix common problems. They don't turn off the idle timer or budget limits (that's in Danger Zone).";

    public TroubleshootingViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiDialogs dialogs,
        IClipboard clipboard,
        ActionBanner banner)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;
        _banner = banner;

        BindFromHost();
        _forwardBanner = true;
        _session.Reloaded += OnSessionReloaded;
    }

    partial void OnStatusMessageChanged(string value)
    {
        if (!_forwardBanner)
            return;
        _banner.ShowInferred(value);
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void BindFromHost()
    {
        var wasForward = _forwardBanner;
        _forwardBanner = false;
        BindFromHostCore();
        _forwardBanner = wasForward;
    }

    private void BindFromHostCore()
    {
        _service = null;
        if (_configHost.Config is not null)
        {
            _service = new TroubleshootingService(
                _configHost.Config,
                _cloud.Ssh,
                _cloud.Compute,
                _cloud.Door);
            if (ResultLog.StartsWith("Troubleshooting service unavailable", StringComparison.Ordinal)
                || ResultLog == DefaultLog)
            {
                ResultLog = DefaultLog;
            }

            StatusMessage =
                "These fix common problems. They don't turn off the idle timer or budget limits (that's in Danger Zone).";
            return;
        }

        ResultLog = "Troubleshooting service unavailable (config/OCI/SSH).";
        StatusMessage = "Troubleshooting unavailable.";
    }

    public async Task ParkPlayIpAsync()
    {
        var confirmed = await ConfirmAsync(
            "Fix play IP?",
            "If the game VM is running, this moves the play IP to it. Otherwise it starts the doorbell VM "
            + "if needed and moves the play IP there.\n\n"
            + "If the play IP is already in the right place, nothing changes. This also fixes the doorbell "
            + "after the $1 spending limit stops both VMs. Continue?",
            "Fix play IP");
        if (!confirmed)
            return;
        await RunAsync("Fix play IP", s => s.ParkPlayIpAsync());
    }

    public Task DiagnoseWaitForgeAsync() =>
        RunAsync("Check doorbell wake", s => s.DiagnoseWaitForgeAsync());

    public async Task ResetDoorStateAsync()
    {
        var confirmed = await ConfirmAsync(
            "Reset doorbell?",
            "Resets the doorbell VM to idle. "
            + "Doesn't move the play IP — use Fix play IP if it's on the wrong VM. Continue?",
            "Reset");
        if (!confirmed)
            return;
        await RunAsync("Reset doorbell", s => s.ResetDoorStateAsync());
    }

    public async Task UnstickDoorAsync()
    {
        var confirmed = await ConfirmAsync(
            "Unstick doorbell after Minecraft is up?",
            "Checks and resets the doorbell VM, then wakes the server and waits until players can join. "
            + "Use this when Minecraft is already running but the doorbell VM is stuck. Continue?",
            "Unstick");
        if (!confirmed)
            return;
        await RunAsync("Unstick doorbell", s => s.UnstickAfterForgeReadyAsync());
    }

    public async Task RefreshOsBudgetAsync()
    {
        var confirmed = await ConfirmAsync(
            "Refresh doorbell hours?",
            "The doorbell VM reloads the budget and hours from cloud storage. "
            + "This doesn't start or stop anything. Continue?",
            "Refresh");
        if (!confirmed)
            return;
        await RunAsync("Refresh doorbell hours", s => s.RefreshOsBudgetAsync());
    }

    public async Task HealLedgerAsync()
    {
        var confirmed = await ConfirmAsync(
            "Fix hours record?",
            "Closes an unfinished entry in the hours record. "
            + "Only works while the game VM is fully Stopped (not Stopping). Continue?",
            "Fix");
        if (!confirmed)
            return;
        await RunAsync("Fix hours record", s => s.HealLedgerAsync());
    }

    public Task ShowIdleStatusAsync() =>
        RunAsync("Idle timer status", s => s.ShowIdleStatusAsync());

    public async Task ForceEnableIdleAsync()
    {
        var confirmed = await ConfirmAsync(
            "Turn idle timer back on?",
            "Turns the idle timer on without restarting the game VM. Doesn't start Minecraft.\n\n"
            + "The idle timer already turns back on every time the game VM boots or Minecraft starts. "
            + "Use this only if you turned it off for testing. Continue?",
            "Turn on");
        if (!confirmed)
            return;
        await RunAsync("Turn idle timer back on", s => s.ForceEnableIdleTimerAsync());
    }

    public async Task ReapplyNetplanAsync()
    {
        var confirmed = await ConfirmAsync(
            "Restore play IP network settings?",
            "Restores the play IP network settings on each running VM. "
            + "Stopped VMs are skipped. Continue?",
            "Restore");
        if (!confirmed)
            return;
        await RunAsync("Restore play IP network settings", s => s.ReapplyPlayNetplanAsync());
    }

    public async Task RepairPermissionsAsync()
    {
        var confirmed = await ConfirmAsync(
            "Repair game permissions?",
            "Resets folder owners and permissions on the game VM the way Setup set them. "
            + "Doesn't open them up to everyone and doesn't start Minecraft. Continue?",
            "Repair");
        if (!confirmed)
            return;
        await RunAsync("Repair game permissions", s => s.RepairGamePermissionsAsync());
    }

    public Task DiagnoseMinecraftAsync() =>
        RunAsync("Check Minecraft folder access", s => s.DiagnoseMinecraftChdirAsync());

    public async Task CopyResultLogAsync()
    {
        try
        {
            await _clipboard.SetTextAsync(
                string.IsNullOrWhiteSpace(ResultLog) ? "(empty)" : ResultLog);
            StatusMessage = "Copied result log.";
        }
        catch (Exception)
        {
            StatusMessage = "Clipboard unavailable.";
        }
    }

    private async Task RunAsync(string title, Func<TroubleshootingService, Task<TroubleshootingLogResult>> action)
    {
        if (IsBusy)
            return;

        if (_service is null)
        {
            AppendLog(title, "Troubleshooting service unavailable (config/OCI/SSH).");
            StatusMessage = "Troubleshooting unavailable.";
            return;
        }

        IsBusy = true;
        StatusMessage = title + "…";
        AppendHeader(title);

        try
        {
            var result = await action(_service);
            AppendBody(result.Log);
            StatusMessage = result.Summary;
        }
        catch (Exception ex)
        {
            AppendBody("ERROR: " + ex.Message);
            StatusMessage = title + " failed: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Task<bool> ConfirmAsync(string title, string message, string confirmButtonText) =>
        _dialogs.ConfirmAsync(title, message, confirmButtonText);

    private void AppendHeader(string title)
    {
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        var block = $"[{stamp}] {title}";
        if (string.IsNullOrWhiteSpace(ResultLog)
            || ResultLog.StartsWith("One-shot repairs.", StringComparison.Ordinal))
        {
            ResultLog = block;
            return;
        }

        ResultLog = ResultLog.TrimEnd() + Environment.NewLine + Environment.NewLine + block;
        TrimLog();
    }

    private void AppendBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return;
        ResultLog = ResultLog.TrimEnd() + Environment.NewLine + body.TrimEnd();
        TrimLog();
    }

    private void AppendLog(string title, string body)
    {
        AppendHeader(title);
        AppendBody(body);
    }

    private void TrimLog()
    {
        if (ResultLog.Length > LogSoftCap)
            ResultLog = ResultLog[^LogTrimTo..];
    }
}
