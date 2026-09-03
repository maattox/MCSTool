using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Static (mocked) plan of what OpenTofu will create. Step 3.2 does not run <c>tofu plan</c> or apply.
/// </summary>
public static class InfraPlanSummary
{
    public static string Build(SetupWizardState state)
    {
        var name = string.IsNullOrWhiteSpace(state.CompartmentName)
            ? CompartmentNamer.BaseName
            : state.CompartmentName.Trim();
        var compartment = string.Equals(name, CompartmentNamer.BaseName, StringComparison.OrdinalIgnoreCase)
            ? $"Create compartment `{CompartmentNamer.BaseName}` (or `{CompartmentNamer.BaseName}-2` / `{CompartmentNamer.BaseName}-3` if that name is taken; tagged mcmgr-domain=mc-server-compartment)"
            : $"Create compartment `{name}` (tagged mcmgr-domain=mc-server-compartment)";

        var version = string.IsNullOrWhiteSpace(state.MinecraftVersion)
            ? "(not chosen yet)"
            : state.MinecraftVersion;
        var flavor = SetupPackImport.PlanLabel(state);

        var region = string.IsNullOrWhiteSpace(state.OciRegion) ? "(from ~/.oci)" : state.OciRegion;
        var profile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile;
        var email = string.IsNullOrWhiteSpace(state.AlertEmail) ? "(not set)" : state.AlertEmail;
        var ssh = string.IsNullOrWhiteSpace(state.SshFingerprint)
            ? (string.IsNullOrWhiteSpace(state.SshPublicKeyPath) ? "(no key yet)" : state.SshPublicKeyPath)
            : state.SshFingerprint;
        if (TofuApplyOutputs.UsesSplitDoorKey(state))
        {
            var door = string.IsNullOrWhiteSpace(state.DoorSshFingerprint)
                ? (string.IsNullOrWhiteSpace(state.DoorSshPublicKeyPath) ? "(door key set)" : state.DoorSshPublicKeyPath)
                : state.DoorSshFingerprint;
            ssh = $"game VM {ssh}; door {door}";
        }

        var shape = Vm1ShapeChoice.Format(state.Vm1Ocpus, state.Vm1MemoryGb);
        var hours = Vm1ShapeChoice.HoursHint(state.Vm1Ocpus, state.Vm1MemoryGb);
        var identityName = string.IsNullOrWhiteSpace(state.IdentityName)
            ? ServerIdentityUx.DefaultServerName(state.ServerType, state.VanillaFlavor)
            : state.IdentityName.Trim();
        var friendsLine = SetupServerType.IsModded(state.ServerType)
            ? "  Players: same exported pack required to join (vanilla Minecraft is not enough; "
              + "cannot rebuild a client pack from server mods)\n"
            : "";

        return
            "OpenTofu apply from this window creates Always Free resources (state under %LOCALAPPDATA%\\"
            + AppSettingsStore.ProductFolderName
            + "\\tofu). "
            + $"VM1 is {shape} ({hours}). "
            + "A second A1 in the same tenancy as other Ampere computers competes for Always Free hours. Deploy writes config.local.json after success.\n\n"
            + "Chosen variables\n"
            + $"  OCI profile: {profile}\n"
            + $"  Region (prefer home region): {region}\n"
            + $"  {compartment}\n"
            + $"  Budget alert email: {email}\n"
            + $"  SSH: {ssh}\n"
            + $"  Server size: {shape} ({hours})\n"
            + $"  Minecraft heap: {JvmHeapChoice.Format(state.JvmXmx)} (Xms = Xmx)\n"
            + $"  Game: {flavor} {version} (EULA {(state.EulaAccepted ? "accepted" : "not accepted")})\n"
            + $"  Server list name: {identityName}\n"
            + friendsLine
            + $"  OCIR Auth Token stored: {(state.AuthTokenStored ? $"yes (Windows Credential Manager {WindowsCredentialStore.OcirTarget})" : "no — required for the spend-brake Function")}\n\n"
            + "OpenTofu will create (on confirmed Deploy)\n"
            + $"  • Compartment {name}\n"
            + "  • VCN mcmgr-vcn (10.0.0.0/16), IGW, public route table\n"
            + "  • Public subnet mcmgr-subnet-public + Security List mcmgr-sl\n"
            + $"  • VM1 mcmgr-vm1 — VM.Standard.A1.Flex {shape}\n"
            + "  • Door mcmgr-door — VM.Standard.E2.1.Micro\n"
            + "  • Reserved play IP mcmgr-play-ip + secondary VNICS (mcmgr-vm1-play, mcmgr-door-play)\n"
            + "  • Object Storage bucket mcmgr-shared-data\n"
            + "  • Dynamic groups mcmgr-dg-instances, mcmgr-dg-door, mcmgr-dg-fn + policy mcmgr-stack\n"
            + "  • $1 budget mcmgr-budget-1usd + email alert\n"
            + "  • Functions application mcmgr-fn-app + OCIR repo mcmgr-fn/softstop\n"
            + "  • Function + Events after a pre-built ARM image is copied (Auth Token; Docker only if no artifact)\n\n"
            + "Not in this apply: writing repo infra/terraform.tfvars (Setup uses LocalAppData). Function/Events need Auth Token after first apply; a pre-built ARM tarball next to the app or in artifacts/ means Docker is not required.";
    }
}
