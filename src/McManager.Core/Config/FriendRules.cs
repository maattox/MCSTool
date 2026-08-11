using System.Text.RegularExpressions;

namespace McManager.Core.Config;

public static class FriendRules
{
    public const string McTagPrefix = "mc-whitelist:";
    public const string SshTagLegacy = "mc-ssh-admin";
    public const string SshAccessSuffix = " SSH access";
    public const string DoorAccessSuffix = " door access";

    private static readonly Regex Ipv4Regex = new(
        @"^(?:(?:25[0-5]|2[0-4]\d|[01]?\d\d?)\.){3}(?:25[0-5]|2[0-4]\d|[01]?\d\d?)$",
        RegexOptions.Compiled);

    public static string NormalizeIp(string value)
    {
        var v = value.Trim();
        if (v.Contains('/'))
            v = v.Split('/', 2)[0].Trim();

        if (!Ipv4Regex.IsMatch(v))
            throw new FormatException($"Invalid IPv4 address: {value}");

        return v;
    }

    public static bool TryNormalizeIp(string value, out string ip)
    {
        try
        {
            ip = NormalizeIp(value);
            return true;
        }
        catch (FormatException)
        {
            ip = "";
            return false;
        }
    }

    public static string ToCidr(string ip) => $"{NormalizeIp(ip)}/32";

    public static string McDescription(string name, string ipFallback) =>
        string.IsNullOrWhiteSpace(name) ? NormalizeIp(ipFallback) : name.Trim();

    public static string SshDescription(string name) =>
        $"{name.Trim()}{SshAccessSuffix}";

    public static string DoorDescription(string name) =>
        $"{name.Trim()}{DoorAccessSuffix}";

    public static bool IsSshOwnedDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var desc = description.Trim();
        return desc == SshTagLegacy || desc.EndsWith(SshAccessSuffix, StringComparison.Ordinal);
    }

    public static bool IsDoorOwnedDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        return description.Trim().EndsWith(DoorAccessSuffix, StringComparison.Ordinal);
    }

    public static bool IsOwnedDescription(string? description, IReadOnlySet<string>? friendNames = null)
    {
        if (string.IsNullOrWhiteSpace(description))
            return false;

        var desc = description.Trim();
        if (desc.StartsWith(McTagPrefix, StringComparison.Ordinal)
            || IsSshOwnedDescription(desc)
            || IsDoorOwnedDescription(desc))
        {
            return true;
        }

        return friendNames is not null && friendNames.Contains(desc);
    }

    public static bool IsSingleHostCidr(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var s = source.Trim();
        if (s.EndsWith("/32", StringComparison.Ordinal))
            return true;

        return Ipv4Regex.IsMatch(s);
    }
}
