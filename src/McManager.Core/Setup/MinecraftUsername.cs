using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>Mojang account name rules used by Vanilla whitelist.</summary>
public static class MinecraftUsername
{
    private static readonly Regex Pattern = new(
        "^[A-Za-z0-9_]{3,16}$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool IsValid(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return Pattern.IsMatch(trimmed);
    }

    public static string Normalize(string name) => name.Trim();
}
