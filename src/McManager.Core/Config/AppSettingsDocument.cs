using System.Text.Json.Serialization;

namespace McManager.Core.Config;

/// <summary>
/// Program settings for this PC (not stack config). Lives under
/// <c>%LOCALAPPDATA%\McManager\app-settings.json</c>.
/// </summary>
public sealed class AppSettingsDocument
{
    public const int DocumentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    /// <summary>
    /// When true, Manager checks GitHub Releases once after the UI is up
    /// and prompts if a newer published tag exists. Never applies an update.
    /// </summary>
    [JsonPropertyName("check_for_updates")]
    public bool CheckForUpdates { get; set; } = true;

    public static AppSettingsDocument Default() => new();
}
