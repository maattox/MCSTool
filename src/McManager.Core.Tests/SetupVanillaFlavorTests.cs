using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupVanillaFlavorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("optimized")]
    [InlineData("Optimized")]
    [InlineData("paper")]
    public void Normalize_always_paper(string? raw)
    {
        Assert.Equal(SetupVanillaFlavor.Optimized, SetupVanillaFlavor.Normalize(raw));
        Assert.True(SetupVanillaFlavor.IsOptimized(raw));
        Assert.Equal(SetupVanillaFlavor.DistributionPaper, SetupVanillaFlavor.ToDistribution(raw));
        Assert.Equal(SetupVanillaFlavor.PlanLabelPaper, SetupVanillaFlavor.PlanLabel(raw));
    }

    [Fact]
    public void ToDistribution_never_mojang_vanilla()
    {
        Assert.Equal("paper", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.Equal("paper", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Optimized));
        Assert.NotEqual("vanilla", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.NotEqual("forge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Default));
        Assert.NotEqual("neoforge", SetupVanillaFlavor.ToDistribution(SetupVanillaFlavor.Optimized));
    }

    [Fact]
    public void Plan_summary_names_paper_as_vanilla_like_path()
    {
        var leftoverFlavor = new SetupWizardState
        {
            MinecraftVersion = "1.21.11",
            VanillaFlavor = SetupVanillaFlavor.Default,
            EulaAccepted = true,
        };
        var paper = new SetupWizardState
        {
            MinecraftVersion = "1.21.10",
            VanillaFlavor = SetupVanillaFlavor.Optimized,
            EulaAccepted = true,
        };

        var leftoverText = InfraPlanSummary.Build(leftoverFlavor);
        var paperText = InfraPlanSummary.Build(paper);

        Assert.Contains("Vanilla (Paper) 1.21.11", leftoverText, StringComparison.Ordinal);
        Assert.DoesNotContain("Default Vanilla", leftoverText, StringComparison.Ordinal);
        Assert.Contains("Server list name: " + ServerIdentityUx.DefaultName, leftoverText, StringComparison.Ordinal);
        Assert.Contains("Vanilla (Paper) 1.21.10", paperText, StringComparison.Ordinal);
        Assert.Contains("Server list name: " + ServerIdentityUx.DefaultName, paperText, StringComparison.Ordinal);
    }
}
