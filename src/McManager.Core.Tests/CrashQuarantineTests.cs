using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class CrashQuarantineTests
{
    [Fact]
    public void Notify_client_only_vs_unknown()
    {
        var client = CrashQuarantine.NotifyMessage("iris", "mods/iris-1.jar", likelyClientOnly: true, retrySucceeded: true);
        Assert.Contains("iris", client, StringComparison.Ordinal);
        Assert.Contains("started without it", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("client-only", client, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Keep excluded", client, StringComparison.Ordinal);
        Assert.DoesNotContain("may be required", client, StringComparison.OrdinalIgnoreCase);

        var unknown = CrashQuarantine.NotifyMessage("worldgen", "mods/worldgen-1.jar", likelyClientOnly: false, retrySucceeded: false);
        Assert.Contains("still failed", unknown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("may be required", unknown, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("client-only", unknown, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Remote_commands_are_sudo_python_and_quote_user_values()
    {
        var move = CrashQuarantine.RemoteCommand("move", modId: "badmod", jarFileName: "badmod-1.jar");
        Assert.Contains("sudo bash -c", move, StringComparison.Ordinal);
        Assert.Contains("quarantine_mod.py", move, StringComparison.Ordinal);
        Assert.Contains("move", move, StringComparison.Ordinal);
        Assert.Contains("--restart", move, StringComparison.Ordinal);
        Assert.Contains("'badmod'", move, StringComparison.Ordinal);
        Assert.DoesNotContain("rm -rf", move, StringComparison.Ordinal);
        Assert.DoesNotContain("excluded_client_only", move, StringComparison.Ordinal);

        var evil = CrashQuarantine.RemoteCommand("restore", relativePath: "mods/foo'; rm -rf /#");
        Assert.Contains("restore", evil, StringComparison.Ordinal);
        Assert.Contains("'\\''", evil, StringComparison.Ordinal);
        Assert.Contains("sudo bash -c", evil, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_remote_json_and_manifest_entries()
    {
        var parsed = CrashQuarantine.ParseRemote(
            "noise\n{\"ok\":true,\"mod_id\":\"badmod\",\"path\":\"mods/badmod-1.jar\",\"likely_client_only\":true}\n");
        Assert.True(parsed.Ok);
        Assert.Equal("badmod", parsed.ModId);
        Assert.Equal("mods/badmod-1.jar", parsed.Path);
        Assert.True(parsed.LikelyClientOnly);

        var fail = CrashQuarantine.ParseRemote("{\"ok\":false,\"error\":\"Could not match exactly one jar for the blamed mod.\"}");
        Assert.False(fail.Ok);
        Assert.Contains("exactly one jar", fail.Error, StringComparison.OrdinalIgnoreCase);

        var entries = CrashQuarantine.ParseManifestEntries(
            """
            {"modpack":{"quarantined_files":[
              {"path":"mods/badmod-1.jar","reason":"crash_attributed_by_loader_report",
               "detected_at":"2026-08-23T00:00:00Z","retry_succeeded":true,"operator_acknowledged":false}
            ]}}
            """);
        Assert.Single(entries);
        Assert.Equal("mods/badmod-1.jar", entries[0].Path);
        Assert.True(entries[0].RetrySucceeded);
        Assert.True(entries[0].NeedsAck);
        Assert.Equal("badmod", entries[0].DisplayName);
        Assert.Contains("started without it", CrashQuarantine.EntryCopy(entries[0], likelyClientOnly: true), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Does_not_treat_list_exclude_as_layer3_fold()
    {
        Assert.True(CrashQuarantine.GuessClientOnlyFromLists("mods/iris-1.7.jar", "iris"));
        Assert.False(CrashQuarantine.GuessClientOnlyFromLists("mods/lithium-fabric.jar", "lithium"));
    }
}
