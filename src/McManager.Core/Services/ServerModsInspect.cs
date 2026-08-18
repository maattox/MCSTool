using System.Text.Json;

namespace McManager.Core.Services;

/// <summary>
/// SSH inspect of VM1 <c>/opt/mcmgr/server/mods</c> plus a few game-manifest fields.
/// Listing only — never zips that folder.
/// </summary>
public static class ServerModsInspect
{
    public const string ModsDir = "/opt/mcmgr/server/mods";
    public const string ManifestPath = "/etc/mcmgr/game-manifest.json";
    public const int MaxListedFiles = 400;

    public const string MarkerManifest = "---MANIFEST---";
    public const string MarkerMods = "---MODS---";
    public const string ManifestMissing = "MCMGR_MANIFEST_MISSING";
    public const string ModsMissing = "MCMGR_MODS_MISSING";
    public const string ModsOk = "MCMGR_MODS_OK";

    /// <summary>
    /// Inner payload for <c>sudo bash -c</c>. Uses sudo for root-owned paths; HOME default for set -u.
    /// </summary>
    public static string RemoteScript { get; } =
        "set -euo pipefail; "
        + "HOME=\"${HOME:-/home/ubuntu}\"; "
        + $"echo {SshShell.Quote(MarkerManifest)}; "
        + $"if sudo test -f {SshShell.Quote(ManifestPath)}; then sudo cat {SshShell.Quote(ManifestPath)}; "
        + $"else echo {SshShell.Quote(ManifestMissing)}; fi; "
        + $"echo {SshShell.Quote(MarkerMods)}; "
        + $"DIR={SshShell.Quote(ModsDir)}; "
        + "if sudo test -d \"$DIR\"; then "
        + "sudo find \"$DIR\" -mindepth 1 -maxdepth 1 -type f ! -name '.*' -printf '%f\\n' | LC_ALL=C sort; "
        + $"echo {SshShell.Quote(ModsOk)}; "
        + $"else echo {SshShell.Quote(ModsMissing)}; fi";

    public static string RemoteCommand =>
        "sudo bash -c " + SshShell.Quote(RemoteScript);

    public static bool TryParse(string? stdout, out ServerModsInspectResult result, out string? error)
    {
        result = ServerModsInspectResult.Empty;
        error = null;

        var text = stdout ?? "";
        var manifestIdx = text.IndexOf(MarkerManifest, StringComparison.Ordinal);
        var modsIdx = text.IndexOf(MarkerMods, StringComparison.Ordinal);
        if (manifestIdx < 0 || modsIdx < 0 || modsIdx < manifestIdx)
        {
            error = "Unexpected inspect output from the game VM.";
            return false;
        }

        var manifestBlob = text[(manifestIdx + MarkerManifest.Length)..modsIdx].Trim();
        var modsBlob = text[(modsIdx + MarkerMods.Length)..].Trim();

        string? distribution = null;
        string? loader = null;
        string? loaderVersion = null;
        string? minecraftVersion = null;
        if (!string.IsNullOrWhiteSpace(manifestBlob)
            && !manifestBlob.Equals(ManifestMissing, StringComparison.Ordinal))
        {
            TryReadManifestFields(
                manifestBlob,
                out distribution,
                out loader,
                out loaderVersion,
                out minecraftVersion);
        }

        var missingDir = modsBlob.Contains(ModsMissing, StringComparison.Ordinal);
        var names = new List<string>();
        if (!missingDir)
        {
            foreach (var raw in modsBlob.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                if (line.Length == 0
                    || line.Equals(ModsOk, StringComparison.Ordinal)
                    || line.Equals(ModsMissing, StringComparison.Ordinal))
                    continue;
                if (!IsSafeFileName(line))
                    continue;
                names.Add(line);
                if (names.Count >= MaxListedFiles)
                    break;
            }
        }

        result = new ServerModsInspectResult(
            distribution,
            loader,
            loaderVersion,
            minecraftVersion,
            missingDir,
            names,
            truncated: names.Count >= MaxListedFiles);
        return true;
    }

    public static bool IsSafeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;
        if (name.Length > 240)
            return false;
        if (name.Contains('/') || name.Contains('\\') || name.Contains("..", StringComparison.Ordinal))
            return false;
        if (name.IndexOfAny(['\0', ';', '|', '&', '$', '`', '\n', '\r']) >= 0)
            return false;
        return true;
    }

    private static void TryReadManifestFields(
        string json,
        out string? distribution,
        out string? loader,
        out string? loaderVersion,
        out string? minecraftVersion)
    {
        distribution = null;
        loader = null;
        loaderVersion = null;
        minecraftVersion = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            distribution = ReadString(root, "distribution");
            loader = ReadString(root, "loader");
            loaderVersion = ReadString(root, "loader_version");
            minecraftVersion = ReadString(root, "minecraft_version");
        }
        catch (JsonException)
        {
            // Inspect listing still works without manifest fields.
        }
    }

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.Null)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}

public sealed class ServerModsInspectResult
{
    public static ServerModsInspectResult Empty { get; } =
        new(null, null, null, null, modsDirectoryMissing: true, [], truncated: false);

    public ServerModsInspectResult(
        string? distribution,
        string? loader,
        string? loaderVersion,
        string? minecraftVersion,
        bool modsDirectoryMissing,
        IReadOnlyList<string> fileNames,
        bool truncated)
    {
        Distribution = distribution;
        Loader = loader;
        LoaderVersion = loaderVersion;
        MinecraftVersion = minecraftVersion;
        ModsDirectoryMissing = modsDirectoryMissing;
        FileNames = fileNames;
        Truncated = truncated;
    }

    public string? Distribution { get; }
    public string? Loader { get; }
    public string? LoaderVersion { get; }
    public string? MinecraftVersion { get; }
    public bool ModsDirectoryMissing { get; }
    public IReadOnlyList<string> FileNames { get; }
    public bool Truncated { get; }

    public string SummaryLine()
    {
        var bits = new List<string>();
        var loaderLabel = DisplayLoader(Loader);
        if (!string.IsNullOrWhiteSpace(loaderLabel))
        {
            bits.Add(string.IsNullOrWhiteSpace(LoaderVersion)
                ? loaderLabel
                : loaderLabel + " " + LoaderVersion);
        }

        if (!string.IsNullOrWhiteSpace(MinecraftVersion))
            bits.Add("Minecraft " + MinecraftVersion);

        if (ModsDirectoryMissing)
            bits.Add("no mods folder on the server yet");
        else
        {
            var n = FileNames.Count;
            bits.Add(n == 1 ? "1 file in mods/" : $"{n} files in mods/");
            if (Truncated)
                bits.Add($"showing first {ServerModsInspect.MaxListedFiles}");
        }

        return bits.Count == 0 ? "Server-side mods on the game VM." : string.Join(" · ", bits);
    }

    public static string DisplayLoader(string? loader)
    {
        var id = (loader ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            _ => string.IsNullOrWhiteSpace(loader) ? "" : loader.Trim(),
        };
    }
}
