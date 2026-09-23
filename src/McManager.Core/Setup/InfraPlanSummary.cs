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
            ? $"A compartment named {CompartmentNamer.BaseName} ({CompartmentNamer.BaseName}-2 or {CompartmentNamer.BaseName}-3 if that name is taken)"
            : $"A compartment named {name}";

        var version = string.IsNullOrWhiteSpace(state.MinecraftVersion)
            ? "(not chosen yet)"
            : state.MinecraftVersion;
        var flavor = SetupPackImport.PlanLabel(state);

        var region = string.IsNullOrWhiteSpace(state.OciRegion) ? "(from ~/.oci/config)" : state.OciRegion;
        var profile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile;
        var email = string.IsNullOrWhiteSpace(state.AlertEmail) ? "(not set)" : state.AlertEmail;
        var ssh = string.IsNullOrWhiteSpace(state.SshFingerprint)
            ? (string.IsNullOrWhiteSpace(state.SshPublicKeyPath) ? "(no key yet)" : state.SshPublicKeyPath)
            : state.SshFingerprint;
        if (TofuApplyOutputs.UsesSplitDoorKey(state))
        {
            var door = string.IsNullOrWhiteSpace(state.DoorSshFingerprint)
                ? (string.IsNullOrWhiteSpace(state.DoorSshPublicKeyPath) ? "(doorbell VM key set)" : state.DoorSshPublicKeyPath)
                : state.DoorSshFingerprint;
            ssh = $"game VM {ssh}; doorbell VM {door}";
        }

        var shape = Vm1ShapeChoice.Format(state.Vm1Ocpus, state.Vm1MemoryGb);
        var identityName = string.IsNullOrWhiteSpace(state.IdentityName)
            ? ServerIdentityUx.DefaultServerName(state.ServerType, state.VanillaFlavor)
            : state.IdentityName.Trim();
        return
            $"  • {compartment}\n"
            + $"  • Game VM, {shape}\n"
            + "  • Doorbell VM\n"
            + "  • Play IP\n"
            + "  • Network and firewall (whitelisted IPs only)\n"
            + "  • Cloud storage\n"
            + $"  • $1 budget alert to {email}\n"
            + "  • $1 spending limit\n\n"
            + "Your choices\n"
            + $"  OCI profile: {profile}\n"
            + $"  Region: {region}\n"
            + $"  Server type: {flavor} {version} (EULA {(state.EulaAccepted ? "accepted" : "not accepted")})\n"
            + $"  Server memory: {JvmHeapChoice.Format(JvmHeapChoice.ClampToHost(state.JvmXmx, state.Vm1MemoryGb))}\n"
            + $"  SSH keys: {ssh}\n"
            + $"  Auth Token saved: {(state.AuthTokenStored ? "yes" : "no — needed for the $1 spending limit")}\n"
            + $"  Server list name: {identityName}\n\n"
            + "Another Ampere VM in the same Oracle account shares the same free hours.";
    }
}
