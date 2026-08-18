using System.Text.Json.Serialization;

namespace McManager.Core.Config;

public sealed class FriendsLocalFile
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    /// <summary><c>private</c> or <c>public</c>. Missing/invalid is treated as private.</summary>
    [JsonPropertyName("mode")]
    public string Mode { get; init; } = IpAccessMode.Private;

    [JsonPropertyName("friends")]
    public List<FriendEntry> Friends { get; init; } = [];

    [JsonPropertyName("blacklist")]
    public List<BlacklistEntry> Blacklist { get; init; } = [];
}

public sealed class FriendEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("ip")]
    public string Ip { get; init; } = "";

    [JsonPropertyName("is_admin")]
    public bool IsAdmin { get; init; }
}

public sealed class BlacklistEntry
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("ip")]
    public string Ip { get; init; } = "";
}

/// <summary>Object Storage <c>ip/allowlist.json</c>. <c>ip</c> is a single IPv4 or IPv4 CIDR.</summary>
public sealed class IpAllowlistDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("mode_note")]
    public string ModeNote { get; set; } = "Allowlist is applied only when ip/mode.json is private.";

    [JsonPropertyName("entries")]
    public List<FriendEntry> Entries { get; set; } = [];
}

/// <summary>Object Storage <c>ip/mode.json</c>.</summary>
public sealed class IpModeDocument
{
    public const int CurrentVersion = 1;

    [JsonPropertyName("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = IpAccessMode.Private;

    [JsonPropertyName("blacklist")]
    public List<BlacklistEntry> Blacklist { get; set; } = [];
}
