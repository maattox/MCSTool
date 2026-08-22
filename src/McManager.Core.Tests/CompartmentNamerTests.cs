using McManager.Core.Config;
using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class CompartmentNamerTests
{
    [Fact]
    public void Next_available_is_mcmgr_when_none_taken()
    {
        Assert.Equal("mcmgr", CompartmentNamer.NextAvailable([]));
        Assert.Equal("mcmgr", CompartmentNamer.NextAvailable(["Default", "ManagedCompartmentForPaaS"]));
    }

    [Fact]
    public void Next_available_suffixes_when_mcmgr_exists()
    {
        Assert.Equal("mcmgr-2", CompartmentNamer.NextAvailable(["mcmgr"]));
        Assert.Equal("mcmgr-2", CompartmentNamer.NextAvailable(["MCMGR", "other"]));
        Assert.Equal("mcmgr-3", CompartmentNamer.NextAvailable(["mcmgr", "mcmgr-2"]));
        Assert.Equal("mcmgr-4", CompartmentNamer.NextAvailable(["mcmgr", "mcmgr-2", "mcmgr-3"]));
    }

    [Fact]
    public void Next_available_skips_holes()
    {
        Assert.Equal("mcmgr-2", CompartmentNamer.NextAvailable(["mcmgr", "mcmgr-3"]));
    }

    [Fact]
    public void Next_available_ignores_null_and_blank()
    {
        Assert.Equal("mcmgr", CompartmentNamer.NextAvailable([null, "  ", ""]));
    }

    [Fact]
    public void Next_available_exhausted_throws()
    {
        var names = new List<string> { "mcmgr" };
        for (var n = 2; n <= CompartmentNamer.MaxNumericSuffix; n++)
            names.Add($"mcmgr-{n}");

        Assert.Throws<InvalidOperationException>(() => CompartmentNamer.NextAvailable(names));
        Assert.False(CompartmentNamer.TryNextAvailable(names, out _));
    }

    [Theory]
    [InlineData("mcmgr")]
    [InlineData("MCMGR")]
    [InlineData("mcmgr-2")]
    [InlineData("mcmgr-10")]
    [InlineData("mcmgr-1")]
    public void Product_name_accepts_base_and_numeric_suffix(string name)
    {
        Assert.True(CompartmentNamer.IsProductName(name));
        Assert.True(ConnectExistingService.IsProductCompartment(name, null));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("mcmgr-foo")]
    [InlineData("mcmgr2")]
    [InlineData("mcmgr-0")]
    [InlineData("Default")]
    public void Product_name_rejects_other_strings(string? name)
    {
        Assert.False(CompartmentNamer.IsProductName(name));
        Assert.False(ConnectExistingService.IsProductCompartment(name, null));
    }

    [Fact]
    public void Product_compartment_still_matches_domain_tag()
    {
        var tags = new Dictionary<string, string>
        {
            ["mcmgr-domain"] = "mc-server-compartment",
        };
        Assert.True(ConnectExistingService.IsProductCompartment("Default", tags));
        Assert.False(ConnectExistingService.IsProductCompartment("Default", new Dictionary<string, string>()));
    }

    [Fact]
    public void Reuse_when_local_tofu_state_or_apply_past_tofu()
    {
        Assert.True(CompartmentNamer.ShouldReuseAssignedName("mcmgr", SetupApplyStage.NotStarted, hasLocalTofuState: true));
        Assert.True(CompartmentNamer.ShouldReuseAssignedName("mcmgr-2", SetupApplyStage.TofuApplied, hasLocalTofuState: false));
        Assert.False(CompartmentNamer.ShouldReuseAssignedName("mcmgr", SetupApplyStage.NotStarted, hasLocalTofuState: false));
        Assert.False(CompartmentNamer.ShouldReuseAssignedName(" ", SetupApplyStage.TofuApplied, hasLocalTofuState: true));
    }

    [Fact]
    public void Plan_summary_auto_names_and_does_not_offer_paste_ocid()
    {
        var text = InfraPlanSummary.Build(new SetupWizardState { CompartmentName = "mcmgr" });
        Assert.Contains("`mcmgr-2`", text, StringComparison.Ordinal);
        Assert.Contains("`mcmgr-3`", text, StringComparison.Ordinal);
        Assert.DoesNotContain("existing OCID", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Use existing compartment", text, StringComparison.Ordinal);
    }
}
