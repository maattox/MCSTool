using McManager.Core.Config;

namespace McManager.Core.Services;

/// <summary>
/// Launch update check: honor the settings toggle, compare the running version
/// to the latest GitHub Release tag, and build a prompt payload. Does not apply
/// an update. Does not retry failed HTTP.
/// </summary>
public static class AppUpdateCheck
{
    public const int NotesMaxChars = 4000;
    public const string OpenDownloadButton = "Open download";

    /// <summary>
    /// When the toggle is off, returns null and does not call GitHub.
    /// Offline / 404 / rate-limit → null (no prompt, no throw).
    /// </summary>
    public static async Task<AppUpdatePrompt?> EvaluateAsync(
        bool checkForUpdates,
        string localVersion,
        GitHubLatestReleaseClient client,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!checkForUpdates)
            return null;

        var latest = await client.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (!latest.Succeeded || latest.Value is null)
            return null;

        return TryBuildPrompt(localVersion, latest.Value);
    }

    public static AppUpdatePrompt? TryBuildPrompt(string localVersion, GitHubReleaseInfo latest)
    {
        ArgumentNullException.ThrowIfNull(latest);
        if (!IsNewerThan(localVersion, latest.TagName))
            return null;

        var notes = string.IsNullOrWhiteSpace(latest.Body)
            ? "A newer Manager is on GitHub Releases."
            : latest.Body.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
        if (notes.Length > NotesMaxChars)
            notes = notes[..NotesMaxChars] + "\n\n…";

        var openUrl = string.IsNullOrWhiteSpace(latest.HtmlUrl)
            ? ProgramPaths.GitHubUrl + "/releases"
            : latest.HtmlUrl;

        return new AppUpdatePrompt(latest.DisplayName, notes, openUrl);
    }

    /// <summary>
    /// True when <paramref name="tagName"/> (optional leading <c>v</c>) is
    /// strictly newer than <paramref name="localVersion"/>. Unparseable values
    /// are not newer.
    /// </summary>
    public static bool IsNewerThan(string localVersion, string tagName)
    {
        if (!TryParseVersion(localVersion, out var local))
            return false;
        if (!TryParseVersion(tagName, out var remote))
            return false;
        return Normalize(remote) > Normalize(local);
    }

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var s = value.Trim();
        if (s.StartsWith('v') || s.StartsWith('V'))
            s = s[1..];

        var plus = s.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
            s = s[..plus];
        var dash = s.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
            s = s[..dash];

        s = s.Trim();
        if (!Version.TryParse(s, out var parsed) || parsed is null)
            return false;
        version = parsed;
        return true;
    }

    private static Version Normalize(Version v) =>
        new(
            v.Major,
            v.Minor,
            v.Build < 0 ? 0 : v.Build,
            v.Revision < 0 ? 0 : v.Revision);
}

public sealed class AppUpdatePrompt
{
    public AppUpdatePrompt(string title, string message, string openUrl)
    {
        Title = title;
        Message = message;
        OpenUrl = openUrl;
    }

    public string Title { get; }
    public string Message { get; }
    public string OpenUrl { get; }
}
