namespace McManager.Core.Setup;

/// <summary>Resumable Setup apply checkpoints (wizard JSON + tofu state + on-box bootstrap-state are separate).</summary>
public static class SetupApplyStage
{
    public const string NotStarted = "not_started";
    public const string TofuApplied = "tofu_applied";
    public const string CloudInit = "cloud_init";
    public const string Door = "door";
    public const string Vm1 = "vm1";
    public const string OsMeta = "os_meta";
    public const string Function = "function";
    public const string ConfigWritten = "config_written";

    public static readonly string[] Order =
    [
        NotStarted,
        TofuApplied,
        CloudInit,
        Door,
        Vm1,
        OsMeta,
        Function,
        ConfigWritten,
    ];

    /// <summary>
    /// Typical seconds per <see cref="Order"/> index from the 2026-08-17 E2E timed log
    /// (<c>docs/deploy-log.txt</c>). Index 0 is unused. Cloud-init uses the operator’s
    /// 3-minute estimate (the 19-minute wait was SETUP-ISSUE-5, already fixed). Function
    /// was skipped in that run (no OCIR login); keep a small floor so the bar still moves.
    /// </summary>
    internal static readonly int[] TypicalSeconds =
    [
        0,   // not_started
        60,  // tofu apply (~48s observed; init/plan start truncated in the copied log)
        180, // cloud-init (operator: 3 min, not the false 19 min wait)
        300, // door bootstrap (4m54s)
        150, // VM1 idle-agent + Minecraft + guest repair (~2m + 25s)
        15,  // Object Storage seed (~1s)
        15,  // Function/OCIR (skipped in the timed run)
        15,  // config.local.json write
    ];

    internal static readonly int TotalTypicalSeconds = SumFrom(1);

    public static int IndexOf(string? stage)
    {
        if (string.IsNullOrWhiteSpace(stage))
            return 0;
        var i = Array.IndexOf(Order, stage.Trim());
        return i < 0 ? 0 : i;
    }

    public static bool Reached(string? current, string required) =>
        IndexOf(current) >= IndexOf(required);

    /// <summary>0–100 when <paramref name="stage"/> has finished. <see cref="NotStarted"/> is 0; <see cref="ConfigWritten"/> is 100.</summary>
    public static int Percent(string? stage) => PercentWhenComplete(stage);

    public static int PercentWhenStarting(string? stage)
    {
        var idx = IndexOf(stage);
        if (idx <= 0)
            return 0;
        return Ratio(WeightThrough(idx - 1));
    }

    public static int PercentWhenComplete(string? stage)
    {
        var idx = IndexOf(stage);
        if (idx <= 0)
            return 0;
        if (idx >= Order.Length - 1)
            return 100;
        return Ratio(WeightThrough(idx));
    }

    /// <summary>
    /// Crawl the current stage’s slice of the bar. Caps below the complete percent until
    /// <see cref="Completed"/> so the fill never looks finished early or jumps backwards.
    /// <see cref="NotStarted"/> is the tofu-apply window.
    /// </summary>
    public static int PercentInProgress(string? stage, TimeSpan timeInStage)
    {
        var work = WorkStage(stage);
        var start = PercentWhenStarting(stage);
        var end = PercentWhenComplete(work);
        if (end <= start)
            return start;

        var typical = TypicalSecondsFor(work);
        if (typical <= 0)
            return start;

        var fraction = Math.Clamp(timeInStage.TotalSeconds / typical, 0, 0.95);
        return start + (int)Math.Round((end - start) * fraction);
    }

    public static TimeSpan EstimateRemaining(string? stage, TimeSpan timeInStage, bool stageComplete)
    {
        var work = WorkStage(stage);
        var idx = IndexOf(work);
        int seconds;
        if (stageComplete)
        {
            seconds = WeightAfter(idx);
        }
        else
        {
            var typical = TypicalSecondsFor(work);
            var used = (int)Math.Clamp(timeInStage.TotalSeconds, 0, typical);
            seconds = typical - used + WeightAfter(idx);
        }

        return TimeSpan.FromSeconds(Math.Max(0, seconds));
    }

    /// <summary>Honest range, not a second-by-second countdown.</summary>
    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return "Less than a minute left";

        var minutes = remaining.TotalMinutes;
        if (minutes < 0.75)
            return "Less than a minute left";
        if (minutes < 2.5)
            return "About 1–3 minutes left";
        if (minutes < 6)
            return "About 3–6 minutes left";
        if (minutes < 10)
            return "About 5–10 minutes left";
        if (minutes < 16)
            return "About 10–15 minutes left";
        return "About 15–25 minutes left";
    }

    public static string DisplayName(string? stage) => (stage ?? "").Trim() switch
    {
        TofuApplied => "Creating cloud resources…",
        CloudInit => "Waiting for the servers to start…",
        Door => "Installing doorbell software…",
        Vm1 => "Installing Minecraft…",
        OsMeta => "Saving shared storage…",
        Function => "Installing the spend-brake Function…",
        ConfigWritten => "Saving local config…",
        _ => "Starting…",
    };

    public static SetupProgressUpdate Starting(string stage, string? caption = null) =>
        new(stage, PercentWhenStarting(stage), caption ?? DisplayName(stage), StageComplete: false);

    public static SetupProgressUpdate Completed(string stage, string? caption = null) =>
        new(stage, PercentWhenComplete(stage), caption ?? DisplayName(stage), StageComplete: true);

    /// <summary>Alias for <see cref="Starting"/> (in-progress report).</summary>
    public static SetupProgressUpdate Update(string stage, string? caption = null) =>
        Starting(stage, caption);

    private static string WorkStage(string? stage)
    {
        var trimmed = (stage ?? "").Trim();
        return trimmed.Length == 0 || trimmed == NotStarted ? TofuApplied : trimmed;
    }

    private static int TypicalSecondsFor(string stage)
    {
        var idx = IndexOf(stage);
        return idx >= 0 && idx < TypicalSeconds.Length ? TypicalSeconds[idx] : 0;
    }

    private static int WeightThrough(int lastInclusive)
    {
        if (lastInclusive <= 0)
            return 0;
        var sum = 0;
        var end = Math.Min(lastInclusive, TypicalSeconds.Length - 1);
        for (var i = 1; i <= end; i++)
            sum += TypicalSeconds[i];
        return sum;
    }

    private static int WeightAfter(int idx)
    {
        var sum = 0;
        for (var i = idx + 1; i < TypicalSeconds.Length; i++)
            sum += TypicalSeconds[i];
        return sum;
    }

    private static int SumFrom(int start)
    {
        var sum = 0;
        for (var i = start; i < TypicalSeconds.Length; i++)
            sum += TypicalSeconds[i];
        return sum;
    }

    private static int Ratio(int part)
    {
        if (TotalTypicalSeconds <= 0)
            return 0;
        return Math.Clamp((int)Math.Round(100.0 * part / TotalTypicalSeconds), 0, 100);
    }
}

/// <summary>UI-only deploy progress. Does not persist; dry-run may report 100% without changing apply_stage.</summary>
public readonly record struct SetupProgressUpdate(string Stage, int Percent, string Caption, bool StageComplete = false);
