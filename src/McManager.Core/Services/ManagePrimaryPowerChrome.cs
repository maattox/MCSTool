namespace McManager.Core.Services;

/// <summary>
/// Combined Start/Stop chrome. Does not change <see cref="ManagePowerUx"/> allow/deny.
/// Start wins when both are allowed (degraded + STOPPED). In-flight Stop keeps the
/// Stop caption while both commands are greyed.
/// </summary>
public static class ManagePrimaryPowerChrome
{
    public static bool ShowsStop(bool canStart, bool canStop, bool stopInFlight)
    {
        if (canStart)
            return false;
        return canStop || stopInFlight;
    }

    public static bool IsEnabled(bool canStart, bool canStop) =>
        canStart || canStop;
}
