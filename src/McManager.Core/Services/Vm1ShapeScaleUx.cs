using McManager.Core.Setup;

namespace McManager.Core.Services;

/// <summary>
/// Danger Zone day-2 VM1 A1 Flex resize: allowed sizes, STOPPED gate, playtime preview.
/// Does not rewrite ledger intervals (those already store per-interval ocpus/memory_gb).
/// </summary>
public static class Vm1ShapeScaleUx
{
    /// <summary>Product Always Free Ampere envelope used in copy (~1500 OCPU-h).</summary>
    public const double AlwaysFreeOcpuHourEnvelope = 1500;

    public static bool IsVm1Stopped(string? lifecycle) =>
        string.Equals((lifecycle ?? "").Trim(), "STOPPED", StringComparison.OrdinalIgnoreCase);

    public static (int Ocpus, int MemoryGb) ToInts(double ocpus, double memoryGb) =>
        ((int)Math.Round(ocpus), (int)Math.Round(memoryGb));

    public static bool ShapeEquals(
        double leftOcpus,
        double leftMemoryGb,
        double rightOcpus,
        double rightMemoryGb,
        double epsilon = 0.01) =>
        Math.Abs(leftOcpus - rightOcpus) <= epsilon
        && Math.Abs(leftMemoryGb - rightMemoryGb) <= epsilon;

    public static bool IsAllowedTarget(int ocpus, int memoryGb) =>
        Vm1ShapeChoice.IsAllowed(ocpus, memoryGb);

    public static string FormatExact(double ocpus, double memoryGb)
    {
        static string Trim(double value) =>
            Math.Abs(value - Math.Round(value)) < 0.05
                ? Math.Round(value).ToString("0")
                : value.ToString("0.##");

        return $"{Trim(ocpus)} OCPU / {Trim(memoryGb)} GB";
    }

    public static double RemainingPlayHours(double remainingOcpuHours, double ocpus)
    {
        if (ocpus <= 0)
            return 0;
        return Math.Max(0, remainingOcpuHours) / ocpus;
    }

    public static double RemainingOcpuHours(double monthlyOcpuTarget, double monthOcpuUsed) =>
        Math.Max(0, monthlyOcpuTarget - monthOcpuUsed);

    public static bool CanApply(
        string? vm1Lifecycle,
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb)
    {
        if (!IsVm1Stopped(vm1Lifecycle))
            return false;
        if (!IsAllowedTarget(targetOcpus, targetMemoryGb))
            return false;
        return !ShapeEquals(currentOcpus, currentMemoryGb, targetOcpus, targetMemoryGb);
    }

    public static string ApplyBlockedReason(
        string? vm1Lifecycle,
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb)
    {
        if (!IsAllowedTarget(targetOcpus, targetMemoryGb))
            return "That size is not offered. Always Free stays at 2 OCPU / 12 GB or 4 OCPU / 24 GB.";

        if (ShapeEquals(currentOcpus, currentMemoryGb, targetOcpus, targetMemoryGb))
            return "The game computer is already this size.";

        if (!IsVm1Stopped(vm1Lifecycle))
        {
            var state = string.IsNullOrWhiteSpace(vm1Lifecycle) ? "unknown" : vm1Lifecycle.Trim();
            return "Stop the server from the top bar first (game computer must be Stopped). "
                + "Minecraft stops with it. Current VM1 state: " + state + ".";
        }

        return "";
    }

    public static string PreviewBody(
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb,
        double monthlyOcpuTarget,
        double monthOcpuUsed)
    {
        var remaining = RemainingOcpuHours(monthlyOcpuTarget, monthOcpuUsed);
        var currentHours = RemainingPlayHours(remaining, currentOcpus);
        var targetHours = RemainingPlayHours(remaining, targetOcpus);
        var direction = targetOcpus > currentOcpus + 0.01
            ? "less wall-clock uptime (hours burn faster)"
            : targetOcpus < currentOcpus - 0.01
                ? "more wall-clock uptime (hours burn slower)"
                : "the same wall-clock uptime";

        return
            $"Current: {FormatExact(currentOcpus, currentMemoryGb)} — about {currentHours:0.0} h left this month.\n"
            + $"New: {FormatExact(targetOcpus, targetMemoryGb)} — about {targetHours:0.0} h left this month ({direction}).\n"
            + $"Always Free envelope is about {AlwaysFreeOcpuHourEnvelope:0} OCPU-h/month; "
            + $"this stack’s budget target is {monthlyOcpuTarget:0} OCPU-h. "
            + "Past usage intervals keep the size they were recorded at.";
    }

    public static string ConfirmMessage(
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb,
        double monthlyOcpuTarget,
        double monthOcpuUsed)
    {
        return
            "This changes how fast Always Free Ampere hours burn while the game computer is on.\n\n"
            + PreviewBody(
                currentOcpus,
                currentMemoryGb,
                targetOcpus,
                targetMemoryGb,
                monthlyOcpuTarget,
                monthOcpuUsed)
            + "\n\nThe game computer and Minecraft must stay Stopped during the Oracle resize. "
            + "Larger than 4 OCPU / 24 GB is not offered until the Always Free envelope is confirmed.\n\n"
            + "Apply this size in Oracle and update shared budget/meta?";
    }
}
