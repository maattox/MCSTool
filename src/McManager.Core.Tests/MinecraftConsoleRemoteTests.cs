using McManager.Core.Services;
using Xunit;

namespace McManager.Core.Tests;

public sealed class MinecraftConsoleRemoteTests
{
    [Theory]
    [InlineData("list", "list")]
    [InlineData("  list  ", "list")]
    [InlineData("/say hi", "say hi")]
    [InlineData("/list", "list")]
    public void Normalize_strips_one_leading_slash(string raw, string expected)
    {
        Assert.True(MinecraftConsoleRemote.TryNormalizeCommand(raw, out var command, out var error));
        Assert.Null(error);
        Assert.Equal(expected, command);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData(" / ")]
    public void Normalize_rejects_empty(string? raw)
    {
        Assert.False(MinecraftConsoleRemote.TryNormalizeCommand(raw, out _, out var error));
        Assert.Equal(MinecraftConsoleRemote.EmptyCommandHint, error);
    }

    [Fact]
    public void Normalize_rejects_too_long()
    {
        var raw = new string('a', MinecraftConsoleRemote.MaxCommandChars + 1);
        Assert.False(MinecraftConsoleRemote.TryNormalizeCommand(raw, out _, out var error));
        Assert.Equal(MinecraftConsoleRemote.CommandTooLongHint, error);
    }

    [Fact]
    public void Rcon_remote_command_uses_localhost_secret_and_base64_payload()
    {
        Assert.True(MinecraftConsoleRemote.TryBuildRconCommand("/say hello world", out var remote, out var error));
        Assert.Null(error);
        Assert.StartsWith("sudo python3 -c ", remote, StringComparison.Ordinal);
        Assert.Contains("127.0.0.1", remote, StringComparison.Ordinal);
        Assert.Contains("25575", remote, StringComparison.Ordinal);
        Assert.Contains("/etc/mcmgr/rcon.secret", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("0.0.0.0", remote, StringComparison.Ordinal);
        Assert.DoesNotContain("say hello world", remote, StringComparison.Ordinal);
        Assert.Contains(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("say hello world")), remote, StringComparison.Ordinal);
    }

    [Fact]
    public void Logs_command_is_journalctl_cat_for_minecraft_unit()
    {
        var cmd = MinecraftConsoleRemote.LogsCommand(200);
        Assert.Equal("sudo journalctl -u minecraft -n 200 --no-pager -o cat", cmd);
        Assert.DoesNotContain("25575", cmd, StringComparison.Ordinal);
    }

    [Fact]
    public void Log_line_count_is_clamped()
    {
        Assert.Equal(50, MinecraftConsoleRemote.ClampLogLines(1));
        Assert.Equal(500, MinecraftConsoleRemote.ClampLogLines(9999));
        Assert.Equal(200, MinecraftConsoleRemote.ClampLogLines(200));
    }

    [Fact]
    public void Send_requires_joinable_minecraft_and_a_command()
    {
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: false, busy: false, "list"));
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: true, "list"));
        Assert.False(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: false, ""));
        Assert.True(MinecraftConsoleRemote.CanSend(minecraftJoinable: true, busy: false, "/list"));
        Assert.Equal(
            MinecraftConsoleRemote.VmStoppedHint,
            MinecraftConsoleRemote.SendDisabledReason(vm1Running: false, minecraftJoinable: false, busy: false, "list"));
        Assert.Equal(
            MinecraftConsoleRemote.MinecraftStoppedHint,
            MinecraftConsoleRemote.SendDisabledReason(vm1Running: true, minecraftJoinable: false, busy: false, "list"));
    }

    [Fact]
    public void Refresh_requires_vm1_running()
    {
        Assert.False(MinecraftConsoleRemote.CanRefresh(vm1Running: false, busy: false));
        Assert.True(MinecraftConsoleRemote.CanRefresh(vm1Running: true, busy: false));
        Assert.False(MinecraftConsoleRemote.CanRefresh(vm1Running: true, busy: true));
    }

    [Fact]
    public void Transcript_line_prefixes_the_command()
    {
        var line = MinecraftConsoleRemote.FormatTranscriptLine("list", "There are 0 of a max of 20 players online:");
        Assert.StartsWith("> list", line, StringComparison.Ordinal);
        Assert.Contains("There are 0", line, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("There are 0 of a max of 20 players online:", 0, 20)]
    [InlineData("There are 1 of a max of 20 players online:", 1, 20)]
    [InlineData("There are 1 of a max of 20 players online:\nSteve", 1, 20)]
    [InlineData("There are 12 of a max of 40 players online", 12, 40)]
    public void Parses_vanilla_player_list(string payload, int online, int max)
    {
        Assert.True(MinecraftConsoleRemote.TryParsePlayerList(payload, out var parsedOnline, out var parsedMax));
        Assert.Equal(online, parsedOnline);
        Assert.Equal(max, parsedMax);
    }

    [Fact]
    public void Parses_list_uuids_names_and_hyphenated_uuids()
    {
        const string payload =
            "There are 2 of a max of 20 players online: "
            + "Steve (069a79f4-44e9-4726-a5be-fca90e38aaf5), "
            + "Alex (61699b2e-d327-4a01-9f1e-0ea8c3f06bc6)";

        Assert.True(MinecraftConsoleRemote.TryParsePlayerList(
            payload, out var online, out var max, out var players));
        Assert.Equal(2, online);
        Assert.Equal(20, max);
        Assert.Equal(2, players.Count);
        Assert.Equal("Steve", players[0].Name);
        Assert.Equal("069a79f444e94726a5befca90e38aaf5", players[0].UuidHyphenless);
        Assert.True(players[0].HasUuid);
        Assert.Equal("Alex", players[1].Name);
        Assert.Equal("61699b2ed3274a019f1e0ea8c3f06bc6", players[1].UuidHyphenless);
    }

    [Fact]
    public void Parses_list_uuids_empty_and_newline_wrapped()
    {
        Assert.True(MinecraftConsoleRemote.TryParsePlayerList(
            "There are 0 of a max of 20 players online:",
            out _,
            out _,
            out var empty));
        Assert.Empty(empty);

        const string wrapped =
            "There are 1 of a max of 20 players online:\n"
            + "Steve (069a79f4-44e9-4726-a5be-fca90e38aaf5)";
        Assert.True(MinecraftConsoleRemote.TryParsePlayerList(wrapped, out var online, out _, out var players));
        Assert.Equal(1, online);
        Assert.Single(players);
        Assert.Equal("Steve", players[0].Name);
    }

    [Fact]
    public void Parses_list_uuids_strips_section_codes()
    {
        const string payload =
            "§eThere are 1 of a max of 20 players online:§r "
            + "Steve (069a79f4-44e9-4726-a5be-fca90e38aaf5)";
        Assert.True(MinecraftConsoleRemote.TryParsePlayerList(payload, out var online, out _, out var players));
        Assert.Equal(1, online);
        Assert.Equal("Steve", Assert.Single(players).Name);
    }

    [Fact]
    public void Hyphenless_uuid_rejects_short_or_non_hex()
    {
        Assert.Equal(
            "069a79f444e94726a5befca90e38aaf5",
            MinecraftConsoleRemote.ToHyphenlessUuid("069a79f4-44e9-4726-a5be-fca90e38aaf5"));
        Assert.Equal(
            "069a79f444e94726a5befca90e38aaf5",
            MinecraftConsoleRemote.ToHyphenlessUuid("069A79F444E94726A5BEFCA90E38AAF5"));
        Assert.Equal("", MinecraftConsoleRemote.ToHyphenlessUuid("steve"));
        Assert.Equal("", MinecraftConsoleRemote.ToHyphenlessUuid(""));
        Assert.Equal("", MinecraftConsoleRemote.ToHyphenlessUuid("069a79f4-44e9"));
    }

    [Fact]
    public void Player_action_commands_append_reason_only_for_kick_and_ban()
    {
        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "Kick", "Steve", "be nice", out var kick, out var error));
        Assert.Null(error);
        Assert.Equal("kick Steve be nice", kick);

        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "op", "Steve", "ignored", out var op, out _));
        Assert.Equal("op Steve", op);

        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "deop", "Steve", null, out var deop, out _));
        Assert.Equal("deop Steve", deop);

        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "ban", "Steve", "griefing", out var ban, out _));
        Assert.Equal("ban Steve griefing", ban);

        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "kick", "Steve", "  \n  ", out var kickNoReason, out _));
        Assert.Equal("kick Steve", kickNoReason);
    }

    [Fact]
    public void Pardon_builds_without_reason()
    {
        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "pardon", "Steve", "ignored", out var pardon, out var error));
        Assert.Null(error);
        Assert.Equal("pardon Steve", pardon);
    }

    [Fact]
    public void Parses_empty_banlist()
    {
        Assert.True(MinecraftConsoleRemote.TryParseBanList("There are no bans", out var none));
        Assert.Empty(none);

        Assert.True(MinecraftConsoleRemote.TryParseBanList("There are 0 ban(s):", out var zero));
        Assert.Empty(zero);
    }

    [Fact]
    public void Parses_one_banlist_was_banned_by_line()
    {
        const string payload = "There are 1 ban(s):\nSteve was banned by Server: griefing";
        Assert.True(MinecraftConsoleRemote.TryParseBanList(payload, out var banned));
        var player = Assert.Single(banned);
        Assert.Equal("Steve", player.Name);
        Assert.False(player.HasUuid);
    }

    [Fact]
    public void Parses_two_banlist_names()
    {
        const string payload =
            "There are 2 ban(s):\n"
            + "Steve was banned by Server: griefing\n"
            + "Alex was banned by Console: spam";
        Assert.True(MinecraftConsoleRemote.TryParseBanList(payload, out var banned));
        Assert.Equal(2, banned.Count);
        Assert.Equal("Steve", banned[0].Name);
        Assert.Equal("Alex", banned[1].Name);
    }

    [Fact]
    public void Parses_banlist_comma_names_when_no_was_banned_by_lines()
    {
        Assert.True(MinecraftConsoleRemote.TryParseBanList(
            "There are 2 ban(s): Steve, Alex", out var banned));
        Assert.Equal(2, banned.Count);
        Assert.Equal("Steve", banned[0].Name);
        Assert.Equal("Alex", banned[1].Name);
        Assert.False(banned[0].HasUuid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RCON authentication failed")]
    [InlineData("There are players online")]
    public void Banlist_parse_rejects_garbage(string? payload)
    {
        Assert.False(MinecraftConsoleRemote.TryParseBanList(payload, out var banned));
        Assert.Empty(banned);
    }

    [Fact]
    public void Player_action_commands_reject_bad_names_and_strip_control_in_reason()
    {
        Assert.False(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "kick", "bad name", null, out _, out var error));
        Assert.Equal("That is not a Minecraft username.", error);

        Assert.True(MinecraftConsoleRemote.TryBuildPlayerActionCommand(
            "ban", "Steve", "line\r\none\0two", out var ban, out _));
        Assert.Equal("ban Steve lineonetwo", ban);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("RCON authentication failed")]
    [InlineData("There are players online")]
    public void Player_list_parse_rejects_non_vanilla(string? payload)
    {
        Assert.False(MinecraftConsoleRemote.TryParsePlayerList(payload, out _, out _));
    }

    [Fact]
    public void Players_pin_is_zero_when_stopped_and_count_when_running()
    {
        Assert.Equal("0", MinecraftConsoleRemote.FormatPlayersPin(false, null, null));
        Assert.Equal("0", MinecraftConsoleRemote.FormatPlayersPin(false, 3, 20));
        Assert.Equal("—", MinecraftConsoleRemote.FormatPlayersPin(true, null, null));
        Assert.Equal("1 / 20", MinecraftConsoleRemote.FormatPlayersPin(true, 1, 20));
        Assert.Equal("0 / 20", MinecraftConsoleRemote.FormatPlayersPin(true, 0, 20));
        Assert.Equal("2", MinecraftConsoleRemote.FormatPlayersPin(true, 2, null));
    }

    [Fact]
    public void Unreachable_rcon_uses_novice_copy()
    {
        var run = SshExecResult.Fail("could not reach Minecraft RCON on localhost", exitStatus: 5);
        Assert.Equal(MinecraftConsoleRemote.RconUnreachableHint, MinecraftConsoleRemote.OperatorHintFromRcon(run));
    }

    [Fact]
    public void Filter_simple_log_drops_rcon_plumbing_keeps_game_and_transcript()
    {
        var full =
            "Starting net.minecraft.server...\n"
            + "[16:00:00] [Server thread/INFO]: Starting RCON listener\n"
            + "[16:00:00] [Server thread/INFO]: RCON running on 0.0.0.0:25575\n"
            + "[16:00:01] [RCON Listener #1/INFO]: Thread RCON Client /127.0.0.1 started\n"
            + "[16:00:01] [RCON Listener #1/INFO]: Thread RCON Client /127.0.0.1 shutting down\n"
            + "[16:00:02] [Server thread/INFO]: Steve joined the game\n"
            + "[16:00:03] [Server thread/INFO]: <Steve> hello\n"
            + "[Rcon: Steve issued server command: list]\n"
            + "> list\n"
            + "There are 0 of a max of 20 players online:\n"
            + "[16:00:04] [Server thread/ERROR]: Something broke\n"
            + "FAIL RCON bind on startup";

        var simple = MinecraftConsoleRemote.FilterSimpleLog(full);

        Assert.DoesNotContain("Thread RCON Client", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting RCON listener", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("RCON running on", simple, StringComparison.Ordinal);
        Assert.Contains("Steve joined the game", simple, StringComparison.Ordinal);
        Assert.Contains("<Steve> hello", simple, StringComparison.Ordinal);
        Assert.Contains("[Rcon: Steve issued server command: list]", simple, StringComparison.Ordinal);
        Assert.Contains("> list", simple, StringComparison.Ordinal);
        Assert.Contains("There are 0 of a max of 20 players online:", simple, StringComparison.Ordinal);
        Assert.Contains("Something broke", simple, StringComparison.Ordinal);
        Assert.Contains("FAIL RCON bind on startup", simple, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_simple_log_drops_modded_boot_noise_keeps_spawn_progress_and_errors()
    {
        var full =
            "-- Logs begin at Fri 2026-08-21 21:44:51 UTC, end at Fri 2026-08-21 21:45:00 UTC. --\n"
            + "[21:44:51] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher running: args [--launchTarget, forgeserver, --nogui]\n"
            + "[21:44:51] [main/INFO] [cp.mo.mo.Launcher/MODLAUNCHER]: ModLauncher 10.0.9 starting: java version 17.0.20\n"
            + "[21:44:52] [main/INFO] [mixin/]: SpongePowered MIXIN Subsystem Version=0.8.5\n"
            + "[21:44:57] [main/WARN] [mixin/]: Reference map 'yungsextras.refmap.json' could not be read\n"
            + "[21:44:59] [main/WARN] [mixin/]: Error loading class: dev/tr7zw/skinlayers/render/CustomizableModelPart (java.lang.ClassNotFoundException)\n"
            + "[21:44:58] [main/INFO] [STDOUT/]: [org.valkyrienskies.mod.forge.mixin.ValkyrienForgeMixinConfigPlugin:onLoad:32]: six-seven\n"
            + "[Server thread/INFO]: Preparing spawn area: 45%\n"
            + "[Server thread/INFO]: Done (12.345s)! For help, type help\n"
            + "[21:45:00] [main/ERROR] [ne.mi.fm.lo.RuntimeDistCleaner/DISTXFORM]: Attempted to load class for invalid dist DEDICATED_SERVER\n"
            + "[21:45:00] [main/FATAL] [mixin/]: Mixin prepare failed preparing LivingEntityMixin\n"
            + "[16:00:01] [Netty Server IO #1/INFO]: Channel read complete\n"
            + "[16:00:01] [RCON Listener #2/INFO]: Thread RCON Client /127.0.0.1 authenticated\n";

        var simple = MinecraftConsoleRemote.FilterSimpleLog(full);

        Assert.DoesNotContain("ModLauncher running", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("SpongePowered MIXIN", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Reference map", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Error loading class", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("six-seven", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Logs begin at", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Netty", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Thread RCON Client", simple, StringComparison.Ordinal);
        Assert.Contains("Preparing spawn area: 45%", simple, StringComparison.Ordinal);
        Assert.Contains("Done (12.345s)!", simple, StringComparison.Ordinal);
        Assert.Contains("invalid dist DEDICATED_SERVER", simple, StringComparison.Ordinal);
        Assert.Contains("Mixin prepare failed", simple, StringComparison.Ordinal);
    }

    [Fact]
    public void Filter_simple_log_keeps_world_prep_fixture()
    {
        var fixturePath = Path.Combine(
            FindRepoRoot(),
            "tests",
            "fixtures",
            "journals",
            "still-loading-spawn-area.txt");
        var full = File.ReadAllText(fixturePath);
        var simple = MinecraftConsoleRemote.FilterSimpleLog(full);

        Assert.Contains("Preparing spawn area: 12%", simple, StringComparison.Ordinal);
        Assert.Contains("Preparing spawn area: 78%", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Starting minecraft server version", simple, StringComparison.Ordinal);
        Assert.DoesNotContain("Generating keypair", simple, StringComparison.Ordinal);
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "config.local.example.json")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName ?? "";
        }

        throw new InvalidOperationException("Could not find repo root from test output directory.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Filter_simple_log_empty_input_returns_empty(string? full)
    {
        Assert.Equal("", MinecraftConsoleRemote.FilterSimpleLog(full));
    }
}
