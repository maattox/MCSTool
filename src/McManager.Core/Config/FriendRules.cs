using System.Globalization;
using System.Text.RegularExpressions;

namespace McManager.Core.Config;

/// <summary>
/// Allowlist source: a single IPv4 (stored without /32) or an IPv4 CIDR prefix
/// (stored as network/prefix). Prefixes /0–/8 are rejected for Minecraft.
/// </summary>
public readonly struct AllowlistSource
{
    public AllowlistSource(string stored, string cidr, int prefixLength)
    {
        Stored = stored;
        Cidr = cidr;
        PrefixLength = prefixLength;
    }

    public string Stored { get; }
    public string Cidr { get; }
    public int PrefixLength { get; }
    public bool IsSingleHost => PrefixLength >= 32;
}

public static class FriendRules
{
    public const string McTagPrefix = "mc-whitelist:";
    public const string SshTagLegacy = "mc-ssh-admin";
    public const string SshAccessSuffix = " SSH access";
    public const string DoorAccessSuffix = " door access";

    /// <summary>Minecraft CIDR floor: reject /0 through /8 (inclusive).</summary>
    public const int MinMinecraftPrefixLength = 9;

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

    public static bool TryNormalizeAllowlistSource(string value, out AllowlistSource source, out string error)
    {
        source = default;
        error = "";
        var v = value.Trim();
        if (string.IsNullOrWhiteSpace(v))
        {
            error = "IP is required.";
            return false;
        }

        string ipPart;
        int prefix;
        var slash = v.IndexOf('/');
        if (slash >= 0)
        {
            ipPart = v[..slash].Trim();
            var prefixText = v[(slash + 1)..].Trim();
            if (!int.TryParse(prefixText, NumberStyles.None, CultureInfo.InvariantCulture, out prefix)
                || prefix < 0
                || prefix > 32)
            {
                error = "Enter a valid IPv4 CIDR prefix (for example 172.56.0.0/16).";
                return false;
            }
        }
        else
        {
            ipPart = v;
            prefix = 32;
        }

        if (!Ipv4Regex.IsMatch(ipPart) || !TryIpv4ToUInt(ipPart, out var addr))
        {
            error = slash >= 0
                ? "Enter a valid IPv4 CIDR prefix (for example 172.56.0.0/16)."
                : "Enter a valid IPv4 address.";
            return false;
        }

        if (prefix < MinMinecraftPrefixLength)
        {
            error = "Prefixes /0 through /8 are too wide for Minecraft. Use a tighter prefix.";
            return false;
        }

        var networkIp = FormatIpv4(addr & PrefixMask(prefix));
        var cidr = $"{networkIp}/{prefix}";
        source = new AllowlistSource(
            prefix == 32 ? networkIp : cidr,
            cidr,
            prefix);
        return true;
    }

    public static string? WidthWarning(AllowlistSource source)
    {
        if (source.IsSingleHost)
            return null;

        var hosts = 1L << (32 - source.PrefixLength);
        return $"This prefix is wider than one host ({hosts.ToString("N0", CultureInfo.InvariantCulture)} addresses). Prefer the tightest prefix that matches the friend’s ISP.";
    }

    public static string ToMinecraftCidr(string stored)
    {
        if (!TryNormalizeAllowlistSource(stored, out var source, out var error))
            throw new FormatException(error);
        return source.Cidr;
    }

    public static string? ToAdminCidr(string stored, bool allowPrefix)
    {
        if (!TryNormalizeAllowlistSource(stored, out var source, out _))
            return null;
        if (source.IsSingleHost)
            return source.Cidr;
        return allowPrefix ? source.Cidr : null;
    }

    public static bool IsPrimaryAdmin(FriendEntry friend, string? adminName, IReadOnlyList<FriendEntry> friends)
    {
        if (!friend.IsAdmin)
            return false;

        if (!string.IsNullOrWhiteSpace(adminName))
        {
            return string.Equals(friend.Name.Trim(), adminName.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        var first = friends.FirstOrDefault(f => f.IsAdmin);
        if (first is null)
            return false;
        if (!string.IsNullOrWhiteSpace(friend.Id) && !string.IsNullOrWhiteSpace(first.Id))
            return string.Equals(friend.Id, first.Id, StringComparison.Ordinal);
        return ReferenceEquals(friend, first);
    }

    public static string McDescription(string name, string ipFallback)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();
        if (TryNormalizeAllowlistSource(ipFallback, out var source, out _))
            return source.Stored;
        return ipFallback.Trim();
    }

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

    private static uint PrefixMask(int prefix) =>
        prefix <= 0 ? 0u : prefix >= 32 ? 0xFFFFFFFFu : 0xFFFFFFFFu << (32 - prefix);

    private static bool TryIpv4ToUInt(string ip, out uint value)
    {
        value = 0;
        var parts = ip.Split('.');
        if (parts.Length != 4)
            return false;
        uint acc = 0;
        foreach (var part in parts)
        {
            if (!byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
                return false;
            acc = (acc << 8) | b;
        }

        value = acc;
        return true;
    }

    private static string FormatIpv4(uint value) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}.{3}",
            value >> 24,
            (value >> 16) & 0xFF,
            (value >> 8) & 0xFF,
            value & 0xFF);
}
