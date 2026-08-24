using McManager.Core.Config;
using McManager.Core.Oci;
using McManager.Core.Services;
using McManager.Core.Setup;

namespace McManager.PackTestHarness;

internal sealed class ReadyGateReport
{
    public bool ReadyForNext { get; init; }
    public bool Ssh { get; init; }
    public string Vm1 { get; init; } = "";
    public string MinecraftUnit { get; init; } = "";
    public bool IdleDisabled { get; init; }
    public List<string> Notes { get; init; } = [];
}

internal static class ReadyGate
{
    public static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(3);

    public static ReadyGateReport AnalyzeOnlySkipped() =>
        new()
        {
            ReadyForNext = true,
            Ssh = false,
            Vm1 = "",
            MinecraftUnit = "",
            IdleDisabled = true,
            Notes = ["analyze-only: SSH ready-gate skipped"],
        };

    public static async Task<ReadyGateReport> RunLiveAsync(
        ManagerLocalConfig config,
        bool passLike,
        CancellationToken cancellationToken)
    {
        var notes = new List<string>();
        var ssh = false;
        var vm1 = "";
        var unit = "";
        var idleDisabled = false;

        var sessionResult = OciSession.TryCreate(config);
        if (!sessionResult.Succeeded || sessionResult.Value is null)
        {
            notes.Add(sessionResult.Error ?? "Could not create OCI session for ready-gate.");
            return NotReady(notes, ssh, vm1, unit, idleDisabled);
        }

        using (sessionResult.Value)
        {
            var compute = new ComputeService(sessionResult.Value);
            var life = await compute.GetLifecycleStateAsync(config.Vm1.InstanceId, cancellationToken);
            vm1 = life.Succeeded ? (life.Value ?? "").Trim() : "";
            if (!life.Succeeded)
                notes.Add(life.Error ?? "GetInstance failed.");
            else if (!string.Equals(vm1, "RUNNING", StringComparison.OrdinalIgnoreCase))
                notes.Add("VM1 is not RUNNING.");
        }

        var sshService = new SshService();
        var probe = await sshService.RunCommandAsync(
            SshTarget.FromVm1(config.Vm1),
            "echo ok",
            TimeSpan.FromSeconds(20),
            cancellationToken);
        ssh = probe.Succeeded
            && (probe.Output ?? "").Contains("ok", StringComparison.OrdinalIgnoreCase);
        if (!ssh)
            notes.Add(probe.Error ?? "SSH probe failed.");

        if (ssh)
        {
            var show = await sshService.RunCommandAsync(
                SshTarget.FromVm1(config.Vm1),
                "systemctl show minecraft -p ActiveState --value",
                TimeSpan.FromSeconds(20),
                cancellationToken);
            unit = (show.Output ?? "").Trim().Split('\n')[0].Trim();

            if (!passLike)
            {
                var stop = await sshService.RunCommandAsync(
                    SshTarget.FromVm1(config.Vm1),
                    MinecraftReadiness.StopUnitCommand,
                    TimeSpan.FromSeconds(45),
                    cancellationToken);
                if (!stop.Succeeded)
                    notes.Add(stop.Error ?? "Could not stop minecraft.service.");
                else
                {
                    var after = await sshService.RunCommandAsync(
                        SshTarget.FromVm1(config.Vm1),
                        "systemctl show minecraft -p ActiveState --value",
                        TimeSpan.FromSeconds(20),
                        cancellationToken);
                    unit = (after.Output ?? "").Trim().Split('\n')[0].Trim();
                    if (string.Equals(unit, "active", StringComparison.OrdinalIgnoreCase))
                        notes.Add("minecraft.service still active after stop.");
                }
            }

            var idle = await sshService.ApplyIdleSettingsAsync(
                config.Vm1,
                idleAgentEnabled: false,
                config.Budget.IdleTimeoutMinutes,
                config.Budget.BudgetWarnMinutes,
                cancellationToken);
            if (!idle.Succeeded)
                notes.Add(idle.Error ?? "Could not disable idle after replace.");
            else
            {
                var idleState = await sshService.RunCommandAsync(
                    SshTarget.FromVm1(config.Vm1),
                    "systemctl is-active mc-idle-watch.timer || true",
                    TimeSpan.FromSeconds(20),
                    cancellationToken);
                var active = (idleState.Output ?? "").Trim();
                idleDisabled = active.Contains("inactive", StringComparison.OrdinalIgnoreCase)
                    || active.Contains("unknown", StringComparison.OrdinalIgnoreCase)
                    || active.Contains("failed", StringComparison.OrdinalIgnoreCase);
                if (!idleDisabled)
                    notes.Add("mc-idle-watch.timer is still active (OS-ISSUE-7).");
            }
        }

        await Task.Delay(Cooldown, cancellationToken);

        var vmRunning = string.Equals(vm1, "RUNNING", StringComparison.OrdinalIgnoreCase);
        var unitOk = passLike
            || !string.Equals(unit, "active", StringComparison.OrdinalIgnoreCase);
        var ready = ssh && vmRunning && unitOk && idleDisabled;
        return new ReadyGateReport
        {
            ReadyForNext = ready,
            Ssh = ssh,
            Vm1 = vm1,
            MinecraftUnit = unit,
            IdleDisabled = idleDisabled,
            Notes = notes,
        };
    }

    private static ReadyGateReport NotReady(
        List<string> notes,
        bool ssh,
        string vm1,
        string unit,
        bool idleDisabled) =>
        new()
        {
            ReadyForNext = false,
            Ssh = ssh,
            Vm1 = vm1,
            MinecraftUnit = unit,
            IdleDisabled = idleDisabled,
            Notes = notes,
        };
}
