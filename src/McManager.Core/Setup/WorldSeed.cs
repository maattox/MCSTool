namespace McManager.Core.Setup;

/// <summary>
/// Minecraft <c>level-seed</c> text. Blank means Minecraft picks a random seed.
/// </summary>
public static class WorldSeed
{
    public const int MaxLength = 256;

    /// <summary>
    /// Trim, strip CR/LF/NUL, cap at <see cref="MaxLength"/>. Empty = random.
    /// </summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";

        var s = raw.Trim().Replace("\r", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal)
            .Replace("\0", "", StringComparison.Ordinal);
        if (s.Length > MaxLength)
            s = s[..MaxLength];
        return s;
    }
}
