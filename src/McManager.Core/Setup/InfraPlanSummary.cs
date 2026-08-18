using McManager.Core.Config;

namespace McManager.Core.Setup;

/// <summary>
/// Static (mocked) plan of what OpenTofu will create. Step 3.2 does not run <c>tofu plan</c> or apply.
/// </summary>
public static class InfraPlanSummary
{
    public static string Build(SetupWizardState state)
    {
        var compartment = state.CreateCompartment
            ? $"Create compartment `{state.CompartmentName}` (tagged mcmgr-domain=mc-server-compartment)"
            : $"Use existing compartment `{state.ExistingCompartmentId}` (must be empty / disposable)";

        var version = string.IsNullOrWhiteSpace(state.MinecraftVersion)
            ? "(not chosen yet)"
            : state.MinecraftVersion;
        var flavor = SetupVanillaFlavor.PlanLabel(state.VanillaFlavor);

        var region = string.IsNullOrWhiteSpace(state.OciRegion) ? "(from ~/.oci)" : state.OciRegion;
        var profile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile;
        var email = string.IsNullOrWhiteSpace(state.AlertEmail) ? "(not set)" : state.AlertEmail;
        var ssh = string.IsNullOrWhiteSpace(state.SshFingerprint)
            ? (string.IsNullOrWhiteSpace(state.SshPublicKeyPath) ? "(no key yet)" : state.SshPublicKeyPath)
            : state.SshFingerprint;

        var shape = Vm1ShapeChoice.Format(state.Vm1Ocpus, state.Vm1MemoryGb);
        var hours = Vm1ShapeChoice.HoursHint(state.Vm1Ocpus, state.Vm1MemoryGb);

        return
            "OpenTofu apply from this window creates Always Free resources (state under %LOCALAPPDATA%\\McManager\\tofu). "
            + $"VM1 is {shape} ({hours}). "
            + "A second A1 in the same tenancy as an existing lab stack competes for Always Free hours. Deploy writes config.local.json after success.\n\n"
            + "Chosen variables\n"
            + $"  OCI profile: {profile}\n"
            + $"  Region (prefer home region): {region}\n"
            + $"  {compartment}\n"
            + $"  Budget alert email: {email}\n"
            + $"  SSH: {ssh}\n"
            + $"  Game computer size: {shape} ({hours})\n"
            + $"  Game: {flavor} {version} (EULA {(state.EulaAccepted ? "accepted" : "not accepted")})\n"
            + $"  OCIR Auth Token stored: {(state.AuthTokenStored ? "yes (Windows Credential Manager McManager/ocir)" : "no — optional until Function push")}\n\n"
            + "OpenTofu will create (on confirmed Deploy)\n"
            + "  • Compartment mcmgr (unless using an existing OCID)\n"
            + "  • VCN mcmgr-vcn (10.0.0.0/16), IGW, public route table\n"
            + "  • Public subnet mcmgr-subnet-public + Security List mcmgr-sl\n"
            + $"  • VM1 mcmgr-vm1 — VM.Standard.A1.Flex {shape}\n"
            + "  • Door mcmgr-door — VM.Standard.E2.1.Micro\n"
            + "  • Reserved play IP mcmgr-play-ip + secondary VNICS (mcmgr-vm1-play, mcmgr-door-play)\n"
            + "  • Object Storage bucket mcmgr-shared-data\n"
            + "  • Dynamic groups mcmgr-dg-instances, mcmgr-dg-door, mcmgr-dg-fn + policy mcmgr-stack\n"
            + "  • $1 budget mcmgr-budget-1usd + email alert\n"
            + "  • Functions application mcmgr-fn-app + OCIR repo mcmgr-fn/softstop\n"
            + "  • Function + Events rule skipped until an image is pushed (Step 3.3)\n\n"
            + "Not in this apply: writing repo infra/terraform.tfvars (Setup uses LocalAppData). Function/Events need Docker + Auth Token after first apply.";
    }
}
