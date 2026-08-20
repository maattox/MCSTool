namespace McManager.Core.Setup;

/// <summary>
/// Always Free–eligible VM1 A1 Flex sizes offered in Setup.
/// Distinct from v1 Danger Zone day-2 resize.
/// </summary>
public static class Vm1ShapeChoice
{
    public const int DefaultOcpus = 4;
    public const int DefaultMemoryGb = 24;
    public const int SmallerOcpus = 2;
    public const int SmallerMemoryGb = 12;

    public static bool IsAllowed(int ocpus, int memoryGb) =>
        (ocpus == DefaultOcpus && memoryGb == DefaultMemoryGb)
        || (ocpus == SmallerOcpus && memoryGb == SmallerMemoryGb);

    public static (int Ocpus, int MemoryGb) Normalize(int ocpus, int memoryGb) =>
        IsAllowed(ocpus, memoryGb)
            ? (ocpus, memoryGb)
            : (DefaultOcpus, DefaultMemoryGb);

    public static string Format(int ocpus, int memoryGb)
    {
        var n = Normalize(ocpus, memoryGb);
        return $"{n.Ocpus} OCPU / {n.MemoryGb} GB";
    }

    public static bool IsDefault(int ocpus, int memoryGb)
    {
        var n = Normalize(ocpus, memoryGb);
        return n.Ocpus == DefaultOcpus;
    }

    /// <summary>Novice hours/headroom copy for confirm + plan summary.</summary>
    public static string HoursHint(int ocpus, int memoryGb)
    {
        var n = Normalize(ocpus, memoryGb);
        return n.Ocpus == SmallerOcpus
            ? "smaller Always Free size — Vanilla can often stay on all month; less room if you add mods or more players later"
            : "recommended — more room for players and later mods; uses Always Free hours faster while the server is on";
    }
}
