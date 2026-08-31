using System.Text.Json.Serialization;
using McManager.Core.Services;

namespace McManager.Core.Usage;

/// <summary>
/// Object Storage <c>messages/server-properties.json</c> — curated gameplay keys (after-v1 P3).
/// Manager is the writer; VM1 boot/pull is the consumer. MOTD is not stored here.
/// </summary>
public sealed class ServerPropertiesDocument
{
    public const int DocumentVersion = 1;
    public const string FileName = "server-properties.json";

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    /// <summary>Allowlisted <c>server.properties</c> keys as string values.</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, string> Properties { get; set; } = new(StringComparer.Ordinal);

    public static ServerPropertiesDocument Defaults(DateTimeOffset? nowUtc = null)
    {
        var doc = new ServerPropertiesDocument
        {
            Version = DocumentVersion,
            Properties = new Dictionary<string, string>(
                ServerPropertiesCatalog.ProductDefaults,
                StringComparer.Ordinal),
        };
        doc.StampUpdated(nowUtc);
        return doc;
    }

    public void StampUpdated(DateTimeOffset? nowUtc = null)
    {
        UpdatedAt = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }
}
