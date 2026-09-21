namespace McManager.Core.Setup;

/// <summary>
/// Minecraft heap presets (Xms = Xmx). Cap is host RAM minus ~4 GB OS/agent headroom;
/// product VMs are 12 GB (max 8G) or 24 GB (max 12G).
/// </summary>
public static class JvmHeapChoice
{
    public const string Default = "4G";
    public const string Medium = "6G";
    public const string Large = "8G";
    public const string Extra = "10G";
    public const string ExtraLarge = "12G";
    public const int OsHeadroomGb = 4;
    public const int MaxOfferedGb = 12;

    public static readonly string[] Presets = [Default, Medium, Large, Extra, ExtraLarge];

    public static bool IsAllowed(string? token)
    {
        var t = (token ?? "").Trim();
        foreach (var p in Presets)
        {
            if (p.Equals(t, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string Normalize(string? token) =>
        IsAllowed(token) ? token!.Trim().ToUpperInvariant() : Default;

    public static int Gigabytes(string? token)
    {
        var n = Normalize(token);
        if (n.Length > 1
            && n.EndsWith('G')
            && int.TryParse(n.AsSpan(0, n.Length - 1), out var gb)
            && gb > 0)
        {
            return gb;
        }

        return 4;
    }

    /// <summary>Largest offered preset that still leaves <see cref="OsHeadroomGb"/> on the host.</summary>
    public static string MaxForHostMemoryGb(int hostMemoryGb)
    {
        var capGb = Math.Min(MaxOfferedGb, Math.Max(4, hostMemoryGb - OsHeadroomGb));
        var best = Default;
        foreach (var p in Presets)
        {
            if (Gigabytes(p) <= capGb)
                best = p;
        }

        return best;
    }

    public static bool FitsHost(string? token, int hostMemoryGb) =>
        Gigabytes(token) <= Gigabytes(MaxForHostMemoryGb(hostMemoryGb));

    public static string ClampToHost(string? token, int hostMemoryGb)
    {
        var n = Normalize(token);
        return FitsHost(n, hostMemoryGb) ? n : MaxForHostMemoryGb(hostMemoryGb);
    }

    public static bool OffersExtraPresets(int hostMemoryGb) =>
        Gigabytes(MaxForHostMemoryGb(hostMemoryGb)) >= 10;

    public static IReadOnlyList<string> PresetsForHostMemoryGb(int hostMemoryGb)
    {
        var cap = Gigabytes(MaxForHostMemoryGb(hostMemoryGb));
        var list = new List<string>(Presets.Length);
        foreach (var p in Presets)
        {
            if (Gigabytes(p) <= cap)
                list.Add(p);
        }

        return list;
    }

    /// <summary>Treat missing/zero <c>vm1.shape_memory_gb</c> as the 24 GB product default.</summary>
    public static int ResolvedHostMemoryGb(double hostMemoryGb) =>
        hostMemoryGb > 0
            ? Math.Max(1, (int)Math.Round(hostMemoryGb))
            : Vm1ShapeChoice.DefaultMemoryGb;

    public static string Format(string? token) => Normalize(token);

    /// <summary>
    /// Next larger offered preset that still fits the host, or null when already at the cap.
    /// </summary>
    public static string? NextLarger(string? token, int hostMemoryGb)
    {
        var currentGb = Gigabytes(token);
        var capGb = Gigabytes(MaxForHostMemoryGb(hostMemoryGb));
        foreach (var p in Presets)
        {
            var gb = Gigabytes(p);
            if (gb > currentGb && gb <= capGb)
                return p;
        }

        return null;
    }
}
