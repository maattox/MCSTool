using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupVanillaFlavorTests
{
    [Theory]
    [InlineData(null, SetupVanillaFlavor.Default)]
    [InlineData("", SetupVanillaFlavor.Default)]
    [InlineData("default", SetupVanillaFlavor.Default)]
    [InlineData("DEFAULT", SetupVanillaFlavor.Default)]
    [InlineData("optimized", SetupVanillaFlavor.Optimized)]
    [InlineData("Optimized", SetupVanillaFlavor.Optimized)]
    [InlineData("paper", SetupVanillaFlavor.Default)]
    public void Normalize_maps_unknown_to_default(string? raw, string expected)
    {
        Assert.Equal(expected, SetupVanillaFlavor.Normalize(raw));
    }

    [Fact]
    public void ToDistribution_default_is_vanilla_optimized_is_paper()
    {
        Assert.Equal("vanilla", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.Equal("paper", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Optimized));
        Assert.NotEqual("forge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.NotEqual("forge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Optimized));
        Assert.NotEqual("neoforge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.NotEqual("neoforge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Optimized));
    }

    [Fact]
    public void Plan_summary_names_default_and_optimized_paths()
    {
        var def = new SetupWizardState
        {
            MinecraftVersion = "1.21.11",
            VanillaFlavor = SetupVanillaFlavor.Default,
            EulaAccepted = true,
        };
        var opt = new SetupWizardState
        {
            MinecraftVersion = "1.21.10",
            VanillaFlavor = SetupVanillaFlavor.Optimized,
            EulaAccepted = true,
        };

        var defText = InfraPlanSummary.Build(def);
        var optText = InfraPlanSummary.Build(opt);

        Assert.Contains("Default Vanilla 1.21.11", defText, StringComparison.Ordinal);
        Assert.DoesNotContain("Paper", defText, StringComparison.Ordinal);
        Assert.Contains("Server list name: " + ServerIdentityUx.DefaultName, defText, StringComparison.Ordinal);
        Assert.Contains("Optimized Vanilla (Paper) 1.21.10", optText, StringComparison.Ordinal);
        Assert.Contains("Server list name: " + ServerIdentityUx.DefaultName, optText, StringComparison.Ordinal);
    }
}
