using System.Text.Json;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Layer 3 quarantine copy, remote command builders, and manifest JSON.
/// Never folds entries into <c>excluded_client_only_files</c>.
/// </summary>
public static class CrashQuarantine
{
    public const string QuarantineDirName = "mods.quarantined";
    public const string ModsRelativePrefix = "mods/";
    public const string OnboxScriptName = "quarantine_mod.py";
    public const string InstalledLibRelative = "/opt/mcmgr/lib/quarantine_mod.py";
    public const string StagingLibRelative = "/tmp/mcmgr-onbox/common/quarantine_mod.py";

    public const string MovingLog = "The loader blamed one mod for the crash; moving it aside and retrying once…";
    public const string RetryingLog = "Retrying Minecraft without the blamed mod…";

    public static string NotifyMessage(string modId, string jarPath, bool likelyClientOnly, bool retrySucceeded)
    {
        var name = string.IsNullOrWhiteSpace(modId) ? "a mod" : modId.Trim();
        var jar = string.IsNullOrWhiteSpace(jarPath) ? "" : " (" + Path.GetFileName(jarPath.Replace('\\', '/')) + ")";
        var sb = new System.Text.StringBuilder();
        if (retrySucceeded)
        {
            sb.Append("Removed '").Append(name).Append('\'').Append(jar)
                .Append(" from this boot because the loader blamed it for a crash. Minecraft started without it.");
        }
        else
        {
            sb.Append("Removed '").Append(name).Append('\'').Append(jar)
                .Append(" from this boot because the loader blamed it for a crash. Minecraft still failed to start.");
        }

        sb.Append(' ');
        if (likelyClientOnly)
        {
            sb.Append("It looks like a client-only mod, so you probably do not need it on the server.");
        }
        else
        {
            sb.Append("This mod may be required for the pack. You can put it back.");
        }

        sb.Append(" On Server → Mods, choose Keep excluded or Put back.");
        return sb.ToString();
    }

    public static string KeepExcludedCopy(string modId) =>
        "'" + (modId ?? "").Trim() + "' will stay off this server. Future installs of this same pack file will skip it.";

    public static string PutBackCopy(string modId) =>
        "Put '" + (modId ?? "").Trim() + "' back into mods/. Restart Minecraft if it is already running.";

    public static string PanelHelp =>
        "If Minecraft crashed and the loader blamed exactly one mod, Manager moves that jar to "
        + "mods.quarantined (never deletes it) and retries once. Keep excluded skips it on future "
        + "installs of this same pack file. Put back restores the jar.";

    public static string EntryCopy(QuarantinedFileEntry entry, bool likelyClientOnly)
    {
        var name = entry.DisplayName;
        var sb = new System.Text.StringBuilder();
        sb.Append('\'').Append(name).Append("' was removed from this boot because the loader blamed it for a crash.");
        if (entry.RetrySucceeded)
            sb.Append(" Minecraft started without it.");
        else
            sb.Append(" Minecraft still failed to start after that retry.");
        sb.Append(' ');
        if (likelyClientOnly)
            sb.Append("It looks like a client-only mod; you probably do not need it on the server.");
        else
            sb.Append("This mod may be required. You can put it back.");
        return sb.ToString();
    }

    public static string RemoteCommand(
        string action,
        string? modId = null,
        string? jarFileName = null,
        string? relativePath = null,
        bool restart = false)
    {
        var verb = (action ?? "").Trim().ToLowerInvariant();
        if (verb is not ("move" or "restore" or "ack" or "set-retry" or "read-crash"))
            throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported quarantine action.");

        var args = new List<string> { "sudo", "python3", "\"$SCRIPT\"", verb };
        if (!string.IsNullOrWhiteSpace(modId))
        {
            args.Add("--mod-id");
            args.Add(SshShell.Quote(modId.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(jarFileName))
        {
            args.Add("--jar-name");
            args.Add(SshShell.Quote(jarFileName.Trim()));
        }

        if (!string.IsNullOrWhiteSpace(relativePath))
        {
            args.Add("--path");
            args.Add(SshShell.Quote(NormalizeRelative(relativePath)));
        }

        if (restart || verb is "move" or "restore")
            args.Add("--restart");
        if (verb == "set-retry")
            args.Add("--succeeded");

        var inner =
            "set -euo pipefail; "
            + "HOME=\"${HOME:-/home/ubuntu}\"; "
            + "if sudo test -f " + InstalledLibRelative + "; then SCRIPT=" + InstalledLibRelative + "; "
            + "else SCRIPT=" + StagingLibRelative + "; fi; "
            + string.Join(' ', args);
        return "sudo bash -c " + SshShell.Quote(inner);
    }

    public static string NormalizeRelative(string? path)
    {
        var n = (path ?? "").Replace('\\', '/').Trim().TrimStart('/');
        if (n.StartsWith(QuarantineDirName + "/", StringComparison.OrdinalIgnoreCase))
            n = ModsRelativePrefix + n[(QuarantineDirName.Length + 1)..];
        if (!n.StartsWith(ModsRelativePrefix, StringComparison.OrdinalIgnoreCase)
            && n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && !n.Contains('/'))
        {
            n = ModsRelativePrefix + n;
        }

        return n;
    }

    public static CrashQuarantineRemoteResult ParseRemote(string? stdout)
    {
        var text = (stdout ?? "").Trim();
        if (text.Length == 0)
            return CrashQuarantineRemoteResult.Fail("No output from the quarantine helper.");

        var jsonStart = text.IndexOf('{');
        if (jsonStart < 0)
            return CrashQuarantineRemoteResult.Fail("Quarantine helper did not return JSON.");

        var json = text[jsonStart..];
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var ok = root.TryGetProperty("ok", out var okEl) && okEl.ValueKind == JsonValueKind.True;
            var error = ReadString(root, "error");
            if (!ok)
                return CrashQuarantineRemoteResult.Fail(error ?? "Quarantine helper failed.");

            return new CrashQuarantineRemoteResult(
                true,
                null,
                ReadString(root, "mod_id"),
                ReadString(root, "path"),
                ReadString(root, "moved_to"),
                ReadBool(root, "likely_client_only"),
                ReadString(root, "crash_report"));
        }
        catch (JsonException)
        {
            return CrashQuarantineRemoteResult.Fail("Quarantine helper returned invalid JSON.");
        }
    }

    public static IReadOnlyList<QuarantinedFileEntry> ParseManifestEntries(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("modpack", out var pack)
                || pack.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            if (!pack.TryGetProperty("quarantined_files", out var arr)
                || arr.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var list = new List<QuarantinedFileEntry>();
            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                    continue;
                var path = ReadString(el, "path");
                if (string.IsNullOrWhiteSpace(path))
                    continue;
                list.Add(new QuarantinedFileEntry(
                    path,
                    ReadString(el, "reason") ?? ReasonOrDefault(),
                    ReadString(el, "detected_at") ?? "",
                    ReadBool(el, "retry_succeeded"),
                    ReadBool(el, "operator_acknowledged")));
            }

            return list;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static bool GuessClientOnlyFromLists(string? relativePath, string? modId)
    {
        var matcher = ExcludeIncludeMatcher.ForModrinth();
        var path = string.IsNullOrWhiteSpace(relativePath)
            ? (string.IsNullOrWhiteSpace(modId) ? "" : "mods/" + modId + ".jar")
            : NormalizeRelative(relativePath);
        if (path.Length == 0)
            return false;
        return matcher.Match(null, path, modId).Exclude;
    }

    private static string ReasonOrDefault() => CrashModAttributor.Reason;

    private static string? ReadString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
            return null;
        var s = el.GetString();
        return string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }

    private static bool ReadBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el))
            return false;
        return el.ValueKind == JsonValueKind.True
            || (el.ValueKind == JsonValueKind.String
                && bool.TryParse(el.GetString(), out var b)
                && b);
    }
}

public sealed record CrashQuarantineRemoteResult(
    bool Ok,
    string? Error,
    string? ModId,
    string? Path,
    string? MovedTo,
    bool LikelyClientOnly,
    string? CrashReport)
{
    public static CrashQuarantineRemoteResult Fail(string error) =>
        new(false, error, null, null, null, false, null);
}

public sealed record QuarantinedFileEntry(
    string Path,
    string Reason,
    string DetectedAt,
    bool RetrySucceeded,
    bool OperatorAcknowledged)
{
    public string FileName
    {
        get
        {
            var n = (Path ?? "").Replace('\\', '/');
            var slash = n.LastIndexOf('/');
            return slash < 0 ? n : n[(slash + 1)..];
        }
    }

    public string DisplayName
    {
        get
        {
            var stem = System.IO.Path.GetFileNameWithoutExtension(FileName);
            if (string.IsNullOrWhiteSpace(stem))
                return FileName;
            var cut = stem.IndexOf('-');
            return cut > 0 ? stem[..cut] : stem;
        }
    }

    public bool NeedsAck => !OperatorAcknowledged;
}
