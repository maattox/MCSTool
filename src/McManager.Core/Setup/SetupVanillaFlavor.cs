namespace McManager.Core.Setup;

/// <summary>
/// Setup Vanilla branch: Default (Mojang jar) vs Optimized (Paper).
/// Modded is a later step — this is not a server-type toggle.
/// </summary>
public static class SetupVanillaFlavor
{
    public const string Default = "default";
    public const string Optimized = "optimized";

    public const string DistributionVanilla = "vanilla";
    public const string DistributionPaper = "paper";

    public static string Normalize(string? value) =>
        string.Equals(value?.Trim(), Optimized, StringComparison.OrdinalIgnoreCase)
            ? Optimized
            : Default;

    public static bool IsOptimized(string? value) =>
        string.Equals(Normalize(value), Optimized, StringComparison.OrdinalIgnoreCase);

    public static string ToDistribution(string? flavor) =>
        IsOptimized(flavor) ? DistributionPaper : DistributionVanilla;

    public static string PlanLabel(string? flavor) =>
        IsOptimized(flavor) ? "Optimized Vanilla (Paper)" : "Default Vanilla";
}
