using System.Text.RegularExpressions;

namespace McManager.Core.Setup;

/// <summary>Mojang account name rules (optional wizard field; not a join gate).</summary>
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

    /// <summary>Empty is OK (join uses OCI Security List). Non-empty values must be valid Mojang names.</summary>
    public static bool IsMissingOrValid(string? name)
    {
        var trimmed = name?.Trim() ?? "";
        return trimmed.Length == 0 || IsValid(trimmed);
    }

    public static string Normalize(string name) => name.Trim();
}
