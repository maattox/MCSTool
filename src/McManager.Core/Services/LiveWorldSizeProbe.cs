using System.Globalization;

namespace McManager.Core.Services;

/// <summary>
/// SSH probe of the live VM1 world folder size (<c>du -sb</c>).
/// Not Object Storage backup zip size.
/// </summary>
public static class LiveWorldSizeProbe
{
    public static readonly TimeSpan RefreshCooldown = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(2);

    public const string MissingMarker = "MCMGR_WORLD_MISSING";

    public const string VmStoppedHint =
        "Start the server to measure the live world folder.";

    public const string MeasuringHint = "Measuring the live world folder…";

    public const string EnabledHint =
        "Measure the live world folder on the game VM. At most every two minutes.";

    public const string DisplayTitle =
        "Uncompressed size of the world folder on the game VM. Backup storage is compressed zips in cloud storage.";

    public static bool TryCreateCommand(string? worldPath, out string command, out string? error)
    {
        command = "";
        if (!WorldWipe.TryNormalizeWorldPath(worldPath, out var path, out error))
            return false;

        var quoted = SshShell.Quote(path);
        var script =
            "set -uo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"DIR={quoted}; "
            + "if sudo test -d \"$DIR\"; then sudo du -sb -- \"$DIR\"; "
            + $"else echo {SshShell.Quote(MissingMarker)}; fi";
        command = "sudo bash -c " + SshShell.Quote(script);
        return true;
    }

    public static bool TryParse(string? stdout, out long bytes, out string? error)
    {
        bytes = 0;
        error = null;
        var text = stdout ?? "";
        if (text.Contains(MissingMarker, StringComparison.Ordinal))
        {
            error = "The world folder is not on the game VM yet.";
            return false;
        }

        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (line.Length == 0)
                continue;

            var splitAt = line.IndexOfAny(['\t', ' ']);
            var number = splitAt < 0 ? line : line[..splitAt];
            if (long.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed >= 0)
            {
                bytes = parsed;
                return true;
            }
        }

        error = "Could not parse the live world size.";
        return false;
    }

    public static string FormatGb(long bytes)
    {
        var gb = bytes / (1024d * 1024d * 1024d);
        return gb.ToString("F1", CultureInfo.InvariantCulture) + " GB";
    }

    public static string FormatDisplay(long? bytes, bool vmRunning, bool measuring)
    {
        if (measuring)
            return "…";
        if (!vmRunning || bytes is null)
            return "—";
        return FormatGb(bytes.Value);
    }

    public static TimeSpan CooldownRemaining(DateTimeOffset nowUtc, DateTimeOffset? lastAttemptUtc)
    {
        if (lastAttemptUtc is null)
            return TimeSpan.Zero;
        var left = RefreshCooldown - (nowUtc - lastAttemptUtc.Value);
        return left < TimeSpan.Zero ? TimeSpan.Zero : left;
    }

    public static bool CanRefresh(
        bool vmRunning,
        bool measuring,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastAttemptUtc)
    {
        if (!vmRunning || measuring)
            return false;
        return CooldownRemaining(nowUtc, lastAttemptUtc) == TimeSpan.Zero;
    }

    public static string RefreshTitle(
        bool vmRunning,
        bool measuring,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastAttemptUtc)
    {
        if (measuring)
            return MeasuringHint;
        if (!vmRunning)
            return VmStoppedHint;

        var remaining = CooldownRemaining(nowUtc, lastAttemptUtc);
        if (remaining > TimeSpan.Zero)
        {
            if (remaining.TotalSeconds <= 90)
                return "Wait a moment before measuring again.";
            var minutes = Math.Max(2, (int)Math.Ceiling(remaining.TotalMinutes));
            return $"Wait about {minutes} minutes before measuring again.";
        }

        return EnabledHint;
    }
}
