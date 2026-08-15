using System.Text;
using McManager.Core.Config;
using McManager.Core.Onbox;
using McManager.Core.Setup;

namespace McManager.Core.Services;

public sealed class TroubleshootingLogResult
{
    public bool Succeeded { get; init; }
    public string Log { get; init; } = "";
    public string Summary { get; init; } = "";

    public static TroubleshootingLogResult Ok(string log, string summary) =>
        new() { Succeeded = true, Log = log, Summary = summary };

    public static TroubleshootingLogResult Fail(string log, string summary) =>
        new() { Succeeded = false, Log = log, Summary = summary };
}

/// <summary>
/// Confirm-gated Manager one-shots wrap existing door_vm / onbox scripts over SSH/OCI.
/// Park-IP variant: reserved play IP → VM1 secondary if VM1 is RUNNING, else door secondary
/// (start the door first if needed). Already-on-target is success.
/// </summary>
public sealed class TroubleshootingService
{
    private static readonly TimeSpan DiagnoseTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ResetTimeout = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UnstickTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan IpMoveTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan HealTimeout = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan PullTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan NetplanTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(45);

    private readonly ManagerLocalConfig _config;
    private readonly ISshService _ssh;
    private readonly IComputeService? _compute;
    private readonly IDoorClient? _door;

    public TroubleshootingService(
        ManagerLocalConfig config,
        ISshService ssh,
        IComputeService? compute,
        IDoorClient? door)
    {
        _config = config;
        _ssh = ssh;
        _compute = compute;
        _door = door;
    }

    public async Task<TroubleshootingLogResult> ParkPlayIpAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Park reserved play IP (preferred: VM1 if RUNNING, else door).");
        log.Line("Wraps door /opt/mccontrol/oci/ip_to_vm1.sh or ip_to_vm2.sh (instance principal).");

        if (_compute is null)
            return log.Fail("OCI Compute session unavailable.");

        var vm1 = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId, cancellationToken);
        if (!vm1.Succeeded)
            return log.Fail(vm1.Error ?? "Could not read VM1 lifecycle.");

        var vm1Life = (vm1.Value ?? "").Trim().ToUpperInvariant();
        log.Line($"VM1 lifecycle: {vm1Life}");

        var doorReady = await EnsureDoorRunningAsync(log, cancellationToken);
        if (!doorReady.Succeeded)
            return doorReady;

        var toVm1 = vm1Life == "RUNNING";
        var script = toVm1
            ? "/opt/mccontrol/oci/ip_to_vm1.sh"
            : "/opt/mccontrol/oci/ip_to_vm2.sh";
        log.Line(toVm1
            ? "VM1 is RUNNING → assign reserved IP to VM1 secondary."
            : "VM1 is not RUNNING → assign reserved IP to door secondary.");

        var run = await RunDoorOciScriptAsync(script, extraArgs: "", IpMoveTimeout, cancellationToken);
        log.AppendExec("door " + Path.GetFileName(script), run);
        return run.Succeeded
            ? log.Ok(toVm1
                ? "Reserved play IP is on VM1 (or was already there)."
                : "Reserved play IP is on the door (or was already there).")
            : log.Fail("IP move script failed. See log.");
    }

    public Task<TroubleshootingLogResult> DiagnoseWaitForgeAsync(
        CancellationToken cancellationToken = default) =>
        RunDoorScriptAsync(
            "Diagnose wait_forge (read-only TCP probe from the door to VM1 :25565).",
            "/opt/mccontrol/scripts/diagnose_wait_forge.sh",
            DiagnoseTimeout,
            needsOciEnv: false,
            cancellationToken);

    public Task<TroubleshootingLogResult> ResetDoorStateAsync(
        CancellationToken cancellationToken = default) =>
        RunDoorScriptAsync(
            "Reset door state (STARTING/DEGRADED → IDLE). Does not move the reserved IP — use Park play IP if needed.",
            "/opt/mccontrol/scripts/reset_door_state.sh",
            ResetTimeout,
            needsOciEnv: false,
            cancellationToken);

    public Task<TroubleshootingLogResult> UnstickAfterForgeReadyAsync(
        CancellationToken cancellationToken = default) =>
        RunDoorScriptAsync(
            "Unstick after Minecraft is listening: diagnose → reset → POST /api/wake (DOOR-ISSUE-5 leftover sticky DEGRADED).",
            "/opt/mccontrol/scripts/unstick_after_forge_ready.sh",
            UnstickTimeout,
            needsOciEnv: false,
            cancellationToken);

    public async Task<TroubleshootingLogResult> RefreshOsBudgetAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Refresh door Object Storage budget/ledger cache.");
        log.Line("Preferred: POST /api/os-refresh. Fallback: pull_os_budget.sh --force.");

        if (_door is not null)
        {
            var http = await _door.RefreshOsAsync(cancellationToken);
            if (http.Succeeded)
            {
                log.Line("POST /api/os-refresh OK");
                log.Line(http.Value ?? "");
                return log.Ok("Door OS cache refreshed.");
            }

            log.Line("HTTP os-refresh failed: " + (http.Error ?? "unknown") + " — trying SSH.");
        }
        else
        {
            log.Line("Door HTTP client unavailable — trying SSH.");
        }

        var ssh = await RunDoorOciScriptAsync(
            "/opt/mccontrol/oci/pull_os_budget.sh",
            extraArgs: "--force",
            PullTimeout,
            cancellationToken);
        log.AppendExec("pull_os_budget.sh --force", ssh);
        return ssh.Succeeded
            ? log.Ok("Door OS cache refreshed via SSH.")
            : log.Fail("OS refresh failed. If the door VM is stopped, use Park play IP first.");
    }

    public async Task<TroubleshootingLogResult> HealLedgerAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Heal open Object Storage ledger (Phase 5: only when VM1 is STOPPED).");

        if (_compute is null)
            return log.Fail("OCI Compute session unavailable.");

        var vm1 = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId, cancellationToken);
        if (!vm1.Succeeded)
            return log.Fail(vm1.Error ?? "Could not read VM1 lifecycle.");

        var life = (vm1.Value ?? "").Trim().ToUpperInvariant();
        log.Line($"VM1 lifecycle: {life}");
        if (life != "STOPPED")
        {
            return log.Fail(
                "Heal refused: VM1 must be STOPPED (not STOPPING/RUNNING). "
                + "Wait for SoftStop to finish, or use Park play IP if the door is down.");
        }

        var run = await RunDoorOciScriptAsync(
            "/opt/mccontrol/oci/heal_os_ledger.sh",
            extraArgs: "",
            HealTimeout,
            cancellationToken);
        log.AppendExec("heal_os_ledger.sh", run);
        return run.Succeeded
            ? log.Ok("Heal script finished (see HEAL_SKIP / HEAL_OS_OK in the log).")
            : log.Fail("Heal script failed. See log.");
    }

    public async Task<TroubleshootingLogResult> ShowIdleStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Idle timer / Minecraft unit status on VM1 (read-only).");
        var unit = MinecraftUnit();
        var cmd =
            "sudo bash -c " + SshShell.Quote(
                "set -euo pipefail; "
                + "echo '=== " + unit + " ==='; "
                + "systemctl is-active " + unit + " || true; "
                + "systemctl show " + unit
                + " -p ActiveState -p SubState -p Result -p NRestarts -p User -p WorkingDirectory --no-pager; "
                + "echo '=== mc-idle-watch.timer ==='; "
                + "systemctl is-enabled mc-idle-watch.timer || true; "
                + "systemctl is-active mc-idle-watch.timer || true; "
                + "systemctl status mc-idle-watch.timer --no-pager || true; "
                + "echo '=== mc-idle-watch.service (last) ==='; "
                + "systemctl status mc-idle-watch.service --no-pager || true");
        var run = await _ssh.RunCommandAsync(
            SshTarget.FromVm1(_config.Vm1),
            cmd,
            StatusTimeout,
            cancellationToken);
        log.AppendExec("idle/minecraft status", run);
        return run.Succeeded
            ? log.Ok("Idle / Minecraft status captured.")
            : log.Fail("Could not read idle status. Is VM1 RUNNING?");
    }

    public async Task<TroubleshootingLogResult> ForceEnableIdleTimerAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Force-enable mc-idle-watch.timer (does not start Minecraft).");
        log.Line("OS-ISSUE-7: VM1 boot / Minecraft start already force-enables idle.");
        var cmd =
            "sudo bash -c " + SshShell.Quote(
                "set -euo pipefail; "
                + "systemctl enable mc-idle-watch.timer; "
                + "systemctl start mc-idle-watch.timer; "
                + "systemctl is-enabled mc-idle-watch.timer; "
                + "systemctl is-active mc-idle-watch.timer; "
                + "systemctl status mc-idle-watch.timer --no-pager");
        var run = await _ssh.RunCommandAsync(
            SshTarget.FromVm1(_config.Vm1),
            cmd,
            StatusTimeout,
            cancellationToken);
        log.AppendExec("enable mc-idle-watch.timer", run);
        return run.Succeeded
            ? log.Ok("Idle timer enabled/started.")
            : log.Fail("Failed to enable idle timer. Is VM1 RUNNING?");
    }

    public async Task<TroubleshootingLogResult> ReapplyPlayNetplanAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Re-apply /etc/netplan/99-mcmgr-play.yaml on RUNNING guests (SETUP-ISSUE-1).");

        if (_compute is null)
            return log.Fail("OCI Compute session unavailable.");

        var any = false;
        var allOk = true;

        var vm1Life = await _compute.GetLifecycleStateAsync(_config.Vm1.InstanceId, cancellationToken);
        if (!vm1Life.Succeeded)
            return log.Fail(vm1Life.Error ?? "Could not read VM1 lifecycle.");

        if (IsRunning(vm1Life.Value))
        {
            any = true;
            var ip = _config.Vm1.SecondaryPrivateIp;
            if (string.IsNullOrWhiteSpace(ip))
            {
                log.Line("VM1 RUNNING but vm1.secondary_private_ip is empty — skipped.");
                allOk = false;
            }
            else
            {
                var script = GuestPlayNetplan.BuildApplyScript(ip);
                var run = await _ssh.RunCommandAsync(
                    SshTarget.FromVm1(_config.Vm1),
                    "sudo bash -c " + SshShell.Quote(script),
                    NetplanTimeout,
                    cancellationToken);
                log.AppendExec("VM1 netplan", run);
                allOk &= run.Succeeded;
            }
        }
        else
        {
            log.Line($"VM1 is {vm1Life.Value} — netplan skipped (guest must be RUNNING).");
        }

        var doorLife = await _compute.GetLifecycleStateAsync(_config.Door.InstanceId, cancellationToken);
        if (!doorLife.Succeeded)
            return log.Fail(doorLife.Error ?? "Could not read door lifecycle.");

        if (IsRunning(doorLife.Value))
        {
            any = true;
            var ip = _config.Door.SecondaryPrivateIp;
            if (string.IsNullOrWhiteSpace(ip))
            {
                log.Line("Door RUNNING but door.secondary_private_ip is empty — skipped.");
                allOk = false;
            }
            else
            {
                var script = GuestPlayNetplan.BuildApplyScript(ip);
                var run = await _ssh.RunCommandAsync(
                    SshTarget.FromDoor(_config.Door),
                    "sudo bash -c " + SshShell.Quote(script),
                    NetplanTimeout,
                    cancellationToken);
                log.AppendExec("door netplan", run);
                allOk &= run.Succeeded;
            }
        }
        else
        {
            log.Line($"Door is {doorLife.Value} — netplan skipped (guest must be RUNNING).");
        }

        if (!any)
            return log.Fail("Neither VM is RUNNING — start the door (Park play IP) or VM1 first.");

        return allOk
            ? log.Ok("Play netplan re-applied on RUNNING guests.")
            : log.Fail("Netplan apply had failures. See log.");
    }

    public async Task<TroubleshootingLogResult> RepairGamePermissionsAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Repair /opt/mcmgr tree using the Step 4.2 layout contract (not chmod 0777).");
        log.Line("Does not start Minecraft (avoids OS-ISSUE-7 idle force-enable).");

        var vm1 = SshTarget.FromVm1(_config.Vm1);
        var probe = await _ssh.RunCommandAsync(
            vm1,
            "test -x /opt/mcmgr/bin/repair-permissions.sh && echo INSTALLED || echo MISSING",
            TimeSpan.FromSeconds(20),
            cancellationToken);
        log.AppendExec("probe repair-permissions.sh", probe);
        if (!probe.Succeeded)
            return log.Fail("Could not reach VM1. Is it RUNNING?");

        string scriptPath;
        if ((probe.Output ?? "").Contains("INSTALLED", StringComparison.Ordinal))
        {
            scriptPath = "/opt/mcmgr/bin/repair-permissions.sh";
        }
        else
        {
            var onbox = ProductPaths.FindOnboxDirectory();
            if (onbox is null)
                return log.Fail("Product onbox/mcmgr/ not found and /opt/mcmgr/bin/repair-permissions.sh is missing.");

            const string staging = "/tmp/mcmgr-onbox";
            var mkdir = await _ssh.RunCommandAsync(
                vm1,
                $"rm -rf {staging} && mkdir -p {staging}/common",
                TimeSpan.FromSeconds(20),
                cancellationToken);
            log.AppendExec("mkdir staging", mkdir);
            if (!mkdir.Succeeded)
                return log.Fail("Could not create ubuntu-writable /tmp/mcmgr-onbox.");

            var files = new (string LocalPath, string RemotePath)[]
            {
                (Path.Combine(onbox, "repair-permissions.sh"), staging + "/repair-permissions.sh"),
                (Path.Combine(onbox, "common", "env.sh"), staging + "/common/env.sh"),
                (Path.Combine(onbox, "common", "layout.sh"), staging + "/common/layout.sh"),
            };
            var upload = await _ssh.UploadTextFilesAsync(vm1, files, cancellationToken);
            log.AppendExec("upload onbox helpers", upload);
            if (!upload.Succeeded)
                return log.Fail("Upload of repair-permissions helpers failed.");

            scriptPath = staging + "/repair-permissions.sh";
        }

        var run = await _ssh.RunCommandAsync(
            vm1,
            "sudo bash " + SshShell.Quote(scriptPath),
            RepairTimeout,
            cancellationToken);
        log.AppendExec("repair-permissions.sh", run);
        return run.Succeeded
            ? log.Ok("Layout contract re-applied. Minecraft was not started.")
            : log.Fail("repair-permissions.sh failed. See log.");
    }

    public async Task<TroubleshootingLogResult> DiagnoseMinecraftChdirAsync(
        CancellationToken cancellationToken = default)
    {
        var log = new LogBuffer();
        log.Line("Minecraft CHDIR / journal diagnosis (read-only).");
        var unit = MinecraftUnit();
        var cmd =
            "sudo bash -c " + SshShell.Quote(
                "set -euo pipefail; "
                + "echo '=== journalctl -u " + unit + " -n 80 ==='; "
                + "journalctl -u " + unit + " -n 80 --no-pager || true; "
                + "echo '=== systemctl show ==='; "
                + "systemctl show " + unit
                + " -p User -p Group -p WorkingDirectory -p Result -p ExecMainStatus -p NRestarts --no-pager; "
                + "WD=$(systemctl show -p WorkingDirectory --value " + unit + "); "
                + "WD=${WD:-/opt/mcmgr/server}; "
                + "echo \"=== namei -l $WD ===\"; "
                + "namei -l \"$WD\" || true; "
                + "echo '=== ls -ld ==='; "
                + "ls -ld /opt /opt/mcmgr /opt/mcmgr/server 2>/dev/null || true");
        var run = await _ssh.RunCommandAsync(
            SshTarget.FromVm1(_config.Vm1),
            cmd,
            StatusTimeout,
            cancellationToken);
        log.AppendExec("minecraft journal + namei", run);
        return run.Succeeded
            ? log.Ok("Diagnosis captured. If you see 200/CHDIR, use Repair game permissions.")
            : log.Fail("Could not read Minecraft journal. Is VM1 RUNNING?");
    }

    private async Task<TroubleshootingLogResult> RunDoorScriptAsync(
        string heading,
        string scriptPath,
        TimeSpan timeout,
        bool needsOciEnv,
        CancellationToken cancellationToken)
    {
        var log = new LogBuffer();
        log.Line(heading);
        var run = needsOciEnv
            ? await RunDoorOciScriptAsync(scriptPath, extraArgs: "", timeout, cancellationToken)
            : await _ssh.RunCommandAsync(
                SshTarget.FromDoor(_config.Door),
                "sudo bash " + SshShell.Quote(scriptPath),
                timeout,
                cancellationToken);
        log.AppendExec(Path.GetFileName(scriptPath), run);
        return run.Succeeded
            ? log.Ok(Path.GetFileName(scriptPath) + " finished.")
            : log.Fail(Path.GetFileName(scriptPath) + " failed. Is the door VM RUNNING?");
    }

    private async Task<SshExecResult> RunDoorOciScriptAsync(
        string scriptPath,
        string extraArgs,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var inner =
            "set -a; source <(tr -d '\\r' < /etc/mccontrol/oci.env); set +a; "
            + "export HOME=\"${HOME:-/home/ubuntu}\"; "
            + "export OCI_CLI_AUTH=\"${OCI_CLI_AUTH:-instance_principal}\"; "
            + "bash -- " + scriptPath
            + (string.IsNullOrWhiteSpace(extraArgs) ? "" : " " + extraArgs);
        var command = "sudo bash -c " + SshShell.Quote(inner);
        return await _ssh.RunCommandAsync(
            SshTarget.FromDoor(_config.Door),
            command,
            timeout,
            cancellationToken);
    }

    private async Task<TroubleshootingLogResult> EnsureDoorRunningAsync(
        LogBuffer log,
        CancellationToken cancellationToken)
    {
        if (_compute is null)
            return log.Fail("OCI Compute session unavailable.");

        var id = _config.Door.InstanceId;
        if (string.IsNullOrWhiteSpace(id))
            return log.Fail("door.instance_id is empty.");

        var life = await _compute.GetLifecycleStateAsync(id, cancellationToken);
        if (!life.Succeeded)
            return log.Fail(life.Error ?? "Could not read door lifecycle.");

        var state = (life.Value ?? "").Trim().ToUpperInvariant();
        log.Line($"Door lifecycle: {state}");

        if (state == "RUNNING")
            return log.Ok("Door already RUNNING.");

        if (state == "STOPPING")
        {
            log.Line("Door is STOPPING — waiting for STOPPED before START.");
            var waitStopped = await _compute.WaitForLifecycleAsync(id, "STOPPED", cancellationToken: cancellationToken);
            if (!waitStopped.Succeeded)
                return log.Fail(waitStopped.Error ?? "Timed out waiting for door STOPPED.");
            state = "STOPPED";
        }

        if (state is "STOPPED" or "STOPPING")
        {
            log.Line("Starting door VM (Always Free Micro; required to run ip_to_vm*.sh).");
            var start = await _compute.StartInstanceAsync(id, cancellationToken);
            if (!start.Succeeded)
                return log.Fail(start.Error ?? "Door START failed.");
        }

        log.Line("Waiting for door RUNNING…");
        var wait = await _compute.WaitForLifecycleAsync(id, "RUNNING", cancellationToken: cancellationToken);
        if (!wait.Succeeded)
            return log.Fail(wait.Error ?? "Timed out waiting for door RUNNING.");

        log.Line("Door is RUNNING.");
        return log.Ok("Door RUNNING.");
    }

    private string MinecraftUnit() =>
        string.IsNullOrWhiteSpace(_config.Vm1.MinecraftUnit) ? "minecraft" : _config.Vm1.MinecraftUnit.Trim();

    private static bool IsRunning(string? lifecycle) =>
        string.Equals(lifecycle?.Trim(), "RUNNING", StringComparison.OrdinalIgnoreCase);

    private sealed class LogBuffer
    {
        private readonly StringBuilder _sb = new();

        public void Line(string text)
        {
            if (string.IsNullOrEmpty(text))
                return;
            _sb.AppendLine(text.TrimEnd());
        }

        public void AppendExec(string name, SshExecResult result)
        {
            Line($"--- {name} (exit {result.ExitStatus}) ---");
            Line(result.Format());
        }

        public TroubleshootingLogResult Ok(string summary)
        {
            Line(summary);
            return TroubleshootingLogResult.Ok(_sb.ToString().TrimEnd(), summary);
        }

        public TroubleshootingLogResult Fail(string summary)
        {
            Line("ERROR: " + summary);
            return TroubleshootingLogResult.Fail(_sb.ToString().TrimEnd(), summary);
        }
    }
}
