namespace McManager.Core.Setup;

/// <summary>
/// Setup Vanilla branch is always Paper (vanilla-like). Resume JSON may still
/// contain <c>default</c>; Normalize maps it to Paper. Mojang Vanilla is not
/// offered. Forge is not a Vanilla-branch choice and is not offered next to
/// NeoForge; packs that declare Forge use the on-box <c>DISTRIBUTION=forge</c>
/// module.
/// </summary>
public static class SetupVanillaFlavor
{
    public const string Default = "default";
    public const string Optimized = "optimized";

    public const string DistributionVanilla = "vanilla";
    public const string DistributionPaper = "paper";

    public const string PlanLabelPaper = "Vanilla (Paper)";

    public static string Normalize(string? value)
    {
        _ = value;
        return Optimized;
    }

    public static bool IsOptimized(string? value) =>
        string.Equals(Normalize(value), Optimized, StringComparison.OrdinalIgnoreCase);

    public static string ToDistribution(string? flavor)
    {
        _ = flavor;
        return DistributionPaper;
    }

    public static string PlanLabel(string? flavor)
    {
        _ = flavor;
        return PlanLabelPaper;
    }
}
