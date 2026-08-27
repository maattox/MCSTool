namespace McManager.Core.Services;

/// <summary>
/// Novice sidebar Status. <c>Running</c> when the Minecraft VM is on or the
/// door is playable — the same “already on” idea as Stop — not only door
/// <c>PLAYABLE</c>. Opening Manager on an already-up VM must not stay Stopped.
/// </summary>
public static class ManageNoviceStatus
{
    public const string Running = "Running";
    public const string Stopped = "Stopped";
    public const string Starting = "Starting…";
    public const string Stopping = "Stopping…";
    public const string Restarting = "Restarting…";

    public static bool IsBusy(string? status) =>
        status is Starting or Stopping or Restarting;

    public static bool IsRunning(string? status) =>
        string.Equals(status, Running, StringComparison.Ordinal);

    public static string Label(string? vm1Lifecycle, bool doorPlayable, bool doorStarting)
    {
        if (ManagePowerUx.IsVm1Stopping(vm1Lifecycle))
            return Stopping;

        var vmOn = ManagePowerUx.IsVm1Running(vm1Lifecycle);
        if (!vmOn && (ManagePowerUx.IsVm1ComingUp(vm1Lifecycle) || doorStarting))
            return Starting;

        if (vmOn || doorPlayable)
            return Running;

        return Stopped;
    }
}
