using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MinecraftReadinessTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "journals", name));

    private static string Systemd(int nRestarts, string active = "activating", int exec = 1, string result = "exit-code") =>
        "NRestarts=" + nRestarts
        + "\nResult=" + result
        + "\nActiveState=" + active
        + "\nSubState=auto-restart"
        + "\nExecMainStatus=" + exec;

    [Fact]
    public void Probe_uses_localhost_rcon_and_journalctl_not_security_list()
    {
        var cmd = MinecraftReadiness.ProbeCommand("2026-08-21 23:19:00");
        Assert.Contains("127.0.0.1", cmd, StringComparison.Ordinal);
        Assert.Contains("25575", cmd, StringComparison.Ordinal);
        Assert.Contains("/etc/mcmgr/rcon.secret", cmd, StringComparison.Ordinal);
        Assert.Contains("sudo journalctl -u minecraft -n 80 --no-pager -o cat", cmd, StringComparison.Ordinal);
        Assert.Contains("--since '2026-08-21 23:19:00 UTC'", cmd, StringComparison.Ordinal);
        Assert.Contains("NRestarts", cmd, StringComparison.Ordinal);
        Assert.Contains(MinecraftReadiness.RconMarker, cmd, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", cmd, StringComparison.Ordinal);
        Assert.Equal(
            "sudo journalctl -u minecraft -n 80 --no-pager -o cat",
            MinecraftConsoleRemote.LogsCommand(80));
    }

    [Fact]
    public void Probe_omits_since_when_timestamp_is_not_safe()
    {
        var cmd = MinecraftReadiness.ProbeCommand("2026-08-21; rm -rf /");
        Assert.DoesNotContain("--since", cmd, StringComparison.Ordinal);
        Assert.False(MinecraftReadiness.IsSafeSinceTimestamp("2026-08-21; rm -rf /"));
        Assert.True(MinecraftReadiness.IsSafeSinceTimestamp("2026-08-21 23:19:00"));
    }

    [Fact]
    public void Forge_mixin_target_not_found_on_client_class_is_not_fatal()
    {
        var journal = Fixture("forge-mixin-target-not-found-benign.txt");
        Assert.False(MinecraftReadiness.HasFatalJournal(journal));
        var report = MinecraftReadiness.Classify(
            "could not reach Minecraft RCON on localhost",
            Systemd(nRestarts: 1, active: "active", exec: 0, result: "success"),
            journal);
        Assert.Equal(MinecraftReadinessKind.StillStarting, report.Kind);
        Assert.True(string.IsNullOrEmpty(MinecraftReadiness.CrashCauseLine(report)));
    }

    [Fact]
    public void Forge_mixin_invalid_dist_fails_fast_with_mod_name()
    {
        var journal = Fixture("forge-mixin-invalid-dist.txt");
        var report = MinecraftReadiness.Classify(
            "could not reach Minecraft RCON on localhost",
            Systemd(nRestarts: 1),
            journal);
        Assert.Equal(MinecraftReadinessKind.Crash, report.Kind);
        Assert.Equal("exampleclientmod", report.ImplicatedMod);
        var message = MinecraftReadiness.FormatCrashMessage(report);
        Assert.Contains(MinecraftReadiness.CrashHeadline, message, StringComparison.Ordinal);
        Assert.Contains("exampleclientmod", message, StringComparison.Ordinal);
        Assert.Contains("/FATAL]", message, StringComparison.Ordinal);
        Assert.DoesNotContain("RCON list did not succeed in time", message, StringComparison.Ordinal);
        Assert.DoesNotContain("holdmyitems", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Fabric_noclassdeffound_abort_fails_fast_with_provided_by_mod()
    {
        var journal = Fixture("fabric-noclassdeffound-abort.txt");
        var report = MinecraftReadiness.Classify(
            "",
            Systemd(nRestarts: 0, active: "active", exec: 0, result: "success"),
            journal);
        Assert.Equal(MinecraftReadinessKind.Crash, report.Kind);
        Assert.Equal("exampleguimod", report.ImplicatedMod);
        var message = MinecraftReadiness.FormatCrashMessage(report);
        Assert.Contains("exampleguimod", message, StringComparison.Ordinal);
        Assert.Contains("NoClassDefFoundError", message, StringComparison.Ordinal);
        Assert.DoesNotContain("mod-loading-screen", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unsupported_class_version_fails_fast_without_inventing_a_mod()
    {
        var journal = Fixture("unsupported-class-version.txt");
        var report = MinecraftReadiness.Classify("", Systemd(nRestarts: 1), journal);
        Assert.Equal(MinecraftReadinessKind.Crash, report.Kind);
        Assert.Null(report.ImplicatedMod);
        var message = MinecraftReadiness.FormatCrashMessage(report);
        Assert.Contains(MinecraftReadiness.JavaTooOldCause, message, StringComparison.Ordinal);
        Assert.Contains("UnsupportedClassVersionError", message, StringComparison.Ordinal);
        Assert.Contains("class file version 69.0", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Healthy_spawn_area_sample_keeps_waiting()
    {
        var journal = Fixture("still-loading-spawn-area.txt");
        var report = MinecraftReadiness.Classify(
            "connection refused",
            Systemd(nRestarts: 0, active: "active", exec: 0, result: "success"),
            journal);
        Assert.Equal(MinecraftReadinessKind.StillStarting, report.Kind);
        var log = MinecraftReadiness.StillStartingLog(3, new MinecraftHealthProbe("", Systemd(0, "active", 0, "success"), journal));
        Assert.Contains("still starting", log, StringComparison.Ordinal);
        Assert.Contains("retry 3/12", log, StringComparison.Ordinal);
        Assert.DoesNotContain("crashed", log, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rcon_list_success_is_joinable_even_if_old_fatal_is_in_the_buffer()
    {
        var journal = Fixture("forge-mixin-invalid-dist.txt");
        var report = MinecraftReadiness.Classify(
            "There are 0 of a max of 20 players online",
            Systemd(nRestarts: 0, active: "active", exec: 0, result: "success"),
            journal);
        Assert.Equal(MinecraftReadinessKind.Joinable, report.Kind);
    }

    [Fact]
    public void Restart_loop_without_journal_is_still_a_crash()
    {
        var report = MinecraftReadiness.Classify(
            "",
            Systemd(nRestarts: 2, active: "failed", exec: 1),
            "");
        Assert.Equal(MinecraftReadinessKind.Crash, report.Kind);
        var message = MinecraftReadiness.FormatCrashMessage(report);
        Assert.Contains(MinecraftReadiness.CrashHeadline, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Single_restart_without_fatal_journal_is_not_yet_a_crash()
    {
        var report = MinecraftReadiness.Classify(
            "",
            Systemd(nRestarts: 1, active: "activating", exec: 1),
            "[Server thread/INFO]: Loading properties\n");
        Assert.Equal(MinecraftReadinessKind.StillStarting, report.Kind);
    }

    [Fact]
    public void Timeout_copy_says_rcon_never_came_up_without_calling_it_a_crash()
    {
        var probe = new MinecraftHealthProbe(
            "could not reach Minecraft RCON on localhost",
            Systemd(0, "active", 0, "success"),
            Fixture("still-loading-spawn-area.txt"));
        var message = MinecraftReadiness.FormatTimeoutMessage(probe);
        Assert.Contains("RCON list did not succeed in time", message, StringComparison.Ordinal);
        Assert.Contains("not crash-looping", message, StringComparison.Ordinal);
        Assert.Contains("minecraft=active", message, StringComparison.Ordinal);
        Assert.Contains("restarts=0", message, StringComparison.Ordinal);
        Assert.Contains("Preparing spawn area", message, StringComparison.Ordinal);
        Assert.DoesNotContain(MinecraftReadiness.CrashHeadline, message, StringComparison.Ordinal);
    }

    [Fact]
    public void Journal_excerpt_is_capped_at_thirty_lines()
    {
        var lines = string.Join('\n', Enumerable.Range(1, 80).Select(i => "line " + i));
        var excerpt = MinecraftReadiness.CapExcerpt(lines);
        var kept = excerpt.Split('\n');
        Assert.Equal(MinecraftReadiness.JournalExcerptMaxLines, kept.Length);
        Assert.Equal("line 51", kept[0]);
        Assert.Equal("line 80", kept[^1]);
    }

    [Fact]
    public void Parse_probe_splits_markers()
    {
        var blob = MinecraftReadiness.RconMarker
            + "\nThere are 0 of a max of 20 players online\n"
            + MinecraftReadiness.SystemdMarker
            + "\nNRestarts=0\nActiveState=active\n"
            + MinecraftReadiness.JournalMarker
            + "\n[Server thread/INFO]: Done\n";
        var probe = MinecraftReadiness.ParseProbe(blob);
        Assert.Contains("There are 0", probe.Rcon, StringComparison.Ordinal);
        Assert.Contains("ActiveState=active", probe.Systemd, StringComparison.Ordinal);
        Assert.Contains("Done", probe.Journal, StringComparison.Ordinal);
        Assert.Equal(MinecraftReadinessKind.Joinable, MinecraftReadiness.Classify(probe).Kind);
    }

    [Fact]
    public void Stop_command_does_not_wipe_server_files()
    {
        Assert.Equal("sudo systemctl stop minecraft", MinecraftReadiness.StopUnitCommand);
        Assert.DoesNotContain("rm ", MinecraftReadiness.StopUnitCommand, StringComparison.Ordinal);
        Assert.Equal(12, MinecraftReadiness.MaxRconAttempts);
        Assert.Equal(TimeSpan.FromSeconds(10), MinecraftReadiness.RetryDelay);
    }
}
