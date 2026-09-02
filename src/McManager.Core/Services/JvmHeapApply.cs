using McManager.Core.Setup;

namespace McManager.Core.Services;

/// <summary>
/// Locate on-box <c>apply-jvm-heap.py</c> and parse its stdout.
/// SSH upload/run lives on <see cref="SshService"/>.
/// </summary>
public static class JvmHeapApply
{
    public const string RemoteDir = "/tmp/mcmgr-heap";
    public const string RemoteScriptName = "apply-jvm-heap.py";
    public const string RemoteScriptPath = RemoteDir + "/" + RemoteScriptName;
    public const string RemoteExtrasJsonName = "extras.json";
    public const string RemoteExtrasJsonPath = RemoteDir + "/" + RemoteExtrasJsonName;

    public static string? FindLocalScript()
    {
        var onbox = ProductPaths.FindOnboxDirectory();
        if (onbox is null)
            return null;
        var path = Path.Combine(onbox, "common", RemoteScriptName);
        return File.Exists(path) ? path : null;
    }

    public static string RunCommand(string heap)
    {
        var token = JvmHeapChoice.Normalize(heap);
        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"python3 {SshShell.Quote(RemoteScriptPath)} {SshShell.Quote(token)}; "
            + "systemctl daemon-reload";
        return "sudo bash -c " + SshShell.Quote(inner);
    }

    public static bool TryParseOk(string? stdout, out string heap, out string? error)
    {
        heap = "";
        error = null;
        var text = stdout ?? "";
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith("OK heap=", StringComparison.Ordinal))
                continue;
            var rest = line["OK heap=".Length..];
            var token = rest.Split(' ', 2)[0].Trim();
            if (!JvmHeapChoice.IsAllowed(token))
            {
                error = "Heap apply returned an unexpected size.";
                return false;
            }

            heap = JvmHeapChoice.Normalize(token);
            return true;
        }

        error = "Heap apply did not confirm OK.";
        return false;
    }

    public static string DumpExtrasCommand()
    {
        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"python3 {SshShell.Quote(RemoteScriptPath)} dump-extras";
        return "sudo bash -c " + SshShell.Quote(inner);
    }

    public static string SetExtrasCommand()
    {
        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"python3 {SshShell.Quote(RemoteScriptPath)} set-extras {SshShell.Quote(RemoteExtrasJsonPath)}; "
            + "systemctl daemon-reload";
        return "sudo bash -c " + SshShell.Quote(inner);
    }

    public static bool TryParseExtrasDump(string? stdout, out IReadOnlyList<string> flags, out string? error)
    {
        flags = [];
        error = null;
        var text = stdout ?? "";
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = raw.Trim();
            if (!line.StartsWith("OK extras=", StringComparison.Ordinal))
                continue;
            var json = line["OK extras=".Length..].Trim();
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
                flags = JvmExtraFlags.Parse(string.Join('\n', parsed ?? []));
                return true;
            }
            catch (System.Text.Json.JsonException)
            {
                error = "Flag dump was not valid JSON.";
                return false;
            }
        }

        error = "Flag dump did not confirm OK.";
        return false;
    }

    public static bool TryParseExtrasSet(string? stdout, out string? error)
    {
        error = null;
        var text = stdout ?? "";
        foreach (var raw in text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (raw.Trim().StartsWith("OK extras_set=", StringComparison.Ordinal))
                return true;
        }

        error = "Flag apply did not confirm OK.";
        return false;
    }
}
