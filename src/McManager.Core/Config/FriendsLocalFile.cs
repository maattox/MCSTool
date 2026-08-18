using System.Text.Json.Serialization;

namespace McManager.Core.Config;

public sealed class FriendsLocalFile
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("friends")]
    public List<FriendEntry> Friends { get; init; } = [];
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

/// <summary>Object Storage <c>ip/allowlist.json</c>. <c>ip</c> is a single IPv4 or IPv4 CIDR.</summary>
public sealed class IpAllowlistDocument
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    [JsonPropertyName("mode_note")]
    public string ModeNote { get; set; } =
        "Product is private-only. This allowlist is always applied. ip/mode.json is withdrawn.";

    [JsonPropertyName("entries")]
    public List<FriendEntry> Entries { get; set; } = [];
}
