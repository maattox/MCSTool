namespace McManager.Core.Setup;

/// <summary>
/// Setup server-memory suggestion from server type, kept server-side jar count, and VM RAM.
/// Never suggests 12G; host cap still applies (8G on 12 GB).
/// </summary>
public static class JvmHeapGuess
{
    public const int MediumJarExclusiveMax = 40;
    public const int LargeJarExclusiveMax = 100;

    public static string UncappedToken(bool isModded, int serverSideJarCount)
    {
        if (!isModded)
            return JvmHeapChoice.Default;

        var jars = Math.Max(0, serverSideJarCount);
        if (jars < MediumJarExclusiveMax)
            return JvmHeapChoice.Medium;
        if (jars < LargeJarExclusiveMax)
            return JvmHeapChoice.Large;
        return JvmHeapChoice.Extra;
    }

    public static string UncappedToken(string? serverType, int serverSideJarCount) =>
        UncappedToken(SetupServerType.IsModded(serverType), serverSideJarCount);

    public static string Suggest(bool isModded, int serverSideJarCount, int hostMemoryGb) =>
        JvmHeapChoice.ClampToHost(UncappedToken(isModded, serverSideJarCount), hostMemoryGb);

    public static string Suggest(string? serverType, int serverSideJarCount, int hostMemoryGb) =>
        Suggest(SetupServerType.IsModded(serverType), serverSideJarCount, hostMemoryGb);

    public static bool IsCappedByHost(bool isModded, int serverSideJarCount, int hostMemoryGb)
    {
        var wantedGb = JvmHeapChoice.Gigabytes(UncappedToken(isModded, serverSideJarCount));
        var capGb = JvmHeapChoice.Gigabytes(JvmHeapChoice.MaxForHostMemoryGb(hostMemoryGb));
        return wantedGb > capGb;
    }

    public static bool IsCappedByHost(string? serverType, int serverSideJarCount, int hostMemoryGb) =>
        IsCappedByHost(SetupServerType.IsModded(serverType), serverSideJarCount, hostMemoryGb);
}
