using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace McManager.Core.Services;

/// <summary>
/// Manager Console tab: SSH to VM1, then localhost RCON + <c>journalctl -u minecraft</c>.
/// Not a Java PTY. RCON stays <c>127.0.0.1:25575</c> on the box — never a Security List rule.
/// </summary>
public static class MinecraftConsoleRemote
{
    public const int RconPort = 25575;
    public const string RconBind = "127.0.0.1";
    public const string RconSecretPath = "/etc/mcmgr/rcon.secret";
    public const string MinecraftUnit = "minecraft";
    public const int DefaultLogLines = 200;
    public const int MinLogLines = 50;
    public const int MaxLogLines = 500;
    public const int MaxCommandChars = 512;

    public const string HelpTitle =
        "Send commands as if you typed them in the Minecraft server console. "
        + "Recent logs show player-facing activity by default (chat, joins, commands, errors); "
        + "switch to Full for the raw service log including RCON and modloader startup noise. "
        + "This is not a live terminal.";

    public const string SimpleLogEmptyHint =
        "No simplified log lines in this buffer. Switch to Full to see the raw service log.";

    public const string Intro =
        "Send Minecraft commands and read recent logs. Start the server first. "
        + "This is not a live terminal.";

    public const string EmptyLogs =
        "No log lines yet. Start the server, then Refresh.";

    public const string VmStoppedHint =
        "Start the server first. Console needs the server running.";

    public const string MinecraftStoppedHint =
        "Minecraft is stopped. Start the server, then send commands.";

    public const string EmptyCommandHint = "Type a command, then Send.";

    public const string CommandTooLongHint = "That command is too long.";

    public const string RconUnreachableHint =
        "Could not reach Minecraft. Is the server Running?";

    private static readonly Regex VanillaPlayerList = new(
        @"There are (\d+) of a max of (\d+) players online",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// One-shot Python: auth with the on-box secret, send one command to localhost RCON.
    /// Payload is argv[1] (base64) so the operator command is never interpolated into the shell.
    /// </summary>
    public const string RconPython =
        "import base64,socket,struct,sys\n"
        + "cmd=base64.standard_b64decode(sys.argv[1]).decode('utf-8')\n"
        + "if not cmd.strip():\n"
        + " sys.stderr.write('empty command\\n'); sys.exit(2)\n"
        + "try:\n"
        + " pw=open('/etc/mcmgr/rcon.secret').read().strip()\n"
        + "except OSError:\n"
        + " sys.stderr.write('could not read rcon secret\\n'); sys.exit(3)\n"
        + "def pkt(k,p):\n"
        + " b=struct.pack('<ii',1,k)+p.encode('utf-8')+b'\\x00\\x00'\n"
        + " return struct.pack('<i',len(b))+b\n"
        + "def recvall(s,n):\n"
        + " buf=b''\n"
        + " while len(buf)<n:\n"
        + "  chunk=s.recv(n-len(buf))\n"
        + "  if not chunk: raise OSError('closed')\n"
        + "  buf+=chunk\n"
        + " return buf\n"
        + "def readpkt(s):\n"
        + " (length,)=struct.unpack('<i',recvall(s,4))\n"
        + " data=recvall(s,length)\n"
        + " rid,_=struct.unpack('<ii',data[:8])\n"
        + " return rid,data[8:-2].decode('utf-8','replace')\n"
        + "try:\n"
        + " s=socket.create_connection(('127.0.0.1',25575),timeout=8)\n"
        + " s.sendall(pkt(3,pw)); rid,_=readpkt(s)\n"
        + " if rid==-1:\n"
        + "  sys.stderr.write('RCON authentication failed\\n'); sys.exit(4)\n"
        + " s.sendall(pkt(2,cmd)); _,payload=readpkt(s); s.close()\n"
        + " sys.stdout.write(payload)\n"
        + "except OSError:\n"
        + " sys.stderr.write('could not reach Minecraft RCON on localhost\\n'); sys.exit(5)\n";

    public static int ClampLogLines(int lineCount)
    {
        if (lineCount < MinLogLines)
            return MinLogLines;
        if (lineCount > MaxLogLines)
            return MaxLogLines;
        return lineCount;
    }

    public static string LogsCommand(int lineCount = DefaultLogLines)
    {
        var n = ClampLogLines(lineCount);
        return "sudo journalctl -u " + MinecraftUnit + " -n " + n.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " --no-pager -o cat";
    }

    public static bool TryNormalizeCommand(string? raw, out string command, out string? error)
    {
        command = "";
        error = null;
        var text = (raw ?? "").Trim();
        if (text.StartsWith('/'))
            text = text[1..].Trim();
        if (text.Length == 0)
        {
            error = EmptyCommandHint;
            return false;
        }

        if (text.Length > MaxCommandChars)
        {
            error = CommandTooLongHint;
            return false;
        }

        command = text;
        return true;
    }

    public static bool TryBuildRconCommand(string? raw, out string remote, out string? error)
    {
        remote = "";
        if (!TryNormalizeCommand(raw, out var command, out error))
            return false;

        var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(command));
        remote = "sudo python3 -c " + SshShell.Quote(RconPython) + " " + SshShell.Quote(b64);
        return true;
    }

    public static bool CanRefresh(bool vm1Running, bool busy) => vm1Running && !busy;

    public static bool CanSend(bool minecraftJoinable, bool busy, string? commandText)
    {
        if (!minecraftJoinable || busy)
            return false;
        return TryNormalizeCommand(commandText, out _, out _);
    }

    public static string SendDisabledReason(bool vm1Running, bool minecraftJoinable, bool busy, string? commandText)
    {
        if (busy)
            return "Working…";
        if (!vm1Running)
            return VmStoppedHint;
        if (!minecraftJoinable)
            return MinecraftStoppedHint;
        if (!TryNormalizeCommand(commandText, out _, out var error))
            return error ?? EmptyCommandHint;
        return "";
    }

    public static string FormatTranscriptLine(string command, string response)
    {
        var body = (response ?? "").TrimEnd();
        if (string.IsNullOrEmpty(body))
            return "> " + command;
        return "> " + command + Environment.NewLine + body;
    }

    /// <summary>
    /// Console Simple view: drop RCON plumbing, journal wrappers, modloader/mixin boot noise, and
    /// Netty worker lines from an unfiltered <c>journalctl -o cat</c> buffer. Keeps chat, joins,
    /// world-prep progress, command transcript, <c>[Rcon:</c> echoes, and WARN/ERROR/FATAL.
    /// </summary>
    public static string FilterSimpleLog(string? full)
    {
        if (string.IsNullOrEmpty(full))
            return "";

        var lines = full.Split('\n');
        if (lines.Length == 0)
            return "";

        var kept = new List<string>(lines.Length);
        foreach (var line in lines)
        {
            if (!IsSimpleLogNoiseLine(line))
                kept.Add(line);
        }

        return string.Join('\n', kept).TrimEnd();
    }

    private static bool IsSimpleLogNoiseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;

        if (IsSimpleLogKeepLine(line))
            return false;

        var lower = line.ToLowerInvariant();
        if (IsRconPlumbingNoise(lower))
            return true;
        if (IsJournalWrapperNoise(line, lower))
            return true;
        if (IsNettyNoise(line, lower))
            return true;
        if (IsMixinDebugNoise(line, lower))
            return true;
        if (IsModloaderBootInfoNoise(line, lower))
            return true;
        return false;
    }

    /// <summary>Player-facing lines that must survive even when they match a noise substring.</summary>
    private static bool IsSimpleLogKeepLine(string line)
    {
        if (line.StartsWith("> ", StringComparison.Ordinal))
            return true;
        if (line.StartsWith("[Rcon:", StringComparison.OrdinalIgnoreCase))
            return true;
        if (line.StartsWith("There are ", StringComparison.Ordinal)
            && line.Contains("players online", StringComparison.OrdinalIgnoreCase))
            return true;

        var lower = line.ToLowerInvariant();
        if (lower.Contains("joined the game", StringComparison.Ordinal))
            return true;
        if (lower.Contains("left the game", StringComparison.Ordinal))
            return true;
        if (lower.Contains("preparing spawn area", StringComparison.Ordinal))
            return true;
        if (lower.Contains("done (", StringComparison.Ordinal) && lower.Contains(")!", StringComparison.Ordinal))
            return true;
        if (line.Contains("]: <", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool IsRconPlumbingNoise(string lower)
    {
        if (lower.Contains("thread rcon client", StringComparison.Ordinal))
            return true;
        if (lower.Contains("thread rcon listener", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon listener", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon running on", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon connection from", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon authenticated", StringComparison.Ordinal))
            return true;
        if (lower.Contains("starting rcon listener", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon client /", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon client started", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon client shutting down", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon listener #", StringComparison.Ordinal))
            return true;
        if (lower.Contains("rcon shutting down", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool IsJournalWrapperNoise(string line, string lower)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("-- ", StringComparison.Ordinal))
            return true;
        if (lower.Contains("-- logs begin at", StringComparison.Ordinal))
            return true;
        if (lower.Contains("-- journal begins", StringComparison.Ordinal))
            return true;
        if (lower.Contains("-- reboot --", StringComparison.Ordinal))
            return true;
        if (lower.StartsWith("defined-by: systemd", StringComparison.Ordinal))
            return true;
        if (lower.Contains("systemd[1]: started minecraft", StringComparison.Ordinal))
            return true;
        if (lower.Contains("systemd[1]: stopped minecraft", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool IsNettyNoise(string line, string lower)
    {
        if (!lower.Contains("netty", StringComparison.Ordinal))
            return false;
        if (lower.Contains("/error]", StringComparison.Ordinal) || lower.Contains("/fatal]", StringComparison.Ordinal))
            return false;
        if (lower.Contains("exception", StringComparison.Ordinal))
            return false;
        return true;
    }

    private static bool IsMixinDebugNoise(string line, string lower)
    {
        if (!lower.Contains("mixin", StringComparison.Ordinal))
            return false;

        if (lower.Contains("mixin apply failed", StringComparison.Ordinal))
            return false;
        if (lower.Contains("mixin prepare failed", StringComparison.Ordinal))
            return false;
        if (lower.Contains("caused the server to crash", StringComparison.Ordinal))
            return false;
        if (lower.Contains("/fatal]", StringComparison.Ordinal) || lower.Contains("/error]", StringComparison.Ordinal))
            return false;

        if (lower.Contains("/info]", StringComparison.Ordinal) || lower.Contains("/warn]", StringComparison.Ordinal))
            return true;
        if (lower.Contains("reference map", StringComparison.Ordinal)
            && lower.Contains("could not be read", StringComparison.Ordinal))
            return true;
        if (lower.Contains("error loading class", StringComparison.Ordinal)
            && lower.Contains("classnotfoundexception", StringComparison.Ordinal))
            return true;
        if (lower.Contains("@mixin target", StringComparison.Ordinal)
            && lower.Contains("was not found", StringComparison.Ordinal))
            return true;
        if (lower.Contains("spongepowered mixin subsystem", StringComparison.Ordinal))
            return true;
        if (lower.Contains("compatibility level set to", StringComparison.Ordinal))
            return true;
        if (lower.Contains("instancing error handler", StringComparison.Ordinal))
            return true;
        return false;
    }

    private static bool IsModloaderBootInfoNoise(string line, string lower)
    {
        var isInfoOrStdout =
            lower.Contains("/info]", StringComparison.Ordinal)
            || lower.Contains("[stdout/", StringComparison.Ordinal)
            || lower.Contains("]: [org.", StringComparison.Ordinal);
        if (!isInfoOrStdout)
            return false;

        if (lower.Contains("preparing spawn area", StringComparison.Ordinal))
            return false;
        if (lower.Contains("done (", StringComparison.Ordinal) && lower.Contains(")!", StringComparison.Ordinal))
            return false;
        if (lower.Contains("joined the game", StringComparison.Ordinal))
            return false;
        if (lower.Contains("left the game", StringComparison.Ordinal))
            return false;

        if (lower.Contains("modlauncher running:", StringComparison.Ordinal))
            return true;
        if (lower.Contains("modlauncher ", StringComparison.Ordinal) && lower.Contains("starting: java version", StringComparison.Ordinal))
            return true;
        if (lower.Contains("immediatewindowprovider", StringComparison.Ordinal))
            return true;
        if (lower.Contains("dependencies adding them to mods", StringComparison.Ordinal))
            return true;
        if (lower.Contains("launching target", StringComparison.Ordinal))
            return true;
        if (lower.Contains("loaded configuration file for", StringComparison.Ordinal))
            return true;
        if (lower.Contains("applying nashorn fix", StringComparison.Ordinal))
            return true;
        if (lower.Contains("applied forge config corruption patch", StringComparison.Ordinal))
            return true;
        if (lower.Contains("with fabric loader", StringComparison.Ordinal))
            return true;
        if (lower.Contains(" mods:", StringComparison.Ordinal) && lower.Contains("loading ", StringComparison.Ordinal))
            return true;
        if (lower.Contains("starting minecraft server version", StringComparison.Ordinal))
            return true;
        if (lower.Contains("loading properties", StringComparison.Ordinal))
            return true;
        if (lower.Contains("default game type:", StringComparison.Ordinal))
            return true;
        if (lower.Contains("generating keypair", StringComparison.Ordinal))
            return true;
        if (lower.Contains("starting minecraft server on", StringComparison.Ordinal))
            return true;
        if (lower.Contains("starting net.minecraft", StringComparison.Ordinal))
            return true;
        if (lower.Contains("forge loaded", StringComparison.Ordinal))
            return true;
        if (lower.Contains("jarinjjardependencylocator", StringComparison.Ordinal))
            return true;
        if (lower.Contains("[stdout/", StringComparison.Ordinal) || lower.Contains("]: [org.", StringComparison.Ordinal))
            return true;
        return false;
    }

    /// <summary>
    /// Parse vanilla / Fabric <c>list</c> RCON text. Does not read the RCON secret.
    /// </summary>
    public static bool TryParsePlayerList(string? rconPayload, out int online, out int? max)
    {
        online = 0;
        max = null;
        if (string.IsNullOrWhiteSpace(rconPayload))
            return false;

        var match = VanillaPlayerList.Match(rconPayload);
        if (!match.Success)
            return false;

        if (!int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out online))
            return false;
        if (int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedMax))
            max = parsedMax;
        return true;
    }

    /// <summary>
    /// Players pin: <c>0</c> when Stopped; <c>X / Y</c> (or <c>X</c>) while Running;
    /// <c>—</c> when Running but the count is unknown.
    /// </summary>
    public static string FormatPlayersPin(bool statusIsRunning, int? online, int? max)
    {
        if (!statusIsRunning)
            return "0";
        if (online is null)
            return "—";
        if (max is > 0)
            return online.Value.ToString(CultureInfo.InvariantCulture)
                + " / "
                + max.Value.ToString(CultureInfo.InvariantCulture);
        return online.Value.ToString(CultureInfo.InvariantCulture);
    }

    public static string OperatorHintFromRcon(SshExecResult run)
    {
        if (run.Succeeded)
            return "";

        var blob = ((run.Error ?? "") + " " + (run.Output ?? "")).ToLowerInvariant();
        if (blob.Contains("could not reach", StringComparison.Ordinal)
            || blob.Contains("connection refused", StringComparison.Ordinal)
            || run.ExitStatus == 5)
        {
            return RconUnreachableHint;
        }

        if (run.ExitStatus == 3)
            return "Could not read the on-box RCON secret.";
        if (run.ExitStatus == 4)
            return "RCON authentication failed.";

        return string.IsNullOrWhiteSpace(run.Error)
            ? "Command failed."
            : run.Error.Trim();
    }
}
