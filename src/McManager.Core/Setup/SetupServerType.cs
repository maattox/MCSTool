namespace McManager.Core.Setup;

/// <summary>
/// Setup server-type branch: Vanilla (Default / Optimized Paper) vs Modded (local pack file).
/// Not a public/private toggle. Quilt is detect-only — not a Setup radio.
/// </summary>
public static class SetupServerType
{
    public const string Vanilla = "vanilla";
    public const string Modded = "modded";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Modded, StringComparison.OrdinalIgnoreCase)
            ? Modded
            : Vanilla;

    public static bool IsModded(string? value) =>
        string.Equals(Normalize(value), Modded, StringComparison.OrdinalIgnoreCase);

    public static bool IsVanilla(string? value) => !IsModded(value);
}
