using McManager.Core.Setup;

namespace McManager.Core.Services;

/// <summary>
/// SSH inspect of VM1 <c>/opt/mcmgr/server/plugins</c> (Paper). Listing only.
/// </summary>
public static class ServerPluginsInspect
{
    public const string PluginsDir = "/opt/mcmgr/server/plugins";
    public const int MaxListedFiles = 400;
    public const long MaxUploadBytes = 64L * 1024 * 1024;

    public const string MarkerPlugins = "---PLUGINS---";
    public const string PluginsMissing = "MCMGR_PLUGINS_MISSING";
    public const string PluginsOk = "MCMGR_PLUGINS_OK";

    public static string RemoteScript { get; } =
        "set -euo pipefail; "
        + "HOME=\"${HOME:-/home/ubuntu}\"; "
        + $"echo {SshShell.Quote(MarkerPlugins)}; "
        + $"DIR={SshShell.Quote(PluginsDir)}; "
        + "if sudo test -d \"$DIR\"; then "
        + "sudo find \"$DIR\" -mindepth 1 -maxdepth 1 -type f -name '*.jar' ! -name '.*' -printf '%f\\n' | LC_ALL=C sort; "
        + $"echo {SshShell.Quote(PluginsOk)}; "
        + $"else echo {SshShell.Quote(PluginsMissing)}; fi";

    public static string RemoteCommand =>
        "sudo bash -c " + SshShell.Quote(RemoteScript);

    public static bool TryParse(string? stdout, out ServerPluginsInspectResult result, out string? error)
    {
        result = ServerPluginsInspectResult.Empty;
        error = null;

        var text = stdout ?? "";
        var idx = text.IndexOf(MarkerPlugins, StringComparison.Ordinal);
        if (idx < 0)
        {
            error = "Unexpected plugin listing from the game VM.";
            return false;
        }

        var blob = text[(idx + MarkerPlugins.Length)..].Trim();
        var missingDir = blob.Contains(PluginsMissing, StringComparison.Ordinal);
        var names = new List<string>();
        if (!missingDir)
        {
            foreach (var raw in blob.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0
                    || line.Equals(PluginsOk, StringComparison.Ordinal)
                    || line.Equals(PluginsMissing, StringComparison.Ordinal))
                    continue;
                if (!IsSafeJarName(line))
                    continue;
                names.Add(line);
                if (names.Count >= MaxListedFiles)
                    break;
            }
        }

        result = new ServerPluginsInspectResult(missingDir, names, truncated: names.Count >= MaxListedFiles);
        return true;
    }

    public static bool IsSafeJarName(string name)
    {
        if (!ServerModsInspect.IsSafeFileName(name))
            return false;
        return name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase);
    }

    public static string StagingRemotePath(string fileName) =>
        "/tmp/mcmgr-plugin-upload/" + fileName;

    public static string InstallScript(string fileName)
    {
        var destName = fileName.Trim();
        var src = StagingRemotePath(destName);
        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"DIR={SshShell.Quote(PluginsDir)}; "
            + $"SRC={SshShell.Quote(src)}; "
            + $"NAME={SshShell.Quote(destName)}; "
            + "mkdir -p \"$DIR\"; "
            + "chown mcmgr:mcmgr \"$DIR\"; "
            + "chmod 0750 \"$DIR\"; "
            + "install -o mcmgr -g mcmgr -m 0640 \"$SRC\" \"$DIR/$NAME\"; "
            + "rm -f \"$SRC\"; "
            + "echo OK";
        return "sudo bash -c " + SshShell.Quote(inner);
    }

    public static string DeleteScript(string fileName)
    {
        var destName = fileName.Trim();
        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"DIR={SshShell.Quote(PluginsDir)}; "
            + $"NAME={SshShell.Quote(destName)}; "
            + "rm -f \"$DIR/$NAME\"; "
            + "echo OK";
        return "sudo bash -c " + SshShell.Quote(inner);
    }
}

public sealed class ServerPluginsInspectResult
{
    public static ServerPluginsInspectResult Empty { get; } =
        new(pluginsDirectoryMissing: true, [], truncated: false);

    public ServerPluginsInspectResult(
        bool pluginsDirectoryMissing,
        IReadOnlyList<string> fileNames,
        bool truncated)
    {
        PluginsDirectoryMissing = pluginsDirectoryMissing;
        FileNames = fileNames;
        Truncated = truncated;
    }

    public bool PluginsDirectoryMissing { get; }
    public IReadOnlyList<string> FileNames { get; }
    public bool Truncated { get; }

    public string SummaryLine()
    {
        if (PluginsDirectoryMissing)
            return "no plugins folder on the server yet";
        var n = FileNames.Count;
        var bit = n == 1 ? "1 plugin jar" : $"{n} plugin jars";
        if (Truncated)
            bit += $" · showing first {ServerPluginsInspect.MaxListedFiles}";
        return bit;
    }
}
