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
