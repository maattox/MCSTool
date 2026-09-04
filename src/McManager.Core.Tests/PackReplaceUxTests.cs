using McManager.Core.Services;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class PackReplaceUxTests
{
    [Fact]
    public void Pick_and_install_work_while_vm1_stopped()
    {
        Assert.True(PackReplaceUx.CanPick(vm1Running: false, busy: false));
        Assert.Equal("", PackReplaceUx.PickDisabledReason(false, false));
        Assert.True(PackReplaceUx.CanInstall(
            vm1Running: false, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Equal("", PackReplaceUx.InstallDisabledReason(false, false, true, true, true));
    }

    [Fact]
    public void Install_requires_pack_confirm()
    {
        Assert.True(PackReplaceUx.CanPick(vm1Running: true, busy: false));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: false, clientPackAcknowledged: true));
        Assert.True(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: false));
        Assert.True(PackReplaceUx.CanInstall(
            vm1Running: true, busy: false, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Contains(
            "Confirm the pack",
            PackReplaceUx.InstallDisabledReason(true, false, true, false, true),
            StringComparison.Ordinal);
        Assert.Equal(
            "",
            PackReplaceUx.InstallDisabledReason(true, false, true, true, false));
    }

    [Fact]
    public void Install_requires_identity_when_incomplete()
    {
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true,
            busy: false,
            canContinue: true,
            packConfirmed: true,
            clientPackAcknowledged: true,
            identityComplete: false));
        Assert.Equal(
            DerivedPackIdentity.IdentityIncompleteReason,
            PackReplaceUx.InstallDisabledReason(true, false, true, true, true, identityComplete: false));
    }

    [Fact]
    public void Busy_blocks_pick_and_install()
    {
        Assert.False(PackReplaceUx.CanPick(vm1Running: true, busy: true));
        Assert.False(PackReplaceUx.CanPick(vm1Running: false, busy: true));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true, busy: true, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: false, busy: true, canContinue: true, packConfirmed: true, clientPackAcknowledged: true));
        Assert.Contains("Wait", PackReplaceUx.PickDisabledReason(true, true), StringComparison.Ordinal);
    }

    [Fact]
    public void Install_blocks_when_freeze_is_violated()
    {
        const string reason = "Cannot skip cofh_core.jar: required by thermal.jar.";
        Assert.False(PackReplaceUx.CanInstall(
            vm1Running: true,
            busy: false,
            canContinue: true,
            packConfirmed: true,
            clientPackAcknowledged: true,
            freezeBlockReason: reason));
        Assert.Equal(
            reason,
            PackReplaceUx.InstallDisabledReason(
                true, false, true, true, true, freezeBlockReason: reason));
        Assert.True(PackReplaceUx.FreezeAllowsContinue(null));
        Assert.False(PackReplaceUx.FreezeAllowsContinue(reason));
    }

    [Fact]
    public void Confirm_copy_differs_for_wipe_vs_keep()
    {
        Assert.Contains("world is kept", PackReplaceUx.ConfirmBody(wipeWorld: false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("world will be deleted", PackReplaceUx.ConfirmBody(wipeWorld: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("started first", PackReplaceUx.ConfirmBody(false), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("started first", PackReplaceUx.ConfirmBody(true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reinstalls Minecraft", PackReplaceUx.ConfirmBody(false), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(PackReplaceUx.ConfirmKeepWorld, PackReplaceUx.ConfirmBody(false));
        Assert.Equal(PackReplaceUx.ConfirmWipeWorld, PackReplaceUx.ConfirmBody(true));
    }

    [Fact]
    public void Change_pack_copy_is_locked_and_pronoun_free()
    {
        Assert.Equal("Drop a mod pack here", PackReplaceUx.DropTitle);
        Assert.Contains("Modrinth .mrpack", PackReplaceUx.DropFormats, StringComparison.Ordinal);
        Assert.Contains("CurseForge Server Pack .zip", PackReplaceUx.DropFormats, StringComparison.Ordinal);
        Assert.Contains(".jar zip", PackReplaceUx.DropFormats, StringComparison.Ordinal);
        Assert.Equal(
            "Known client-only mods will automatically be skipped. Check the list below and confirm that all client-only mods are correctly marked.",
            PackReplaceUx.SkipWarningBody);
        Assert.False(PackReplaceUx.ShouldShowSkipListWarning(assistedReviewVisible: false));
        Assert.True(PackReplaceUx.ShouldShowSkipListWarning(assistedReviewVisible: true));
        Assert.Contains("irreversible", PackReplaceUx.WipeWorldLabel, StringComparison.OrdinalIgnoreCase);

        var paneCopy = string.Join(
            " ",
            PackReplaceUx.DropTitle,
            PackReplaceUx.DropFormats,
            PackReplaceUx.DropLargeHint,
            PackReplaceUx.SkipWarningBody,
            PackReplaceUx.ChangePackPickHint,
            PackReplaceUx.PackConfirmLabel,
            PackReplaceUx.WipeWorldLabel,
            PackReplaceUx.ConfirmKeepWorld,
            PackReplaceUx.ConfirmWipeWorld);
        Assert.DoesNotContain(" you ", " " + paneCopy + " ", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" we ", " " + paneCopy + " ", StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(" your ", " " + paneCopy + " ", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Skip_list_warning_only_when_assisted_review_is_visible()
    {
        Assert.False(PackReplaceUx.ShouldShowSkipListWarning(assistedReviewVisible: false));
        Assert.True(PackReplaceUx.ShouldShowSkipListWarning(assistedReviewVisible: true));
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
