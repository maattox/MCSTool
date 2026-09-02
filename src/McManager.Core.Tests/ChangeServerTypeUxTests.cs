using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class ChangeServerTypeUxTests
{
    [Fact]
    public void Labels_match_setup_game_step()
    {
        Assert.Equal("Default Vanilla", ChangeServerTypeUx.LabelDefaultVanilla);
        Assert.Equal("Optimized Vanilla (Paper)", ChangeServerTypeUx.LabelPaper);
        Assert.Equal("Modded", ChangeServerTypeUx.LabelModded);
        Assert.Equal(ChangeServerTypeUx.LabelDefaultVanilla, SetupVanillaFlavor.PlanLabel(SetupVanillaFlavor.Default));
        Assert.Equal(ChangeServerTypeUx.LabelPaper, SetupVanillaFlavor.PlanLabel(SetupVanillaFlavor.Optimized));
    }

    [Fact]
    public void Kind_label_maps_meta_server_kind()
    {
        Assert.Equal(ChangeServerTypeUx.LabelDefaultVanilla, ChangeServerTypeUx.KindLabel("vanilla"));
        Assert.Equal(ChangeServerTypeUx.LabelPaper, ChangeServerTypeUx.KindLabel("paper"));
        Assert.Equal(ChangeServerTypeUx.LabelModded, ChangeServerTypeUx.KindLabel("modded"));
        Assert.Equal(ChangeServerTypeUx.LabelModded, ChangeServerTypeUx.KindLabel("fabric"));
        Assert.Equal("—", ChangeServerTypeUx.KindLabel(null));
        Assert.Equal("—", ChangeServerTypeUx.KindLabel(""));
    }

    [Fact]
    public void Direction_warnings_match_scrutiny()
    {
        Assert.Equal(
            ChangeServerTypeUx.VanillaPaperMild,
            ChangeServerTypeUx.DirectionWarning("vanilla", ChangeServerTypeUx.ChoicePaper));
        Assert.Equal(
            ChangeServerTypeUx.VanillaPaperMild,
            ChangeServerTypeUx.DirectionWarning("paper", ChangeServerTypeUx.ChoiceDefaultVanilla));
        Assert.Equal(
            ChangeServerTypeUx.AnyToModdedNote,
            ChangeServerTypeUx.DirectionWarning("vanilla", ChangeServerTypeUx.ChoiceModded));
        Assert.Equal(
            ChangeServerTypeUx.AnyToModdedNote,
            ChangeServerTypeUx.DirectionWarning("paper", ChangeServerTypeUx.ChoiceModded));
        Assert.Equal(
            ChangeServerTypeUx.ModdedToVanillaStrong,
            ChangeServerTypeUx.DirectionWarning("modded", ChangeServerTypeUx.ChoiceDefaultVanilla));
        Assert.Equal(
            ChangeServerTypeUx.ModdedToVanillaStrong,
            ChangeServerTypeUx.DirectionWarning("fabric", ChangeServerTypeUx.ChoicePaper));
        Assert.Null(ChangeServerTypeUx.DirectionWarning("vanilla", ChangeServerTypeUx.ChoiceDefaultVanilla));
        Assert.Null(ChangeServerTypeUx.DirectionWarning("paper", ChangeServerTypeUx.ChoicePaper));
        Assert.Null(ChangeServerTypeUx.DirectionWarning("modded", ChangeServerTypeUx.ChoiceModded));
    }

    [Fact]
    public void Confirm_names_stack_and_play_ip()
    {
        Assert.Contains("play IP", ChangeServerTypeUx.ConfirmKeepWorld, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a cloud Redeploy", ChangeServerTypeUx.ConfirmKeepWorld, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("play IP", ChangeServerTypeUx.ConfirmWipeWorld, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deleted", ChangeServerTypeUx.ConfirmWipeWorld, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ChangeServerTypeUx.ConfirmWipeWorld, ChangeServerTypeUx.ConfirmBody(wipeWorld: true));
        Assert.Equal(ChangeServerTypeUx.ConfirmKeepWorld, ChangeServerTypeUx.ConfirmBody(wipeWorld: false));
    }

    [Fact]
    public void Meta_kind_follows_setup_not_stack_text()
    {
        Assert.Equal("vanilla", ChangeServerTypeUx.ServerKindForMeta(ChangeServerTypeUx.ChoiceDefaultVanilla, null));
        Assert.Equal("paper", ChangeServerTypeUx.ServerKindForMeta(ChangeServerTypeUx.ChoicePaper, null));
        Assert.Equal("modded", ChangeServerTypeUx.ServerKindForMeta(ChangeServerTypeUx.ChoiceModded, "fabric"));
    }

    [Fact]
    public void Planner_refuses_modded_without_pack()
    {
        var plan = ChangeServerTypePlanner.TryCreate(
            ChangeServerTypeUx.ChoiceModded,
            "1.21.8",
            packPath: null,
            wipeWorld: false,
            "1.21.8",
            "vanilla");
        Assert.False(plan.Succeeded);
        Assert.Equal(ChangeServerTypeUx.MissingPackError, plan.Error);
    }

    [Fact]
    public void Planner_refuses_vanilla_without_version()
    {
        var plan = ChangeServerTypePlanner.TryCreate(
            ChangeServerTypeUx.ChoicePaper,
            "  ",
            packPath: null,
            wipeWorld: false,
            "1.21.8",
            "vanilla");
        Assert.False(plan.Succeeded);
        Assert.Equal(ChangeServerTypeUx.MissingVersionError, plan.Error);
    }

    [Fact]
    public void Planner_vanilla_to_paper_builds_onbox_distribution()
    {
        var plan = ChangeServerTypePlanner.TryCreate(
            ChangeServerTypeUx.ChoicePaper,
            "1.21.8",
            packPath: null,
            wipeWorld: false,
            "1.21.8",
            "vanilla");
        Assert.True(plan.Succeeded, plan.Error);
        Assert.Null(plan.Value!.Preview);
        Assert.False(plan.Value.WipeWorld);
        var state = ChangeServerTypePlanner.ToWizardState(plan.Value);
        Assert.Equal(SetupServerType.Vanilla, state.ServerType);
        Assert.Equal(SetupVanillaFlavor.Optimized, state.VanillaFlavor);
        Assert.Equal("1.21.8", state.MinecraftVersion);
        Assert.Equal("paper", SetupPackImport.ToDistribution(state));
        Assert.True(SetupPackImport.IsOnboxDistribution("paper"));
        Assert.NotNull(plan.Value.SaveCompatibilityWarning);
        Assert.Contains("Paper", plan.Value.SaveCompatibilityWarning, StringComparison.Ordinal);
        Assert.Contains("Vanilla", plan.Value.SaveCompatibilityWarning, StringComparison.Ordinal);
    }

    [Fact]
    public void Planner_modded_to_vanilla_keeps_strong_save_warning_and_wipe_hides_it()
    {
        var keep = ChangeServerTypePlanner.TryCreate(
            ChangeServerTypeUx.ChoiceDefaultVanilla,
            "1.21.1",
            packPath: null,
            wipeWorld: false,
            "1.21.1",
            "fabric");
        Assert.True(keep.Succeeded, keep.Error);
        Assert.NotNull(keep.Value!.SaveCompatibilityWarning);
        Assert.Contains("Vanilla", keep.Value.SaveCompatibilityWarning, StringComparison.Ordinal);
        Assert.Contains("missing from the world", keep.Value.SaveCompatibilityWarning, StringComparison.Ordinal);

        var wipe = ChangeServerTypePlanner.TryCreate(
            ChangeServerTypeUx.ChoiceDefaultVanilla,
            "1.21.1",
            packPath: null,
            wipeWorld: true,
            "1.21.1",
            "fabric");
        Assert.True(wipe.Succeeded, wipe.Error);
        Assert.Null(wipe.Value!.SaveCompatibilityWarning);
        Assert.True(wipe.Value.WipeWorld);
    }

    [Fact]
    public void Success_message_keeps_idle_note_and_play_ip()
    {
        var text = ChangeServerTypeUx.SuccessMessage(
            new ChangeServerTypeResult("paper", "1.21.8", wipedWorld: false));
        Assert.Contains("Optimized Vanilla (Paper)", text, StringComparison.Ordinal);
        Assert.Contains("1.21.8", text, StringComparison.Ordinal);
        Assert.Contains("kept", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("play IP", text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PackReplaceUx.IdleForceEnableNote, text, StringComparison.Ordinal);
    }

    [Fact]
    public void Bootstrap_change_type_reuses_prepare_and_copies_pack_only_when_modded()
    {
        var root = ProductPaths.FindProductRepoRoot();
        Assert.False(string.IsNullOrWhiteSpace(root), "product root not found");
        var path = Path.Combine(root!, "src", "McManager.Core", "Setup", "SetupBootstrapService.cs");
        Assert.True(File.Exists(path), path);
        var text = File.ReadAllText(path);
        Assert.Contains("ChangeServerTypeAsync", text, StringComparison.Ordinal);
        Assert.Contains("ReinstallMinecraft", text, StringComparison.Ordinal);
        Assert.Contains("prepare-pack-replace.sh", text, StringComparison.Ordinal);
        Assert.Contains("if (SetupServerType.IsModded(state.ServerType))", text, StringComparison.Ordinal);
        Assert.Contains("InstallModdedPack", text, StringComparison.Ordinal);
        Assert.DoesNotContain("dummy zip", text, StringComparison.OrdinalIgnoreCase);
    }
}
