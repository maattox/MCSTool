namespace McManager.Core.Setup;

/// <summary>
/// Static Minecraft → Java-major floor (blueprint §9.1). Loaders without a
/// per-version Java API (Fabric/Quilt/Forge/NeoForge, and pack analysis) share
/// this table. Prefer Paper Fill <c>minimumJavaVersion</c> or Mojang
/// <c>javaVersion.majorVersion</c> when those clients have the field.
/// </summary>
public static class MinecraftJavaFloor
{
    /// <summary>
    /// Maps a Minecraft version id to the product Java major floor.
    /// Returns false for empty/unrecognized ids (do not guess).
    /// </summary>
    public static bool TryGet(string? minecraftVersion, out int javaMajor)
    {
        javaMajor = 0;
        if (string.IsNullOrWhiteSpace(minecraftVersion))
            return false;

        var id = minecraftVersion.Trim();
        if (id.StartsWith("26.", StringComparison.OrdinalIgnoreCase) || StartsWithMc(id, "26"))
        {
            javaMajor = 25;
            return true;
        }

        if (StartsWithMc(id, "1.21") || StartsWithMc(id, "1.22")
            || StartsWithMc(id, "1.20.5") || StartsWithMc(id, "1.20.6"))
        {
            javaMajor = 21;
            return true;
        }

        if (StartsWithMc(id, "1.18") || StartsWithMc(id, "1.19") || StartsWithMc(id, "1.20"))
        {
            javaMajor = 17;
            return true;
        }

        if (StartsWithMc(id, "1.17"))
        {
            javaMajor = 16;
            return true;
        }

        if (StartsWithMc(id, "1.12") || StartsWithMc(id, "1.13") || StartsWithMc(id, "1.14")
            || StartsWithMc(id, "1.15") || StartsWithMc(id, "1.16"))
        {
            javaMajor = 8;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Prefix match that does not treat <c>1.20</c> as a prefix of <c>1.200</c>.
    /// <c>1.20.5</c> still matches prefix <c>1.20</c> (next char is <c>.</c>).
    /// Call more-specific prefixes (1.20.5) before 1.20.
    /// </summary>
    public static bool StartsWithMc(string id, string prefix)
    {
        if (!id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        return id.Length == prefix.Length || !char.IsDigit(id[prefix.Length]);
    }
}
