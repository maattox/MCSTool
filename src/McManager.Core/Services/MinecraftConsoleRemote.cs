using System.Text;

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
        + "Recent logs are from the game computer. This is not a live terminal.";

    public const string Intro =
        "Send Minecraft commands and read recent logs. Start the server first. "
        + "This is not a live terminal.";

    public const string EmptyLogs =
        "No log lines yet. Start the server, then Refresh.";

    public const string VmStoppedHint =
        "Start the server first. Console needs the game computer up.";

    public const string MinecraftStoppedHint =
        "Minecraft is stopped. Start the server, then send commands.";

    public const string EmptyCommandHint = "Type a command, then Send.";

    public const string CommandTooLongHint = "That command is too long.";

    public const string RconUnreachableHint =
        "Could not reach Minecraft. Is the server Running?";

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
