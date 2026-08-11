using System.Text.Json.Serialization;

namespace McManager.Core.Usage;

/// <summary>Object Storage <c>meta/flags.json</c> dirty-flag protocol.</summary>
public sealed class MetaFlagsDocument
{
    public static readonly string[] Categories = ["ledger", "budget", "meta", "ip", "messages"];
    public static readonly string[] Consumers = ["manager", "door", "vm1"];

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("categories")]
    public Dictionary<string, Dictionary<string, bool>> CategoriesMap { get; set; } = new(StringComparer.Ordinal);

    [JsonPropertyName("help")]
    public string? Help { get; set; }

    public static MetaFlagsDocument Empty()
    {
        var doc = new MetaFlagsDocument
        {
            Help =
                "When a writer updates a category, set that category's consumer "
                + "flags to true so each side knows to pull. Consumers clear only "
                + "their own flag after a successful pull. Writers clear their "
                + "own consumer bit (they already have the data).",
        };
        doc.Normalize();
        doc.StampUpdated();
        return doc;
    }

    public void Normalize()
    {
        Version = Version <= 0 ? 1 : Version;
        var cats = new Dictionary<string, Dictionary<string, bool>>(StringComparer.Ordinal);
        foreach (var cat in Categories)
        {
            CategoriesMap.TryGetValue(cat, out var src);
            var row = new Dictionary<string, bool>(StringComparer.Ordinal);
            foreach (var consumer in Consumers)
            {
                row[consumer] = src is not null
                                && src.TryGetValue(consumer, out var v)
                                && v;
            }

            cats[cat] = row;
        }

        CategoriesMap = cats;
    }

    public bool IsDirty(string category, string consumer)
    {
        Normalize();
        return CategoriesMap.TryGetValue(category, out var row)
               && row.TryGetValue(consumer, out var dirty)
               && dirty;
    }

    public void MarkDirty(string category, IEnumerable<string> consumers, string? clearWriter = null)
    {
        Normalize();
        if (!CategoriesMap.TryGetValue(category, out var row))
        {
            row = new Dictionary<string, bool>(StringComparer.Ordinal);
            CategoriesMap[category] = row;
        }

        foreach (var c in consumers)
        {
            if (Consumers.Contains(c, StringComparer.Ordinal))
                row[c] = true;
        }

        if (!string.IsNullOrWhiteSpace(clearWriter)
            && Consumers.Contains(clearWriter, StringComparer.Ordinal))
        {
            row[clearWriter] = false;
        }

        StampUpdated();
    }

    public void ClearFlag(string category, string consumer)
    {
        Normalize();
        if (!CategoriesMap.TryGetValue(category, out var row))
            return;
        if (Consumers.Contains(consumer, StringComparer.Ordinal))
            row[consumer] = false;
        StampUpdated();
    }

    public void StampUpdated(DateTimeOffset? nowUtc = null)
    {
        var now = (nowUtc ?? DateTimeOffset.UtcNow).UtcDateTime;
        UpdatedAt = now.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
    }

    public string SummarizeBudgetFlags()
    {
        Normalize();
        if (!CategoriesMap.TryGetValue("budget", out var row))
            return "budget flags unavailable";

        static string Bit(bool v) => v ? "Y" : "-";
        return $"budget door={Bit(row.GetValueOrDefault("door"))} "
               + $"vm1={Bit(row.GetValueOrDefault("vm1"))} "
               + $"manager={Bit(row.GetValueOrDefault("manager"))}";
    }
}
