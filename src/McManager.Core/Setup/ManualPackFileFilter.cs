namespace McManager.Core.Setup;

/// <summary>
/// Combine in-jar side with the itzg/product CurseForge exclude lists (blueprint §24.3).
/// Matcher runs after reading in-jar metadata (robustness R3 / Step 8.7 P2). Same keep/exclude
/// order as <see cref="MrpackFileFilter"/>: force-include, pack/in-jar client, list exclude.
/// Unclear jars stay kept (server pack assumed).
/// </summary>
internal static class ManualPackFileFilter
{
    public enum Action
    {
        Install,
        SkipInJarMetadata,
        SkipOverrideList,
    }

    public static Action Decide(string? inJarEnvironment, ExcludeIncludeMatch match)
    {
        if (match.Keep)
            return Action.Install;

        if ((inJarEnvironment ?? "").Equals("client", StringComparison.OrdinalIgnoreCase))
            return Action.SkipInJarMetadata;

        if (match.Exclude)
            return Action.SkipOverrideList;

        return Action.Install;
    }

    public static bool IsModJarPath(string relativePath)
    {
        var n = (relativePath ?? "").Replace('\\', '/').Trim();
        return n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && n.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
            && !n.EndsWith('/');
    }

    public static bool IsRootModJar(string relativePath)
    {
        var n = (relativePath ?? "").Replace('\\', '/').Trim();
        if (n.Contains('/') || !n.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            return false;
        return !ManualServerPackAnalyzer.IsInstallerJarFileName(n);
    }
}
