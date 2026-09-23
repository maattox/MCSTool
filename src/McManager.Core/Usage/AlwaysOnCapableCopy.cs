using McManager.Core.Services;

namespace McManager.Core.Usage;

/// <summary>
/// Usage / pin copy when VM1 can stay up ~24/7 inside Always Free (e.g. 2 OCPU / 12 GB).
/// Still meters hours; does not nag as if the stack were on a scarce 4-OCPU budget.
/// </summary>
public static class AlwaysOnCapableCopy
{
    public static bool ForShape(double ocpus) =>
        Vm1ShapeScaleUx.CanStayUpAroundTheClock(ocpus);

    public static string UsageLead(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How many free hours the server has used. This smaller size can usually stay on all month inside Always Free. Hours are still counted."
            : "How many free hours the server has used. A budget below can block Start when you're out of hours.";

    public static string RemainingHoursLabel(bool alwaysOnCapable) =>
        alwaysOnCapable ? "Hours available this month" : "Hours left this month";

    public static string RemainingHoursHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "This size can usually stay on all month. Hours are still counted. This is not rollover."
            : "Hours left in this month’s budget — not rollover";

    public static string SoftCapsHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "The server still stops if it reaches this limit. This size can usually stay on all month."
            : "Stops the server before the monthly allowance is fully used.";

    public static string IdleWarningsHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server can sit empty. Out-of-hours warnings are rare on this size."
            : "How long the server can sit empty, and how much warning players get before it stops for being out of hours.";

    public static string PublishConfirmBody(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "This updates the hours budget. Hours are still counted; this smaller size can usually stay on all month. Continue?"
            : "This updates the hours budget the server uses to stop itself when you run out of free time. Continue?";

    public static string PinTodayHint(double dailyHours, bool alwaysOnCapable)
    {
        _ = alwaysOnCapable;
        return $"/ {dailyHours:F1}h";
    }

    public static string PinAvgHint(double dailyHours, bool alwaysOnCapable) =>
        alwaysOnCapable
            ? $"/ {dailyHours:F1}h typical day"
            : $"/ {dailyHours:F1}h budget";

    public static string PinMonthHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "used this month" : "of monthly hours";

    public static string PinTodayHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server has been on today. This smaller size can usually stay on all month; hours are still counted."
            : "How long the server has been on today, versus the daily slice of your monthly free hours.";

    public static string PinMonthHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Share of this month’s hours already used. This smaller size can usually stay on all month. Details are on the Usage tab."
            : "Share of this month's free hours already used. Details and edits are on the Usage tab.";

    public static string PinAvgHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Average hours the server has been on per day this month."
            : "Average hours the server has been on per day this month, versus today's allowed hours.";

    public static string PinRolloverHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Unused hours from closed UTC days. Add them to later days in Usage → Calendar, or keep them for days that run over. They don't let players wake the server on a day set to 0 hours."
            : "Unused hours from closed UTC days. Add them to later days in Usage → Calendar, or keep them for days that run over. They don't let players wake the server on a day set to 0 hours. For the hours still left in the month, see Hours left this month on the Usage tab.";

    public static string PinRemainingHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "still counted" : "not rollover";

    public static string PinRemainingHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Hours still available this month. This smaller size can usually stay on all month; hours are still counted. This is not rollover."
            : "Hours left in this month’s budget — not rollover. Details and edits are on the Usage tab.";

    public static string PinIdleHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "empty server" : "empty / not running";

    public static string PinIdleHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server can sit empty before it stops. Out-of-hours warnings are rare on this size. Change this on Usage → Budget settings or Advanced → Danger Zone."
            : "How long the server can sit empty (or with Minecraft not running) before it stops. Change this on Usage → Budget settings or Advanced → Danger Zone.";
}
