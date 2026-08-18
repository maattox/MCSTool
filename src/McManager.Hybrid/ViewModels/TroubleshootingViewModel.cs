using CommunityToolkit.Mvvm.ComponentModel;
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

    [ObservableProperty]
    private string _resultLog = DefaultLog;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage =
        "These actions repair the doorbell. They do not disable $0 idle/budget brakes (that is Danger Zone).";

    public TroubleshootingViewModel(
        LocalConfigHost configHost,
        ManageCloudServices cloud,
        ManageSession session,
        IUiDialogs dialogs,
        IClipboard clipboard)
    {
        _configHost = configHost;
        _cloud = cloud;
        _session = session;
        _dialogs = dialogs;
        _clipboard = clipboard;

        BindFromHost();
        _session.Reloaded += OnSessionReloaded;
    }

    private void OnSessionReloaded(object? sender, EventArgs e) => BindFromHost();

    private void BindFromHost()
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
                "These actions repair the doorbell. They do not disable $0 idle/budget brakes (that is Danger Zone).";
            return;
        }

        ResultLog = "Troubleshooting service unavailable (config/OCI/SSH).";
        StatusMessage = "Troubleshooting unavailable.";
    }

    public async Task ParkPlayIpAsync()
    {
        var confirmed = await ConfirmAsync(
            "Park reserved play IP?",
            "If VM1 is RUNNING, this assigns the reserved public play IP to VM1’s secondary "
            + "(via door ip_to_vm1.sh). Otherwise it starts the door if needed and assigns the IP "
            + "to the door secondary (ip_to_vm2.sh).\n\n"
            + "Already-on-target is success. This recovers a stuck doorbell after the $1 Function "
            + "SoftStops both VMs (FN-ISSUE-1). Continue?",
            "Park IP");
        if (!confirmed)
            return;
        await RunAsync("Park play IP", s => s.ParkPlayIpAsync());
    }

    public Task DiagnoseWaitForgeAsync() =>
        RunAsync("Diagnose wait_forge", s => s.DiagnoseWaitForgeAsync());

    public async Task ResetDoorStateAsync()
    {
        var confirmed = await ConfirmAsync(
            "Reset door state?",
            "Stops mccontrol, sets door_state=DOOR_IDLE, starts the unit. "
            + "Does not move the reserved play IP — use Park play IP if the IP is on the wrong VM. Continue?",
            "Reset door");
        if (!confirmed)
            return;
        await RunAsync("Reset door state", s => s.ResetDoorStateAsync());
    }

    public async Task UnstickDoorAsync()
    {
        var confirmed = await ConfirmAsync(
            "Unstick door after Minecraft is up?",
            "Runs diagnose → reset → POST /api/wake and waits for PLAYABLE. "
            + "Use this when Minecraft is already listening but the door is stuck STARTING/DEGRADED. Continue?",
            "Unstick");
        if (!confirmed)
            return;
        await RunAsync("Unstick door", s => s.UnstickAfterForgeReadyAsync());
    }

    public async Task RefreshOsBudgetAsync()
    {
        var confirmed = await ConfirmAsync(
            "Refresh door OS budget cache?",
            "POST /api/os-refresh (or SSH pull_os_budget.sh --force) re-reads Object Storage. "
            + "Does not start or stop VMs. Continue?",
            "Refresh");
        if (!confirmed)
            return;
        await RunAsync("Refresh OS budget", s => s.RefreshOsBudgetAsync());
    }

    public async Task HealLedgerAsync()
    {
        var confirmed = await ConfirmAsync(
            "Heal open ledger?",
            "Runs heal_os_ledger.sh on the door. The Manager refuses unless VM1 is STOPPED "
            + "(Phase 5: not STOPPING). Continue?",
            "Heal");
        if (!confirmed)
            return;
        await RunAsync("Heal ledger", s => s.HealLedgerAsync());
    }

    public Task ShowIdleStatusAsync() =>
        RunAsync("Idle timer status", s => s.ShowIdleStatusAsync());

    public async Task ForceEnableIdleAsync()
    {
        var confirmed = await ConfirmAsync(
            "Force-enable idle timer?",
            "Enables and starts mc-idle-watch.timer on VM1. Does not start Minecraft.\n\n"
            + "OS-ISSUE-7: every VM1 boot / Minecraft start already FORCE-ENABLES idle. "
            + "Use this only if you disabled the timer for testing and want it back without rebooting. Continue?",
            "Enable idle");
        if (!confirmed)
            return;
        await RunAsync("Force-enable idle timer", s => s.ForceEnableIdleTimerAsync());
    }

    public async Task ReapplyNetplanAsync()
    {
        var confirmed = await ConfirmAsync(
            "Re-apply play netplan?",
            "Writes /etc/netplan/99-mcmgr-play.yaml and runs netplan apply on each RUNNING guest "
            + "(VM1 and/or door) using the secondary private IP from local config. "
            + "STOPPED VMs are skipped. Continue?",
            "Apply netplan");
        if (!confirmed)
            return;
        await RunAsync("Re-apply play netplan", s => s.ReapplyPlayNetplanAsync());
    }

    public async Task RepairPermissionsAsync()
    {
        var confirmed = await ConfirmAsync(
            "Repair game tree permissions?",
            "Runs the Step 4.2 layout contract (repair-permissions.sh): root:mcmgr 0750 on /opt/mcmgr, "
            + "mcmgr:mcmgr 0750 on server/. Does not chmod 0777 and does not start Minecraft. Continue?",
            "Repair");
        if (!confirmed)
            return;
        await RunAsync("Repair game permissions", s => s.RepairGamePermissionsAsync());
    }

    public Task DiagnoseMinecraftAsync() =>
        RunAsync("Minecraft CHDIR diagnosis", s => s.DiagnoseMinecraftChdirAsync());

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
