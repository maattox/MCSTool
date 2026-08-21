namespace McManager.Core.Setup;

/// <summary>
/// Novice warning when a full pack replace keeps the world but Minecraft or the loader changes
/// (blueprint §12.3 / §28.1). Null when the save is wiped or the identity is unchanged/unknown.
/// </summary>
public static class PackReplaceSaveCompatibility
{
    public static string? Warn(
        string? currentMinecraftVersion,
        string? currentLoaderOrDistribution,
        string? newMinecraftVersion,
        string? newLoader)
    {
        var oldMc = NormalizeVersion(currentMinecraftVersion);
        var newMc = NormalizeVersion(newMinecraftVersion);
        var oldLoader = NormalizeLoader(currentLoaderOrDistribution);
        var newLoaderNorm = NormalizeLoader(newLoader);

        var mcChanged = oldMc.Length > 0 && newMc.Length > 0
            && !string.Equals(oldMc, newMc, StringComparison.OrdinalIgnoreCase);
        var loaderChanged = oldLoader.Length > 0 && newLoaderNorm.Length > 0
            && !string.Equals(oldLoader, newLoaderNorm, StringComparison.OrdinalIgnoreCase);

        if (!mcChanged && !loaderChanged)
            return null;

        var parts = new List<string>();
        if (mcChanged)
        {
            parts.Add(
                $"The new pack uses Minecraft {newMc} (this server is on {oldMc}). "
                + "The existing world may not load.");
        }

        if (loaderChanged)
        {
            var oldLabel = DisplayLoader(oldLoader);
            var newLabel = DisplayLoader(newLoaderNorm);
            if (IsVanillaLike(newLoaderNorm) && !IsVanillaLike(oldLoader))
            {
                parts.Add(
                    $"The new pack is {newLabel} (this server is on {oldLabel}). "
                    + "Blocks and items from the old mods will be missing from the world.");
            }
            else
            {
                parts.Add(
                    $"The new pack uses {newLabel} (this server is on {oldLabel}). "
                    + "The existing world may not load cleanly.");
            }
        }

        parts.Add("Download a world save first if that world matters.");
        return string.Join(" ", parts);
    }

    internal static string NormalizeLoader(string? loaderOrDistribution)
    {
        var id = (loaderOrDistribution ?? "").Trim().ToLowerInvariant();
        return id switch
        {
            "fabric" or "quilt" or "forge" or "neoforge" or "vanilla" or "paper" => id,
            "modded" => "",
            _ => id,
        };
    }

    internal static bool IsWorldOverlayRelative(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var n = relativePath.Replace('\\', '/').Trim().TrimStart('/');
        if (n.Length == 0)
            return false;
        var slash = n.IndexOf('/');
        var first = slash < 0 ? n : n[..slash];
        return first.Equals("world", StringComparison.OrdinalIgnoreCase)
            || first.Equals("worlds", StringComparison.OrdinalIgnoreCase)
            || first.Equals("saves", StringComparison.OrdinalIgnoreCase)
            || first.Equals("world_nether", StringComparison.OrdinalIgnoreCase)
            || first.Equals("world_the_end", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeVersion(string? version) => (version ?? "").Trim();

    private static bool IsVanillaLike(string loader) =>
        loader is "vanilla" or "paper";

    private static string DisplayLoader(string loader) =>
        loader switch
        {
            "fabric" => "Fabric",
            "forge" => "Forge",
            "neoforge" => "NeoForge",
            "quilt" => "Quilt",
            "vanilla" => "Vanilla",
            "paper" => "Paper",
            _ => loader,
        };
}
