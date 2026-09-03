using System.Text.Json.Serialization;

namespace McManager.Core.Config;

/// <summary>
/// Program settings for this PC (not stack config). Lives under
/// <c>%LOCALAPPDATA%\MCSTool\app-settings.json</c>.
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

    /// <summary>Slug of the server whose folder Manager is using. Ignored when <c>MCMANAGER_CONFIG_DIR</c> is set.</summary>
    [JsonPropertyName("active_server")]
    public string? ActiveServer { get; set; }

    /// <summary>This PC’s servers (id = folder slug, display_name = UI label). Not OCI profile names.</summary>
    [JsonPropertyName("servers")]
    public List<ServerIndexEntry> Servers { get; set; } = [];

    public static AppSettingsDocument Default() => new();
}

public sealed class ServerIndexEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";
}
