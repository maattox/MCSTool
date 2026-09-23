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

    /// <summary>Longest calendar month as wall-clock hours (24 × 31).</summary>
    public const double LongestMonthHours = 24 * 31;

    /// <summary>
    /// True when this OCPU count can stay up ~24/7 inside the Always Free envelope
    /// (2 OCPU × 744 h ≈ 1488 OCPU-h; 4 OCPU cannot).
    /// </summary>
    public static bool CanStayUpAroundTheClock(double ocpus)
    {
        if (ocpus <= 0)
            return false;
        return ocpus * LongestMonthHours <= AlwaysFreeOcpuHourEnvelope + 0.5;
    }

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
            return "The server is already this size.";

        if (!IsVm1Stopped(vm1Lifecycle))
        {
            var state = string.IsNullOrWhiteSpace(vm1Lifecycle) ? "unknown" : vm1Lifecycle.Trim();
            return "Stop the server from the sidebar first (it must be fully Stopped). "
                + "Minecraft stops with it. Current game VM state: " + state + ".";
        }

        return "";
    }

    public static string PreviewBody(
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb,
        double monthlyOcpuTarget,
        double monthOcpuUsed,
        string? currentJvmXmx = null)
    {
        var remaining = RemainingOcpuHours(monthlyOcpuTarget, monthOcpuUsed);
        var currentHours = RemainingPlayHours(remaining, currentOcpus);
        var targetHours = RemainingPlayHours(remaining, targetOcpus);
        var direction = targetOcpus > currentOcpus + 0.01
            ? "fewer hours of uptime; free hours are used faster"
            : targetOcpus < currentOcpus - 0.01
                ? "more hours of uptime; free hours are used slower"
                : "the same hours of uptime";

        var body =
            $"Current: {FormatExact(currentOcpus, currentMemoryGb)} — about {currentHours:0.0} hours left this month.\n"
            + $"New: {FormatExact(targetOcpus, targetMemoryGb)} — about {targetHours:0.0} hours left this month ({direction}).\n"
            + $"Always Free allows about {AlwaysFreeOcpuHourEnvelope:0} CPU-hours a month; "
            + $"this server's budget is {monthlyOcpuTarget:0} CPU-hours. "
            + "Past usage keeps the size it was recorded at.";
        var clamp = ServerMemoryClampSentence(targetMemoryGb, currentJvmXmx);
        return string.IsNullOrEmpty(clamp) ? body : body + "\n" + clamp;
    }

    public static string ConfirmMessage(
        double currentOcpus,
        double currentMemoryGb,
        int targetOcpus,
        int targetMemoryGb,
        double monthlyOcpuTarget,
        double monthOcpuUsed,
        string? currentJvmXmx = null)
    {
        return
            "This changes how fast Always Free hours are used while the server is on.\n\n"
            + PreviewBody(
                currentOcpus,
                currentMemoryGb,
                targetOcpus,
                targetMemoryGb,
                monthlyOcpuTarget,
                monthOcpuUsed,
                currentJvmXmx)
            + "\n\nThe server must stay Stopped while Oracle changes the size. "
            + "Sizes above 4 OCPU / 24 GB are not offered yet.\n\n"
            + "Change the VM size now?";
    }

    /// <summary>
    /// Token to persist after a size change. Downsize 24→12 GB clamps 10G/12G to 8G.
    /// Upsize never raises server memory.
    /// </summary>
    public static string ServerMemoryAfterResize(string? currentJvmXmx, int targetMemoryGb) =>
        JvmHeapChoice.ClampToHost(currentJvmXmx, targetMemoryGb);

    public static bool ServerMemoryWillClamp(string? currentJvmXmx, int targetMemoryGb)
    {
        var current = JvmHeapChoice.Normalize(currentJvmXmx);
        return !string.Equals(
            current,
            ServerMemoryAfterResize(current, targetMemoryGb),
            StringComparison.Ordinal);
    }

    public static string? ServerMemoryClampSentence(int targetMemoryGb, string? currentJvmXmx)
    {
        if (!ServerMemoryWillClamp(currentJvmXmx, targetMemoryGb))
            return null;
        var cap = ServerMemoryAfterResize(currentJvmXmx, targetMemoryGb);
        return $"Server memory will be set to {cap} so it fits this size.";
    }
}
