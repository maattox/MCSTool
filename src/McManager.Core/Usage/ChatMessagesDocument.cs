using System.Text.Json.Serialization;

namespace McManager.Core.Usage;

/// <summary>
/// Object Storage <c>messages/chat.json</c> — MOTD-scale identity + VM1 chat templates (v1).
/// Manager is the writer; VM1 idle/boot is the consumer. Description may include <c>§</c> codes.
/// </summary>
public sealed class ChatMessagesDocument
{
    public const int DocumentVersion = 1;
    public const string FileName = "chat.json";
    public const string IconFileName = "server-icon.png";
    public const string DoorIdleFileName = "door-idle.png";
    public const string DoorStartingFileName = "door-starting.png";
    public const string DoorExhaustedFileName = "door-exhausted.png";

    public static readonly IReadOnlyDictionary<string, string> DefaultChatMessages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["budget_warn_leftover"] =
                "Daily usage limit exceeded; using leftover hours (~{ocpu:.1f} OCPU-h / ~{gb:.1f} GB-h left).",
            ["budget_final_warn"] = "Daily + leftover usage exhausted. Server will shut down soon.",
            ["budget_stop"] = "Usage limits reached. Server shutting down.",
            ["soft_cap_stop"] = "Monthly usage soft cap reached. Server shutting down.",
            ["idle_stop"] = "No players for {minutes} minutes. Saving and shutting down.",
            ["idle_stop_inactive"] =
                "Minecraft not running for {minutes} minutes. Saving and shutting down.",
            ["admin_stop"] = "Admin requested shutdown. Saving world…",
        };

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";

    /// <summary>Player-facing server name (Manager display + MOTD first line).</summary>
    [JsonPropertyName("server_name")]
    public string ServerName { get; set; } = "";

    /// <summary>
    /// MOTD second list line. May include Minecraft <c>§</c> codes. One line, 59 visible characters.
    /// </summary>
    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>
    /// Ignored. List MOTD is always <see cref="ServerName"/> then <see cref="Description"/>.
    /// Kept so older <c>chat.json</c> still deserializes.
    /// </summary>
    [JsonPropertyName("motd_omit_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool MotdOmitName { get; set; }

    /// <summary>Optional Object Storage key for the 64×64 PNG, typically <c>messages/server-icon.png</c>.</summary>
    [JsonPropertyName("icon_object")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IconObject { get; set; }

    [JsonPropertyName("chat_messages")]
    public Dictionary<string, string> ChatMessages { get; set; } = new(StringComparer.Ordinal);

    public static ChatMessagesDocument Defaults(DateTimeOffset? nowUtc = null)
    {
        var doc = new ChatMessagesDocument
        {
            Version = DocumentVersion,
            ChatMessages = new Dictionary<string, string>(
                DefaultChatMessages,
                StringComparer.Ordinal),
        };
        doc.StampUpdated(nowUtc);
        return doc;
    }

    public void StampUpdated(DateTimeOffset? nowUtc = null)
    {
        UpdatedAt = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    /// <summary>
    /// Fill missing template keys from built-in defaults. Does not overwrite a non-empty stored string.
    /// </summary>
    public void FillMissingChatKeys()
    {
        ChatMessages ??= new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in DefaultChatMessages)
        {
            if (!ChatMessages.TryGetValue(pair.Key, out var existing)
                || string.IsNullOrWhiteSpace(existing))
            {
                ChatMessages[pair.Key] = pair.Value;
            }
        }
    }
}
