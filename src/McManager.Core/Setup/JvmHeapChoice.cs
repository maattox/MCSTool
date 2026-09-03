namespace McManager.Core.Setup;

/// <summary>
/// Minecraft heap presets (Xms = Xmx). Cap is host RAM minus ~4 GB OS/agent headroom;
/// product VMs are 12 GB or 24 GB, so 4G / 6G / 8G are all inside the cap.
/// </summary>
public static class JvmHeapChoice
{
    public const string Default = "4G";
    public const string Medium = "6G";
    public const string Large = "8G";
    public const int OsHeadroomGb = 4;
    public const int MaxOfferedGb = 8;

    public static readonly string[] Presets = [Default, Medium, Large];

    public static bool IsAllowed(string? token)
    {
        var t = (token ?? "").Trim();
        return t.Equals(Default, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Medium, StringComparison.OrdinalIgnoreCase)
            || t.Equals(Large, StringComparison.OrdinalIgnoreCase);
    }

    public static string Normalize(string? token) =>
        IsAllowed(token) ? token!.Trim().ToUpperInvariant() : Default;

    public static int Gigabytes(string? token) =>
        Normalize(token) switch
        {
            Medium => 6,
            Large => 8,
            _ => 4,
        };

    /// <summary>Largest offered preset that still leaves <see cref="OsHeadroomGb"/> on the host.</summary>
    public static string MaxForHostMemoryGb(int hostMemoryGb)
    {
        var capGb = Math.Min(MaxOfferedGb, Math.Max(4, hostMemoryGb - OsHeadroomGb));
        if (capGb >= 8)
            return Large;
        if (capGb >= 6)
            return Medium;
        return Default;
    }

    public static bool FitsHost(string? token, int hostMemoryGb) =>
        Gigabytes(token) <= Gigabytes(MaxForHostMemoryGb(hostMemoryGb));

    public static string Format(string? token) => Normalize(token);
}
