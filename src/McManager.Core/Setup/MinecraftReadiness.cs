using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Setup / Change pack joinable health (blueprint §12.1 step 9 + §14.3 health-check row).
/// Success is still a working RCON <c>list</c>. Crash-loops and loader FATAL fail fast
/// instead of looking like a slow first world gen. A RuntimeDistCleaner ERROR for a
/// missing client mixin <em>target</em> (library jar, <c>@Mixin target … was not found</c>)
/// is not treated as fatal — the dedicated server can still reach Done.
/// </summary>
public static class MinecraftReadiness
{
    public const int MaxRconAttempts = 12;
    public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    public const int ProbeJournalLines = 80;
    public const int JournalExcerptMaxLines = 30;
    public const int JournalLineMaxChars = 240;
    public const int CrashLoopRestartThreshold = 2;

    public const string RconMarker = "===MCMGR_RCON===";
    public const string SystemdMarker = "===MCMGR_SYSTEMD===";
    public const string JournalMarker = "===MCMGR_JOURNAL===";

    public const string StopUnitCommand = "sudo systemctl stop minecraft";
    public const string JournalSinceDateCommand = "date -u -d '-30 seconds' '+%Y-%m-%d %H:%M:%S'";

    public const string JoinableLog = "RCON list succeeded.";
    public const string CrashDetectedLog =
        "Minecraft crash detected during health check; stopping the unit.";

    public const string CrashHeadline =
        "Minecraft crashed while starting and was stopped so it would not keep restarting.";

    public const string TimeoutHeadline =
        "Minecraft is running but RCON list did not succeed in time. "
        + "The unit is not crash-looping; it may still be generating the world, or RCON is not responding. "
        + "Re-Deploy can resume on-box stages.";

    public const string JavaTooOldCause =
        "The Java runtime on the server is too old for this pack (UnsupportedClassVersionError).";

    public const string ClientDistCause =
        "A client-only class tried to load on the dedicated server (invalid dist DEDICATED_SERVER).";

    private static readonly Regex SafeSinceUtc = new(
        @"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ProvidedByMod = new(
        @"provided by ['""]([^'""]+)['""]",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex CausedCrashMod = new(
        @"caused the (?:server|game) to crash:\s*[\r\n]+\s*[-–—*]*\s*([A-Za-z][A-Za-z0-9_.-]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MixinApplyFailed = new(
        @"Mixin apply failed ([A-Za-z][A-Za-z0-9_.-]*)\.mixins",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex MixinApplyForMod = new(
        @"Mixin apply for mod ([A-Za-z][A-Za-z0-9_.-]*)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsSafeSinceTimestamp(string? value) =>
        !string.IsNullOrWhiteSpace(value) && SafeSinceUtc.IsMatch(value.Trim());

    /// <summary>
    /// One SSH command: localhost RCON <c>list</c>, systemd unit fields, recent journal.
    /// Does not open 25575 on the Security List.
    /// </summary>
    public static string ProbeCommand(string? journalSinceUtc)
    {
        if (!MinecraftConsoleRemote.TryBuildRconCommand("list", out var rcon, out _))
            rcon = "true";

        var journal = MinecraftConsoleRemote.LogsCommand(ProbeJournalLines);
        if (IsSafeSinceTimestamp(journalSinceUtc))
        {
            journal += " --since " + ShellSingleQuote(journalSinceUtc!.Trim() + " UTC");
        }

        return "echo " + ShellSingleQuote(RconMarker)
            + "; " + rcon
            + "; echo " + ShellSingleQuote(SystemdMarker)
            + "; systemctl show minecraft -p NRestarts -p Result -p ActiveState -p SubState -p ExecMainStatus --no-pager"
            + "; echo " + ShellSingleQuote(JournalMarker)
            + "; " + journal;
    }

    public static MinecraftHealthProbe ParseProbe(string? blob)
    {
        var text = blob ?? "";
        if (!text.Contains(RconMarker, StringComparison.Ordinal))
            return new MinecraftHealthProbe(text, "", "");

        return new MinecraftHealthProbe(
            SliceBetween(text, RconMarker, SystemdMarker),
            SliceBetween(text, SystemdMarker, JournalMarker),
            SliceAfter(text, JournalMarker));
    }

    public static MinecraftUnitSnapshot ParseSystemd(string? show)
    {
        var nRestarts = 0;
        var result = "";
        var active = "";
        var sub = "";
        var exec = 0;
        foreach (var raw in (show ?? "").Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (TryField(line, "NRestarts", out var n))
                _ = int.TryParse(n, NumberStyles.Integer, CultureInfo.InvariantCulture, out nRestarts);
            else if (TryField(line, "Result", out var r))
                result = r;
            else if (TryField(line, "ActiveState", out var a))
                active = a;
            else if (TryField(line, "SubState", out var s))
                sub = s;
            else if (TryField(line, "ExecMainStatus", out var e))
                _ = int.TryParse(e, NumberStyles.Integer, CultureInfo.InvariantCulture, out exec);
        }

        return new MinecraftUnitSnapshot(nRestarts, result, active, sub, exec);
    }

    public static bool LooksJoinable(string? rconPayload)
    {
        var list = rconPayload ?? "";
        return list.Contains("players", StringComparison.OrdinalIgnoreCase)
            || list.Contains("There are", StringComparison.OrdinalIgnoreCase);
    }

    public static MinecraftReadinessReport Classify(MinecraftHealthProbe probe)
    {
        var unit = ParseSystemd(probe.Systemd);
        var journal = probe.Journal ?? "";
        var fatal = HasFatalJournal(journal);
        var implicated = TryExtractImplicatedMod(journal);
        var excerpt = CapExcerpt(journal);

        if (LooksJoinable(probe.Rcon))
        {
            return new MinecraftReadinessReport(
                MinecraftReadinessKind.Joinable, unit, implicated, excerpt, fatal);
        }

        var crashLoop = unit.NRestarts >= CrashLoopRestartThreshold
            || string.Equals(unit.ActiveState, "failed", StringComparison.OrdinalIgnoreCase)
            || (unit.NRestarts >= 1 && fatal);

        if (fatal || crashLoop)
        {
            return new MinecraftReadinessReport(
                MinecraftReadinessKind.Crash, unit, implicated, excerpt, fatal);
        }

        return new MinecraftReadinessReport(
            MinecraftReadinessKind.StillStarting, unit, implicated, excerpt, fatal);
    }

    public static MinecraftReadinessReport Classify(string? rcon, string? systemd, string? journal) =>
        Classify(new MinecraftHealthProbe(rcon ?? "", systemd ?? "", journal ?? ""));

    public static bool HasFatalJournal(string? journal)
    {
        var text = journal ?? "";
        if (text.Length == 0)
            return false;
        if (text.Contains("/FATAL]", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("UnsupportedClassVersionError", StringComparison.Ordinal))
            return true;
        if (HasFatalInvalidDist(text))
            return true;
        if (text.Contains("caused the server to crash", StringComparison.OrdinalIgnoreCase)
            || text.Contains("caused the game to crash", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Could not execute entrypoint stage", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Failed to start the minecraft server", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("NoClassDefFoundError", StringComparison.Ordinal))
            return true;
        if (text.Contains("Exception in thread \"main\"", StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>
    /// Dedicated-server invalid-dist lines are only fatal when the loader aborts
    /// (FATAL / mixin prepare failed / crash report). A missing client mixin target
    /// on a dual-side library (CoFH Core) logs ERROR + "target was not found" and
    /// the server can still start.
    /// </summary>
    internal static bool HasFatalInvalidDist(string? journal)
    {
        var text = journal ?? "";
        if (!text.Contains("invalid dist DEDICATED_SERVER", StringComparison.OrdinalIgnoreCase))
            return false;
        if (text.Contains("Mixin prepare failed", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("InvalidMixinException", StringComparison.Ordinal))
            return true;
        if (text.Contains("/FATAL]", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("caused the server to crash", StringComparison.OrdinalIgnoreCase)
            || text.Contains("caused the game to crash", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Failed to start the minecraft server", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text.Contains("Exception in thread \"main\"", StringComparison.Ordinal))
            return true;
        if (text.Contains("@Mixin target", StringComparison.Ordinal)
            && text.Contains("was not found", StringComparison.OrdinalIgnoreCase))
            return false;
        return false;
    }

    public static string? TryExtractImplicatedMod(string? journal)
    {
        var text = journal ?? "";
        if (text.Length == 0)
            return null;

        var provided = ProvidedByMod.Match(text);
        if (provided.Success)
            return provided.Groups[1].Value;

        var caused = CausedCrashMod.Match(text);
        if (caused.Success)
            return caused.Groups[1].Value;

        var mixinFailed = MixinApplyFailed.Match(text);
        if (mixinFailed.Success)
            return mixinFailed.Groups[1].Value;

        var mixinFor = MixinApplyForMod.Match(text);
        if (mixinFor.Success)
            return mixinFor.Groups[1].Value;

        return null;
    }

    public static string CapExcerpt(string? journal, int maxLines = JournalExcerptMaxLines)
    {
        if (maxLines < 1)
            maxLines = 1;
        var text = (journal ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
        if (text.Length == 0)
            return "";

        var lines = text.Split('\n');
        var start = Math.Max(0, lines.Length - maxLines);
        var kept = new List<string>(Math.Min(maxLines, lines.Length - start));
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (line.Length > JournalLineMaxChars)
                line = line[..JournalLineMaxChars] + "…";
            kept.Add(line);
        }

        return string.Join('\n', kept).TrimEnd();
    }

    public static string FormatCrashMessage(MinecraftReadinessReport report)
    {
        var sb = new StringBuilder();
        sb.Append(CrashHeadline);
        var cause = CrashCauseLine(report);
        if (!string.IsNullOrWhiteSpace(cause))
        {
            sb.Append('\n');
            sb.Append(cause);
        }

        if (!string.IsNullOrWhiteSpace(report.JournalExcerpt))
        {
            sb.Append("\n\nRecent log:\n");
            sb.Append(report.JournalExcerpt);
        }

        return sb.ToString();
    }

    public static string FormatTimeoutMessage(MinecraftHealthProbe? last)
    {
        if (last is null)
            return TimeoutHeadline;

        var unit = ParseSystemd(last.Systemd);
        var active = string.IsNullOrWhiteSpace(unit.ActiveState) ? "unknown" : unit.ActiveState;
        var detail = TimeoutHeadline
            + " (minecraft=" + active
            + ", restarts=" + unit.NRestarts.ToString(CultureInfo.InvariantCulture)
            + ").";
        var excerpt = CapExcerpt(last.Journal, maxLines: 10);
        if (string.IsNullOrWhiteSpace(excerpt))
            return detail;
        return detail + "\n\nRecent log:\n" + excerpt;
    }

    public static string StillStartingLog(int attempt, MinecraftHealthProbe probe)
    {
        var unit = ParseSystemd(probe.Systemd);
        var active = string.IsNullOrWhiteSpace(unit.ActiveState) ? "unknown" : unit.ActiveState;
        return "RCON not ready yet (minecraft still starting; minecraft="
            + active
            + "); retry "
            + attempt.ToString(CultureInfo.InvariantCulture)
            + "/"
            + MaxRconAttempts.ToString(CultureInfo.InvariantCulture)
            + "…";
    }

    public static string CrashCauseLine(MinecraftReadinessReport report)
    {
        if (!string.IsNullOrWhiteSpace(report.ImplicatedMod))
        {
            return "Likely cause: mod '" + report.ImplicatedMod + "' (from the loader crash report).";
        }

        var journal = report.JournalExcerpt ?? "";
        if (journal.Contains("UnsupportedClassVersionError", StringComparison.Ordinal))
            return JavaTooOldCause;
        if (HasFatalInvalidDist(journal))
            return ClientDistCause;
        return "";
    }

    private static string SliceBetween(string text, string startMarker, string endMarker)
    {
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
            return "";
        start += startMarker.Length;
        var end = text.IndexOf(endMarker, start, StringComparison.Ordinal);
        if (end < 0)
            return text[start..].Trim();
        return text[start..end].Trim();
    }

    private static string SliceAfter(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return "";
        start += marker.Length;
        return start >= text.Length ? "" : text[start..].Trim();
    }

    private static bool TryField(string line, string key, out string value)
    {
        value = "";
        var prefix = key + "=";
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        value = line[prefix.Length..].Trim();
        return true;
    }

    private static string ShellSingleQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}

public enum MinecraftReadinessKind
{
    Joinable,
    StillStarting,
    Crash,
}

public sealed record MinecraftHealthProbe(string Rcon, string Systemd, string Journal);

public sealed record MinecraftUnitSnapshot(
    int NRestarts,
    string Result,
    string ActiveState,
    string SubState,
    int ExecMainStatus);

public sealed record MinecraftReadinessReport(
    MinecraftReadinessKind Kind,
    MinecraftUnitSnapshot Unit,
    string? ImplicatedMod,
    string JournalExcerpt,
    bool HasFatalJournal);
