using McManager.Core.Config;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class SetupWizardStoreTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 2)]
    [InlineData(4, 3)]
    [InlineData(5, 4)]
    [InlineData(6, 5)]
    [InlineData(7, 6)]
    [InlineData(8, 7)]
    public void V1_step_index_skips_removed_compartment_page(int oldStep, int expected)
    {
        Assert.Equal(expected, SetupWizardStore.MigrateStepIndexFromV1(oldStep));
    }

    [Fact]
    public void Normalize_v1_summary_lands_on_review_and_forces_create()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 1,
            CurrentStep = 8,
            CreateCompartment = false,
            CompartmentName = "custom-lab",
            ExistingCompartmentId = "ocid1.compartment.oc1..example",
        });

        Assert.Equal(SetupWizardState.CurrentSchemaVersion, state.SchemaVersion);
        Assert.Equal(SetupWizardState.StepSummary, state.CurrentStep);
        Assert.True(state.CreateCompartment);
        Assert.Equal("", state.ExistingCompartmentId);
        Assert.Equal(CompartmentNamer.BaseName, state.CompartmentName);
        Assert.Equal(8, SetupWizardState.StepCount);
        Assert.Equal(SetupWizardState.StepIdentity, 4);
    }

    [Fact]
    public void Normalize_v1_on_compartment_page_lands_on_oci()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 1,
            CurrentStep = 2,
        });
        Assert.Equal(SetupWizardState.StepOci, state.CurrentStep);
    }

    [Theory]
    [InlineData(4, 4)]
    [InlineData(5, 6)]
    [InlineData(6, 7)]
    [InlineData(7, 8)]
    public void V2_step_index_inserts_identity_page(int oldStep, int expected)
    {
        Assert.Equal(expected, SetupWizardStore.MigrateStepIndexFromV2(oldStep));
    }

    [Fact]
    public void Normalize_v2_review_lands_on_review()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 2,
            CurrentStep = 7,
            CompartmentName = "mcmgr-2",
        });
        Assert.Equal(SetupWizardState.StepSummary, state.CurrentStep);
        Assert.Equal("mcmgr-2", state.CompartmentName);
    }

    [Fact]
    public void Normalize_v2_eula_lands_on_eula()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 2,
            CurrentStep = 5,
        });
        Assert.Equal(SetupWizardState.StepEula, state.CurrentStep);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(3, 2)]
    [InlineData(8, 7)]
    public void V3_step_index_merges_email_into_oci(int oldStep, int expected)
    {
        Assert.Equal(expected, SetupWizardStore.MigrateStepIndexFromV3(oldStep));
    }

    [Fact]
    public void Normalize_v3_email_lands_on_oci()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 3,
            CurrentStep = 2,
        });
        Assert.Equal(SetupWizardState.StepOci, state.CurrentStep);
    }

    [Fact]
    public void Normalize_v3_eula_lands_on_eula()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 3,
            CurrentStep = 6,
            CompartmentName = "mcmgr-2",
        });
        Assert.Equal(SetupWizardState.StepEula, state.CurrentStep);
        Assert.Equal("mcmgr-2", state.CompartmentName);
    }

    [Fact]
    public void Normalize_v3_review_lands_on_review()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 3,
            CurrentStep = 8,
        });
        Assert.Equal(SetupWizardState.StepSummary, state.CurrentStep);
    }

    [Fact]
    public void Normalize_v4_does_not_shift_again()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 4,
            CurrentStep = 5,
            CompartmentName = "mcmgr-2",
        });
        Assert.Equal(SetupWizardState.StepEula, state.CurrentStep);
        Assert.Equal("mcmgr-2", state.CompartmentName);
    }

    [Fact]
    public void Normalize_clamps_out_of_range_step()
    {
        var state = SetupWizardStore.Normalize(new SetupWizardState
        {
            SchemaVersion = 4,
            CurrentStep = 99,
        });
        Assert.Equal(0, state.CurrentStep);
    }
}
