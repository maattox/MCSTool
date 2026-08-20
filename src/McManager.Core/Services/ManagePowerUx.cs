namespace McManager.Core.Services;

/// <summary>
/// Top-bar Start/Stop chrome rules. Novice Status is door-playable; Start still
/// requires the Minecraft VM (VM1) OCI lifecycle to be STOPPED.
/// </summary>
public static class ManagePowerUx
{
    public const string WaitUntilFullyStoppedToolTip =
        "Wait until the server has fully stopped.";

    public static bool IsVm1Stopped(string? lifecycle) =>
        string.Equals((lifecycle ?? "").Trim(), "STOPPED", StringComparison.OrdinalIgnoreCase);

    public static bool IsVm1Running(string? lifecycle) =>
        string.Equals((lifecycle ?? "").Trim(), "RUNNING", StringComparison.OrdinalIgnoreCase);

    public static bool IsVm1Stopping(string? lifecycle) =>
        string.Equals((lifecycle ?? "").Trim(), "STOPPING", StringComparison.OrdinalIgnoreCase);

    public static bool IsVm1ComingUp(string? lifecycle)
    {
        var life = (lifecycle ?? "").Trim().ToUpperInvariant();
        return life is "STARTING" or "PROVISIONING";
    }

    /// <summary>
    /// Start is allowed only when VM1 is fully STOPPED — not STOPPING, STARTING,
    /// PROVISIONING, RUNNING, empty, or unknown.
    /// </summary>
    public static bool LifecycleAllowsStart(string? vm1Lifecycle) =>
        IsVm1Stopped(vm1Lifecycle);

    public static bool CanStart(
        bool hasInitialStatus,
        bool powerActionInFlight,
        bool spendBrakeUnlockInFlight,
        bool configLoaded,
        string? vm1Lifecycle,
        bool doorPlayable,
        bool doorStarting,
        bool doorDegraded,
        bool spendBrakeBlocks,
        bool doorStatusKnown)
    {
        if (!hasInitialStatus || powerActionInFlight || spendBrakeUnlockInFlight || !configLoaded)
            return false;
        if (!doorStatusKnown || spendBrakeBlocks)
            return false;

        var alreadyOn = !doorDegraded && (IsVm1Running(vm1Lifecycle) || doorPlayable);
        var starting = !doorDegraded && (IsVm1ComingUp(vm1Lifecycle) || doorStarting);
        if (alreadyOn || starting)
            return false;

        return LifecycleAllowsStart(vm1Lifecycle);
    }
}
