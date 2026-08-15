using McManager.Core.Config;

namespace McManager.Core.Setup;

public sealed class OciConfigProfile
{
    public OciConfigProfile(
        string name,
        string region,
        string tenancy = "",
        string user = "",
        string fingerprint = "")
    {
        Name = name;
        Region = region;
        Tenancy = tenancy;
        User = user;
        Fingerprint = fingerprint;
    }

    public string Name { get; }
    public string Region { get; }
    public string Tenancy { get; }
    public string User { get; }
    public string Fingerprint { get; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Region) ? Name : $"{Name} ({Region})";

    public string DetailsText
    {
        get
        {
            var lines = new List<string>
            {
                $"Profile: {Name}",
                $"Region: {(string.IsNullOrWhiteSpace(Region) ? "(not set in ~/.oci/config)" : Region)}",
                $"Tenancy: {Abbreviate(Tenancy)}",
                $"User: {Abbreviate(User)}",
            };
            if (!string.IsNullOrWhiteSpace(Fingerprint))
                lines.Add($"Fingerprint: {Fingerprint}");
            return string.Join(Environment.NewLine, lines);
        }
    }

    private static string Abbreviate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "(not set in ~/.oci/config)";
        if (value.Length <= 36)
            return value;
        return value[..20] + "…" + value[^10];
    }
}

/// <summary>Reads profile names and <c>region=</c> from <c>~/.oci/config</c> (no API calls).</summary>
public static class OciConfigProfiles
{
    public static string DefaultConfigPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".oci", "config");
    }

    public static IReadOnlyList<OciConfigProfile> List(string? configPath = null)
    {
        configPath = LocalConfigStore.ExpandPath(configPath ?? DefaultConfigPath());
        if (!File.Exists(configPath))
            return [new OciConfigProfile("DEFAULT", "")];

        var profiles = new List<OciConfigProfile>();
        string? current = null;
        var region = "";
        var tenancy = "";
        var user = "";
        var fingerprint = "";

        void Flush()
        {
            if (current is not null)
                profiles.Add(new OciConfigProfile(current, region, tenancy, user, fingerprint));
        }

        foreach (var raw in File.ReadAllLines(configPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Flush();
                current = line[1..^1].Trim();
                region = "";
                tenancy = "";
                user = "";
                fingerprint = "";
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;

            current ??= "DEFAULT";
            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Equals("region", StringComparison.OrdinalIgnoreCase))
                region = value;
            else if (key.Equals("tenancy", StringComparison.OrdinalIgnoreCase))
                tenancy = value;
            else if (key.Equals("user", StringComparison.OrdinalIgnoreCase))
                user = value;
            else if (key.Equals("fingerprint", StringComparison.OrdinalIgnoreCase))
                fingerprint = value;
        }

        Flush();
        if (profiles.Count == 0)
            profiles.Add(new OciConfigProfile("DEFAULT", ""));

        return profiles;
    }

    public static string? TryGetValue(string? profileName, string key, string? configPath = null)
    {
        configPath = LocalConfigStore.ExpandPath(configPath ?? DefaultConfigPath());
        if (!File.Exists(configPath))
            return null;

        var wanted = string.IsNullOrWhiteSpace(profileName) ? "DEFAULT" : profileName.Trim();
        string? current = null;
        string? found = null;
        var inWanted = false;

        foreach (var raw in File.ReadAllLines(configPath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                current = line[1..^1].Trim();
                inWanted = string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            current ??= "DEFAULT";
            inWanted = string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase);
            if (!inWanted)
                continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
                continue;
            if (line[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase))
                found = line[(eq + 1)..].Trim();
        }

        return string.IsNullOrWhiteSpace(found) ? null : found;
    }
}
