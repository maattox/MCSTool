using System.Text.Json.Serialization;

namespace McManager.Core.Config;

/// <summary>
/// Program settings for this PC (not stack config). Lives under
/// <c>%LOCALAPPDATA%\McManager\app-settings.json</c>. Phase 9 honors
/// <see cref="CheckForUpdates"/>; this step only persists the toggle.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int DocumentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    /// <summary>
    /// When true, Manager will check GitHub Releases on launch once that ships.
    /// No network check runs until Phase 9.
    /// </summary>
    [JsonPropertyName("check_for_updates")]
    public bool CheckForUpdates { get; set; } = true;

    public static AppSettingsDocument Default() => new();
}
