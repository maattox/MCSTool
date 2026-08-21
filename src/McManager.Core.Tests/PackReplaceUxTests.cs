using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackReplaceUxTests
{
    [Fact]
    public void Pick_and_install_require_running_vm1()
    {
        Assert.False(PackReplaceUx.CanPick(vm1Running: false, busy: false));
        Assert.Equal(PackReplaceUx.StartFirstMessage, PackReplaceUx.PickDisabledReason(false, false));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: false, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Equal(
            PackReplaceUx.StartFirstMessage,
            PackReplaceUx.InstallDisabledReason(false, false, true, true, true));
    }

    [Fact]
    public void Install_requires_both_setup_checkboxes()
    {
        Assert.True(PackReplaceUx.CanPick(vm1Running: true, busy: false));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: false, clientPackAcknowledged: true));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: false));
        Assert.True(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Contains(
            "Confirm the pack",
            PackReplaceUx.InstallDisabledReason(true, false, true, false, true),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Busy_blocks_pick_and_install()
    {
        Assert.False(PackReplaceUx.CanPick(vm1Running: true, busy: true));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true, busy: true, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Contains("Wait", PackReplaceUx.PickDisabledReason(true, true), StringComparison.Ordinal);
    }

    [Fact]
    public void Confirm_copy_differs_for_wipe_vs_keep()
    {
        Assert.Contains("world is kept", PackReplaceUx.ConfirmBody(wipeWorld: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("world will be deleted", PackReplaceUx.ConfirmBody(wipeWorld: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reinstalls Minecraft", PackReplaceUx.ConfirmBody(false), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PackReplaceUx.ConfirmKeepWorld, PackReplaceUx.ConfirmBody(false));
        Assert.Equal(PackReplaceUx.ConfirmWipeWorld, PackReplaceUx.ConfirmBody(true));
    }

    [Fact]
    public void Save_compat_warning_hidden_when_wiping()
    {
        var warning = PackReplaceSaveCompatibility.Warn("1.21.1", "fabric", "1.20.1", "fabric");
        Assert.NotNull(warning);
        Assert.Equal(warning, PackReplaceUx.VisibleSaveCompatibilityWarning(wipeWorld: false, warning));
        Assert.Null(PackReplaceUx.VisibleSaveCompatibilityWarning(wipeWorld: true, warning));
        Assert.Null(PackReplaceUx.VisibleSaveCompatibilityWarning(wipeWorld: false, "  "));
    }

    [Fact]
    public void Analyze_fixture_can_continue_and_meta_kind_is_modded()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "packs", "fabric-mistag.mrpack");
        Assert.True(File.Exists(path), path);
        var analysis = SetupPackImport.AnalyzeFile(path);
        Assert.True(analysis.Succeeded, analysis.Error);
        var preview = analysis.Value!;
        Assert.True(preview.CanContinue);
        Assert.Equal(SetupPackImport.KindMrpack, preview.Kind);

        var plan = PackReplacePlanner.TryCreate(path, wipeWorld: false, "1.21.1", "forge");
        Assert.True(plan.Succeeded, plan.Error);
        Assert.NotNull(plan.Value!.SaveCompatibilityWarning);
        Assert.Null(PackReplaceUx.VisibleSaveCompatibilityWarning(wipeWorld: true, plan.Value.SaveCompatibilityWarning));

        Assert.Equal(SetupServerType.Modded, PackReplaceUx.ServerKindForMeta(preview.Loader));
        Assert.Equal(SetupServerType.Modded, PackReplaceUx.ServerKindForMeta("fabric"));
        Assert.Equal("", PackReplaceUx.ServerKindForMeta(""));
    }

    [Fact]
    public void Success_message_notes_idle_and_world()
    {
        var kept = PackReplaceUx.SuccessMessage(
            new PackReplaceResult("CI Fabric Strip Fixture", "1.21.1", "fabric", wipedWorld: false, "compat"));
        Assert.Contains("world was kept", kept, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(PackReplaceUx.IdleForceEnableNote, kept, StringComparison.Ordinal);
        Assert.Contains("compat", kept, StringComparison.Ordinal);

        var wiped = PackReplaceUx.SuccessMessage(
            new PackReplaceResult("CI Fabric Strip Fixture", "1.21.1", "fabric", wipedWorld: true, null));
        Assert.Contains("world was wiped", wiped, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("compat", wiped, StringComparison.Ordinal);
    }

    [Fact]
    public void Help_title_mentions_change_pack()
    {
        Assert.Contains("Change pack", ModdingPanelLogic.HelpTitle, StringComparison.Ordinal);
        Assert.Contains("world is kept", ModdingPanelLogic.HelpTitle, StringComparison.OrdinalIgnoreCase);
    }
}
