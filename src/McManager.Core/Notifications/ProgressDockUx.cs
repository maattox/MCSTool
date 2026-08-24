namespace McManager.Core.Notifications;

/// <summary>
/// Shared copy and visibility for the window-locked Setup / Change pack progress dock.
/// Progress lives on the dock; compact toasts stay for outcomes, not the running job.
/// </summary>
public static class ProgressDockUx
{
    public const string ChangePackPickStatus = "Choose a pack file, then install.";

    public const string ChangePackReviewStatus = "Review the pack, then install.";

    public const string ChangePackAnalyzeFallback = "Analyzing modpack…";

    public const string ChangePackBuildFallback = "Building the derived pack…";

    public const string ChangePackInstallFallback = "Reinstalling Minecraft from this pack…";

    public static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;
        var totalSeconds = (int)Math.Floor(elapsed.TotalSeconds);
        var hours = totalSeconds / 3600;
        var minutes = (totalSeconds % 3600) / 60;
        var seconds = totalSeconds % 60;
        return hours > 0
            ? $"Time elapsed: {hours}:{minutes:D2}:{seconds:D2}"
            : $"Time elapsed: {minutes}:{seconds:D2}";
    }

    public static string OneLineStatus(bool jobActive, string? caption, string? fallback)
    {
        if (jobActive && !string.IsNullOrWhiteSpace(caption))
            return caption.Trim();
        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
    }

    public static bool ShowChangePackDock(bool showChangePackUi) => showChangePackUi;

    public static bool ShowJobProgress(bool analyzing, bool replaceRunning) =>
        analyzing || replaceRunning;

    /// <summary>
    /// Setup stages report a percent. Change pack SSH lines do not — use an indeterminate bar.
    /// </summary>
    public static bool PercentUnknown(bool hasStagePercent) => !hasStagePercent;

    /// <summary>
    /// Map a raw SSH / tofu / bootstrap log line to a short dock caption.
    /// Returns null when the line should not replace the current caption
    /// (unmapped shell, journal dumps). Never returns a raw <c>rm</c> / <c>tofu</c> command.
    /// </summary>
    public static string? TryHumanizeLogLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        var text = line.Trim();
        if (text.Length >= 11 && text[0] == '[' && text[3] == ':' && text[6] == ':' && text[9] == ']')
            text = text[10..].TrimStart();

        var wasCommand = text.StartsWith("> ", StringComparison.Ordinal)
            || text.StartsWith("$ ", StringComparison.Ordinal);
        var body = StripLogPrefix(text);
        if (body.Length == 0)
            return null;

        var mapped = MapKnownLine(body);
        if (mapped is not null)
            return mapped;

        if (wasCommand || LooksLikeShellOrDump(body))
            return null;

        if (LooksLikeHumanStatus(body))
            return TruncateStatus(body);

        return null;
    }

    /// <summary>Dock one-liner: mapped English, else <paramref name="fallback"/> — never the raw command.</summary>
    public static string HumanizeOrFallback(string? line, string fallback)
    {
        var mapped = TryHumanizeLogLine(line);
        if (!string.IsNullOrWhiteSpace(mapped))
            return mapped;
        return string.IsNullOrWhiteSpace(fallback) ? "" : fallback.Trim();
    }

    private static string StripLogPrefix(string line)
    {
        var text = line;
        if (text.Length >= 11 && text[0] == '[' && text[3] == ':' && text[6] == ':' && text[9] == ']')
            text = text[10..].TrimStart();

        if (text.StartsWith("> ", StringComparison.Ordinal))
            return text[2..].TrimStart();
        if (text.StartsWith("$ ", StringComparison.Ordinal))
            return text[2..].TrimStart();
        return text;
    }

    private static string? MapKnownLine(string body)
    {
        if (Contains(body, "rm -rf") || Contains(body, "mkdir -p"))
            return "Preparing files on the server…";
        if (Contains(body, "tofu init") || Contains(body, "tofu apply") || Contains(body, "tofu output")
            || StartsWithToken(body, "tofu"))
            return "Creating cloud resources…";
        if (Contains(body, "cloud-init ready"))
            return "Servers are ready.";
        if (Contains(body, "cloud-init") || Contains(body, "/etc/mcmgr/cloud-init")
            || Contains(body, "/etc/mcmgr-door/cloud-init"))
            return "Waiting for the servers to start…";
        if (Contains(body, "Door bootstrap finished") || Contains(body, "Door runtime repaired"))
            return "Doorbell software is ready.";
        if (Contains(body, "Repairing door"))
            return "Finishing doorbell setup…";
        if (Contains(body, "Door src") || Contains(body, "door bootstrap")
            || Contains(body, "Installing door"))
            return "Installing doorbell software…";
        if (Contains(body, "Parking reserved play IP") || Contains(body, "play IP is on VM1")
            || Contains(body, "door PLAYABLE"))
            return "Moving the play IP to the game server…";
        if (Contains(body, "Repairing VM1") || Contains(body, "VM1 runtime repaired"))
            return "Finishing the game server setup…";
        if (Contains(body, "firewalld") || Contains(body, "host filter"))
            return "Opening Minecraft on the firewall…";
        if (Contains(body, "Idle agent"))
            return "Installing the idle timer…";
        if (Contains(body, "onbox src") || Contains(body, "DISTRIBUTION=")
            || Contains(body, "Installing Minecraft"))
            return "Installing Minecraft…";
        if (Contains(body, "VM1 bootstrap finished") || Contains(body, "Pack replace finished"))
            return "Minecraft install is finished.";
        if (Contains(body, "server.properties"))
            return "Applying server settings…";
        if (Contains(body, "server-side pack") || Contains(body, "uploaded pack files"))
            return "Installing pack files…";
        if (StartsWithToken(body, "uploaded") || StartsWithToken(body, "put"))
            return "Copying files to the server…";
        if (Contains(body, "Object Storage") || Contains(body, "Published budget")
            || Contains(body, "Seeding Object") || Contains(body, "meta/infra.json"))
            return "Saving shared storage…";
        if (Contains(body, "OCIR") || Contains(body, "Function image") || Contains(body, "Pushed Function")
            || Contains(body, "spend-brake"))
            return "Installing the spend-brake Function…";
        if (Contains(body, "Waiting for VM1") || Contains(body, "VM1 is stopped")
            || Contains(body, "Waiting for VM1 RUNNING"))
            return "Waiting for the game server to start…";
        if (Contains(body, "Waiting for door"))
            return "Waiting for the doorbell to start…";
        if (Contains(body, "RCON list succeeded"))
            return "Minecraft is ready.";
        if (Contains(body, "blamed one mod") || Contains(body, "moving it aside"))
            return "Moving the blamed mod aside…";
        if (Contains(body, "Retrying Minecraft without"))
            return "Retrying Minecraft without that mod…";
        if (Contains(body, "Removed '") && Contains(body, "from this boot"))
            return "A crash blamed one mod; it was set aside.";
        if (Contains(body, "crash detected") || Contains(body, "Minecraft crash"))
            return "Minecraft crashed while starting.";
        if (Contains(body, "RCON not ready") || Contains(body, "still starting"))
            return "Waiting for Minecraft to start…";
        if (Contains(body, "tfvars") || Contains(body, "Preparing the cloud"))
            return "Preparing the cloud plan…";
        if (Contains(body, "dry-run") || Contains(body, "Dry-run"))
            return "Dry-run (no Oracle Cloud)…";
        if (Contains(body, "out of host capacity") || Contains(body, "capacity is unavailable"))
            return "Always Free A1 capacity is unavailable.";
        return null;
    }

    private static bool LooksLikeShellOrDump(string body)
    {
        if (body.StartsWith('>') || body.StartsWith('$'))
            return true;
        if (body.Contains("&&", StringComparison.Ordinal))
            return true;
        if (body.Contains("/tmp/", StringComparison.Ordinal))
            return true;
        if (body.StartsWith("sudo ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("bash ", StringComparison.OrdinalIgnoreCase)
            || body.StartsWith("systemctl ", StringComparison.OrdinalIgnoreCase))
            return true;
        if (body.Contains('\t') || body.Contains("Exception", StringComparison.Ordinal)
            || body.Contains(" at ", StringComparison.Ordinal))
            return true;
        return body.Length > 140;
    }

    private static bool LooksLikeHumanStatus(string body)
    {
        if (body.Length < 12 || body.Length > 90)
            return false;
        if (!char.IsLetter(body[0]))
            return false;
        return body.Contains(' ', StringComparison.Ordinal);
    }

    private static bool Contains(string body, string token) =>
        body.Contains(token, StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithToken(string body, string token) =>
        body.StartsWith(token, StringComparison.OrdinalIgnoreCase);

    private static string TruncateStatus(string body)
    {
        var first = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0].Trim();
        return first.Length <= 90 ? first : first[..87] + "…";
    }
}
