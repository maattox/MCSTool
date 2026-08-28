using System.Text.Json.Serialization;
using McManager.Core.Setup;

namespace McManager.Core.Config;

/// <summary>
/// Resume snapshot for the Setup wizard. Gitignored <c>data/setup-wizard.local.json</c>.
/// Never stores Auth Tokens, SSH private keys, or tenancy secrets beyond profile/region names.
/// Step 3.3 writes tfvars/state under LocalAppData and may overwrite <c>config.local.json</c>
/// after a confirmed Deploy. It never writes repo <c>infra/terraform.tfvars</c>.
/// </summary>
public sealed class SetupWizardState
{
    /// <summary>Always Free → OCI+email → SSH → game → name/icon → EULA → Auth Token → Review. Compartment is auto-named (no page).</summary>
    public const int StepCount = 8;

    public const int StepAlwaysFree = 0;
    public const int StepOci = 1;
    public const int StepSsh = 2;
    public const int StepGame = 3;
    public const int StepIdentity = 4;
    public const int StepEula = 5;
    public const int StepAuthToken = 6;
    public const int StepSummary = 7;

    /// <summary>v1 had a Compartment page at index 2. v2 dropped it. v3 inserts Name and icon after Minecraft. v4 merges budget email into the OCI page.</summary>
    public const int CurrentSchemaVersion = 4;

    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("current_step")]
    public int CurrentStep { get; set; }

    [JsonPropertyName("always_free_confirmed")]
    public bool AlwaysFreeConfirmed { get; set; }

    [JsonPropertyName("residual_charge_disclosed")]
    public bool ResidualChargeDisclosed { get; set; }

    [JsonPropertyName("capacity_wait_consent")]
    public bool CapacityWaitConsent { get; set; }

    [JsonPropertyName("oci_profile")]
    public string OciProfile { get; set; } = "DEFAULT";

    [JsonPropertyName("oci_region")]
    public string OciRegion { get; set; } = "";

    [JsonPropertyName("create_compartment")]
    public bool CreateCompartment { get; set; } = true;

    [JsonPropertyName("compartment_name")]
    public string CompartmentName { get; set; } = "mcmgr";

    [JsonPropertyName("existing_compartment_id")]
    public string ExistingCompartmentId { get; set; } = "";

    [JsonPropertyName("alert_email")]
    public string AlertEmail { get; set; } = "";

    /// <summary><c>generate</c> or <c>import</c>.</summary>
    [JsonPropertyName("ssh_mode")]
    public string SshMode { get; set; } = "generate";

    [JsonPropertyName("ssh_public_key_path")]
    public string SshPublicKeyPath { get; set; } = "";

    [JsonPropertyName("ssh_public_key")]
    public string SshPublicKey { get; set; } = "";

    [JsonPropertyName("ssh_fingerprint")]
    public string SshFingerprint { get; set; } = "";

    [JsonPropertyName("vanilla_confirmed")]
    public bool VanillaConfirmed { get; set; }

    /// <summary>
    /// Setup branch: <c>vanilla</c> or <c>modded</c>. Missing/unknown → vanilla.
    /// </summary>
    [JsonPropertyName("server_type")]
    public string ServerType { get; set; } = SetupServerType.Vanilla;

    /// <summary>
    /// Vanilla branch: <c>default</c> (Mojang) or <c>optimized</c> (Paper).
    /// Missing/unknown values normalize to default.
    /// </summary>
    [JsonPropertyName("vanilla_flavor")]
    public string VanillaFlavor { get; set; } = SetupVanillaFlavor.Default;

    [JsonPropertyName("include_snapshots")]
    public bool IncludeSnapshots { get; set; }

    [JsonPropertyName("minecraft_version")]
    public string MinecraftVersion { get; set; } = "";

    /// <summary>Local path of the imported pack archive (Modded branch). Not a secret.</summary>
    [JsonPropertyName("pack_path")]
    public string PackPath { get; set; } = "";

    /// <summary><c>mrpack</c> or <c>manual_zip</c>.</summary>
    [JsonPropertyName("pack_kind")]
    public string PackKind { get; set; } = "";

    [JsonPropertyName("pack_name")]
    public string PackName { get; set; } = "";

    [JsonPropertyName("pack_version_id")]
    public string PackVersionId { get; set; } = "";

    [JsonPropertyName("pack_loader")]
    public string PackLoader { get; set; } = "";

    [JsonPropertyName("pack_loader_version")]
    public string PackLoaderVersion { get; set; } = "";

    [JsonPropertyName("pack_java_major")]
    public int? PackJavaMajor { get; set; }

    /// <summary>Original user-selected path before derived archive (resume rebuild).</summary>
    [JsonPropertyName("pack_source_path")]
    public string PackSourcePath { get; set; } = "";

    [JsonPropertyName("pack_summary")]
    public string PackSummary { get; set; } = "";

    [JsonPropertyName("pack_confirmed")]
    public bool PackConfirmed { get; set; }

    [JsonPropertyName("client_pack_acknowledged")]
    public bool ClientPackAcknowledged { get; set; }

    [JsonPropertyName("identity_name")]
    public string IdentityName { get; set; } = "";

    [JsonPropertyName("identity_description")]
    public string IdentityDescription { get; set; } = "";

    [JsonPropertyName("identity_name_customized")]
    public bool IdentityNameCustomized { get; set; }

    [JsonPropertyName("identity_description_customized")]
    public bool IdentityDescriptionCustomized { get; set; }

    /// <summary>Ignored. List MOTD is always name then description (legacy resume field).</summary>
    [JsonPropertyName("identity_motd_omit_name")]
    public bool IdentityMotdOmitName { get; set; }

    /// <summary>Local 64×64 PNG path for Setup seed. Not a secret. Empty = no custom icon (P8 default later).</summary>
    [JsonPropertyName("identity_icon_path")]
    public string IdentityIconPath { get; set; } = "";

    [JsonPropertyName("eula_accepted")]
    public bool EulaAccepted { get; set; }

    [JsonPropertyName("auth_token_stored")]
    public bool AuthTokenStored { get; set; }

    [JsonPropertyName("admin_cidr")]
    public string AdminCidr { get; set; } = "";

    /// <summary>VM1 A1 Flex OCPUs. Setup picker: 2 or 4. Default 4.</summary>
    [JsonPropertyName("vm1_ocpus")]
    public int Vm1Ocpus { get; set; } = Vm1ShapeChoice.DefaultOcpus;

    /// <summary>VM1 A1 Flex memory in GB. Setup picker: 12 (with 2 OCPU) or 24 (with 4). Default 24.</summary>
    [JsonPropertyName("vm1_memory_gb")]
    public int Vm1MemoryGb { get; set; } = Vm1ShapeChoice.DefaultMemoryGb;

    [JsonPropertyName("apply_stage")]
    public string ApplyStage { get; set; } = SetupApplyStage.NotStarted;

    [JsonPropertyName("function_image")]
    public string FunctionImage { get; set; } = "";

    [JsonPropertyName("updated_at")]
    public string UpdatedAt { get; set; } = "";
}
