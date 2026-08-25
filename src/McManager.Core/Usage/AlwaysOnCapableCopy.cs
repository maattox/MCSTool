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
            ? "How much free cloud time the server has used. This smaller size can usually stay on all month inside Always Free. Hours are still counted."
            : "How much free cloud time the server has used. Saving a budget below is what refuses a start when you are out of hours.";

    public static string RemainingHoursLabel(bool alwaysOnCapable) =>
        alwaysOnCapable ? "Hours available this month" : "Hours left this month";

    public static string RemainingHoursHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "This size can usually stay on all month. Hours are still counted — this is not the rollover bank."
            : "Wall-clock time still in this month’s cap — not the rollover bank";

    public static string SoftCapsHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Still used to stop the server if a cap is hit. This size can usually stay on all month."
            : "Warn and idle-stop before you fully spend the monthly allowance.";

    public static string IdleWarningsHint(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server can sit empty. Daily-cap warnings are uncommon on this size."
            : "How long the server can sit empty, and how far ahead to warn players about the daily cap.";

    public static string PublishConfirmBody(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "This updates the shared hours budget. Usage is still counted; this smaller size can usually stay on all month. Continue?"
            : "This updates the shared hours budget the server uses to stop itself when you run out of free time. Continue?";

    public static string PinTodayHint(double dailyHours, bool alwaysOnCapable) =>
        alwaysOnCapable
            ? $"/ {dailyHours:F1}h today"
            : $"/ {dailyHours:F1}h allowed";

    public static string PinAvgHint(double dailyHours, bool alwaysOnCapable) =>
        alwaysOnCapable
            ? $"/ {dailyHours:F1}h typical day"
            : $"/ {dailyHours:F1}h budget";

    public static string PinMonthHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "used this month" : "of monthly cap";

    public static string PinTodayHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server has been on today. This smaller size can usually stay on all month; hours are still counted."
            : "How long the server has been on today, versus the daily slice of your monthly free hours.";

    public static string PinMonthHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Share of this month’s hours already used. This smaller size can usually stay on all month. Details are on the Usage tab."
            : "Share of this month's free compute budget already used. Details and edits are on the Usage tab.";

    public static string PinAvgHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Average hours the server has been on per day this month."
            : "Average hours the server has been on per day this month, versus today's allowed hours.";

    public static string PinRolloverHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Unused daily hours saved from earlier days this month."
            : "Unused daily hours saved from earlier days this month. This is not the hours still left in the month — that remaining figure is the Hours left pin.";

    public static string PinRemainingHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "still counted" : "not rollover";

    public static string PinRemainingHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "Wall-clock hours still available this month. This smaller size can usually stay on all month; hours are still counted. This is not the rollover bank."
            : "Wall-clock hours still in this month’s cap — not the rollover bank. Details and edits are on the Usage tab.";

    public static string PinIdleHint(bool alwaysOnCapable) =>
        alwaysOnCapable ? "empty server" : "empty / not running";

    public static string PinIdleHelp(bool alwaysOnCapable) =>
        alwaysOnCapable
            ? "How long the server can sit empty before it stops. Daily-cap warnings are uncommon on this size. Change this on Usage → Budget or Advanced → Danger."
            : "How long the server can sit empty (or with Minecraft not running) before it stops. Change this on Usage → Budget or Advanced → Danger.";
}
