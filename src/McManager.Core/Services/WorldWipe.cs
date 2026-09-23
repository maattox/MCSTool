namespace McManager.Core.Services;

/// <summary>
/// Builds the SSH wipe script for the live VM1 world directory.
/// Deletes that folder and sibling Nether/End folders (<c>{name}_nether</c>,
/// <c>{name}_the_end</c>), then clears the shared-map tile cache. Recreates the
/// world folder empty. Does not touch mods, config, <c>server.properties</c>,
/// or Object Storage backups.
/// </summary>
public static class WorldWipe
{
    public const string ServerDir = "/opt/mcmgr/server";

    private static readonly HashSet<string> ForbiddenLeafNames = new(StringComparer.Ordinal)
    {
        "mods",
        "config",
        "libraries",
        "bin",
        "logs",
        "crash-reports",
        "defaultconfigs",
        "versions",
        ".fabric",
        "server.properties",
        "eula.txt",
        "server.jar",
        "user_jvm_args.txt",
        "run.sh",
        "run.bat",
        "unix_args.txt",
        "win_args.txt",
    };

    public static bool TryCreate(string? worldPath, out WorldWipePlan plan, out string? error)
    {
        plan = default!;
        error = null;

        if (!TryNormalizeWorldPath(worldPath, out var normalized, out error))
            return false;

        plan = new WorldWipePlan(normalized, BuildRemoteScript(normalized));
        return true;
    }

    public static bool TryNormalizeWorldPath(string? worldPath, out string normalized, out string? error)
    {
        normalized = "";
        error = null;

        var raw = (worldPath ?? "").Trim().Replace('\\', '/');
        if (raw.Length > 1)
            raw = raw.TrimEnd('/');

        if (string.IsNullOrWhiteSpace(raw) || !raw.StartsWith('/'))
        {
            error = "The world folder setting must be a full path on the game VM.";
            return false;
        }

        if (raw.Contains("..", StringComparison.Ordinal)
            || raw.Contains('*', StringComparison.Ordinal)
            || raw.Contains('?', StringComparison.Ordinal)
            || raw.Contains('[', StringComparison.Ordinal)
            || raw.IndexOfAny(['\n', '\r', '\0', ';', '|', '&', '$', '`']) >= 0)
        {
            error = "The world folder setting is not a safe folder.";
            return false;
        }

        if (string.Equals(raw, ServerDir, StringComparison.Ordinal)
            || !raw.StartsWith(ServerDir + "/", StringComparison.Ordinal))
        {
            error =
                "The world folder setting must be a folder inside the server folder "
                + "(not the server folder itself).";
            return false;
        }

        var leaf = raw[(ServerDir.Length + 1)..];
        if (string.IsNullOrEmpty(leaf) || leaf.Contains('/', StringComparison.Ordinal))
        {
            error = "The world folder setting must be a single folder directly inside the server folder.";
            return false;
        }

        if (ForbiddenLeafNames.Contains(leaf))
        {
            error = $"Refusing to wipe '{leaf}' — that is not a world save.";
            return false;
        }

        normalized = raw;
        return true;
    }

    private static string BuildRemoteScript(string worldPath)
    {
        var quoted = SshShell.Quote(worldPath);
        return
            "set -euo pipefail; "
            + $"WORLD={quoted}; "
            + "case \"$WORLD\" in "
            + "/opt/mcmgr/server/*) ;; "
            + "*) echo refusing world_path outside /opt/mcmgr/server/; exit 2 ;; "
            + "esac; "
            + "BASE=$(basename -- \"$WORLD\"); "
            + "case \"$BASE\" in "
            + "mods|config|libraries|bin|logs) echo refusing reserved name; exit 2 ;; "
            + "esac; "
            + "PARENT=$(dirname -- \"$WORLD\"); "
            + "if [ -e \"$WORLD\" ]; then sudo rm -rf -- \"$WORLD\"; fi; "
            + "for EXTRA in \"${BASE}_nether\" \"${BASE}_the_end\"; do "
            + "DIR=\"$PARENT/$EXTRA\"; "
            + "case \"$DIR\" in "
            + "/opt/mcmgr/server/*) ;; "
            + "*) echo refusing dimension path; exit 2 ;; "
            + "esac; "
            + "case \"$EXTRA\" in "
            + "mods|config|libraries|bin|logs) echo refusing reserved name; exit 2 ;; "
            + "esac; "
            + "if [ -e \"$DIR\" ] || [ -L \"$DIR\" ]; then sudo rm -rf -- \"$DIR\"; fi; "
            + "done; "
            + "sudo mkdir -p -- \"$WORLD\"; "
            + "sudo chown mcmgr:mcmgr -- \"$WORLD\"; "
            + "sudo chmod 0750 -- \"$WORLD\"; "
            + PlayerMapReset.VmCacheClearBody()
            + "echo OK";
    }
}

public sealed class WorldWipePlan
{
    public WorldWipePlan(string worldPath, string remoteScript)
    {
        WorldPath = worldPath;
        RemoteScript = remoteScript;
    }

    public string WorldPath { get; }
    public string RemoteScript { get; }
}
