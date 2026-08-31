using System.Text.Json;
using System.Text.Json.Nodes;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>Parsed <c>tofu output -json</c> values needed for local config + meta.</summary>
public sealed class TofuApplyOutputs
{
    public string CompartmentId { get; init; } = "";
    public string TenancyId { get; init; } = "";
    public string Region { get; init; } = "";
    public string VcnId { get; init; } = "";
    public string SubnetId { get; init; } = "";
    public string SecurityListId { get; init; } = "";
    public string Vm1InstanceId { get; init; } = "";
    public string Vm1DisplayName { get; init; } = "mcmgr-vm1";
    public string Vm1Shape { get; init; } = "VM.Standard.A1.Flex";
    public double Vm1Ocpus { get; init; } = 4;
    public double Vm1MemoryGb { get; init; } = 24;
    public string Vm1PrimaryPrivateIp { get; init; } = "";
    public string Vm1SecondaryPrivateIp { get; init; } = "";
    public string Vm1SecondaryPrivateIpId { get; init; } = "";
    public string Vm1SshHost { get; init; } = "";
    public string DoorInstanceId { get; init; } = "";
    public string DoorDisplayName { get; init; } = "mcmgr-door";
    public string DoorPrimaryPrivateIp { get; init; } = "";
    public string DoorSecondaryPrivateIp { get; init; } = "";
    public string DoorSecondaryPrivateIpId { get; init; } = "";
    public string DoorSshHost { get; init; } = "";
    public int DoorHttpPort { get; init; } = 8080;
    public string PlayReservedPublicIp { get; init; } = "";
    public string PlayReservedPublicIpId { get; init; } = "";
    public string ObjectStorageNamespace { get; init; } = "";
    public string ObjectStorageBucket { get; init; } = "";
    public string ObjectStorageBucketId { get; init; } = "";
    public string WorldPath { get; init; } = "/opt/mcmgr/server/world";
    public string MinecraftUnit { get; init; } = "minecraft";
    public string SshUser { get; init; } = "ubuntu";
    public string? FunctionId { get; init; }

    public const string CannedDryRunJson = """
        {
          "compartment_id": { "value": "ocid1.compartment.oc1..dry-run" },
          "tenancy_id": { "value": "ocid1.tenancy.oc1..dry-run" },
          "region": { "value": "us-sanjose-1" },
          "vcn_id": { "value": "ocid1.vcn.oc1.us-sanjose-1.dry-run" },
          "subnet_id": { "value": "ocid1.subnet.oc1.us-sanjose-1.dry-run" },
          "security_list_id": { "value": "ocid1.securitylist.oc1.us-sanjose-1.dry-run" },
          "vm1_instance_id": { "value": "ocid1.instance.oc1.us-sanjose-1.dry-run-vm1" },
          "vm1_display_name": { "value": "mcmgr-vm1" },
          "vm1_shape": { "value": "VM.Standard.A1.Flex" },
          "vm1_ocpus": { "value": 4 },
          "vm1_memory_gb": { "value": 24 },
          "vm1_primary_private_ip": { "value": "10.0.0.10" },
          "vm1_secondary_private_ip": { "value": "10.0.0.11" },
          "vm1_secondary_private_ip_id": { "value": "ocid1.privateip.oc1.us-sanjose-1.dry-run-vm1s" },
          "vm1_ssh_host": { "value": "203.0.113.10" },
          "door_instance_id": { "value": "ocid1.instance.oc1.us-sanjose-1.dry-run-door" },
          "door_display_name": { "value": "mcmgr-door" },
          "door_primary_private_ip": { "value": "10.0.0.20" },
          "door_secondary_private_ip": { "value": "10.0.0.21" },
          "door_secondary_private_ip_id": { "value": "ocid1.privateip.oc1.us-sanjose-1.dry-run-doors" },
          "door_ssh_host": { "value": "203.0.113.20" },
          "door_http_port": { "value": 8080 },
          "play_reserved_public_ip": { "value": "203.0.113.30" },
          "play_reserved_public_ip_id": { "value": "ocid1.publicip.oc1.us-sanjose-1.dry-run-play" },
          "object_storage_namespace": { "value": "dryrunns" },
          "object_storage_bucket": { "value": "mcmgr-shared-data" },
          "object_storage_bucket_id": { "value": "ocid1.bucket.oc1.us-sanjose-1.dry-run" },
          "world_path": { "value": "/opt/mcmgr/server/world" },
          "minecraft_unit": { "value": "minecraft" },
          "ssh_user": { "value": "ubuntu" },
          "function_id": { "value": null }
        }
        """;

    public static ServiceResult<TofuApplyOutputs> Parse(string json)
    {
        try
        {
            var root = JsonNode.Parse(json) as JsonObject
                       ?? throw new JsonException("tofu output root is not an object.");
            return ServiceResult<TofuApplyOutputs>.Ok(new TofuApplyOutputs
            {
                CompartmentId = Str(root, "compartment_id"),
                TenancyId = Str(root, "tenancy_id"),
                Region = Str(root, "region"),
                VcnId = Str(root, "vcn_id"),
                SubnetId = Str(root, "subnet_id"),
                SecurityListId = Str(root, "security_list_id"),
                Vm1InstanceId = Str(root, "vm1_instance_id"),
                Vm1DisplayName = Str(root, "vm1_display_name", "mcmgr-vm1"),
                Vm1Shape = Str(root, "vm1_shape", "VM.Standard.A1.Flex"),
                Vm1Ocpus = Num(root, "vm1_ocpus", 4),
                Vm1MemoryGb = Num(root, "vm1_memory_gb", 24),
                Vm1PrimaryPrivateIp = Str(root, "vm1_primary_private_ip"),
                Vm1SecondaryPrivateIp = Str(root, "vm1_secondary_private_ip"),
                Vm1SecondaryPrivateIpId = Str(root, "vm1_secondary_private_ip_id"),
                Vm1SshHost = Str(root, "vm1_ssh_host"),
                DoorInstanceId = Str(root, "door_instance_id"),
                DoorDisplayName = Str(root, "door_display_name", "mcmgr-door"),
                DoorPrimaryPrivateIp = Str(root, "door_primary_private_ip"),
                DoorSecondaryPrivateIp = Str(root, "door_secondary_private_ip"),
                DoorSecondaryPrivateIpId = Str(root, "door_secondary_private_ip_id"),
                DoorSshHost = Str(root, "door_ssh_host"),
                DoorHttpPort = (int)Num(root, "door_http_port", 8080),
                PlayReservedPublicIp = Str(root, "play_reserved_public_ip"),
                PlayReservedPublicIpId = Str(root, "play_reserved_public_ip_id"),
                ObjectStorageNamespace = Str(root, "object_storage_namespace"),
                ObjectStorageBucket = Str(root, "object_storage_bucket"),
                ObjectStorageBucketId = Str(root, "object_storage_bucket_id"),
                WorldPath = Str(root, "world_path", "/opt/mcmgr/server/world"),
                MinecraftUnit = Str(root, "minecraft_unit", "minecraft"),
                SshUser = Str(root, "ssh_user", "ubuntu"),
                FunctionId = NullOrStr(root, "function_id"),
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<TofuApplyOutputs>.Fail($"Failed to parse tofu outputs: {ex.Message}");
        }
    }

    public ManagerLocalConfig ToLocalConfig(SetupWizardState state, string rconPassword)
    {
        var vm1Key = PrivateKeyPath(state);
        var doorKey = DoorPrivateKeyPath(state);
        var ociConfig = "%USERPROFILE%\\.oci\\config";
        return new ManagerLocalConfig
        {
            SchemaVersion = 1,
            AdminName = "admin",
            Oci = new OciSettings
            {
                ConfigFile = ociConfig,
                Profile = string.IsNullOrWhiteSpace(state.OciProfile) ? "DEFAULT" : state.OciProfile,
                Region = string.IsNullOrWhiteSpace(Region) ? state.OciRegion : Region,
                CompartmentId = CompartmentId,
                TenancyId = TenancyId,
            },
            Network = new NetworkSettings
            {
                VcnId = VcnId,
                SubnetId = SubnetId,
                SecurityListId = SecurityListId,
            },
            Vm1 = new Vm1Settings
            {
                InstanceId = Vm1InstanceId,
                DisplayName = Vm1DisplayName,
                Shape = Vm1Shape,
                ShapeOcpus = Vm1Ocpus,
                ShapeMemoryGb = Vm1MemoryGb,
                PrimaryPrivateIp = Vm1PrimaryPrivateIp,
                SecondaryPrivateIp = Vm1SecondaryPrivateIp,
                SecondaryPrivateIpId = Vm1SecondaryPrivateIpId,
                SshHost = Vm1SshHost,
                SshUser = SshUser,
                SshKeyPath = vm1Key,
                WorldPath = WorldPath,
                MinecraftUnit = MinecraftUnit,
            },
            Door = new DoorSettings
            {
                InstanceId = DoorInstanceId,
                DisplayName = DoorDisplayName,
                PrimaryPrivateIp = DoorPrimaryPrivateIp,
                SecondaryPrivateIp = DoorSecondaryPrivateIp,
                SecondaryPrivateIpId = DoorSecondaryPrivateIpId,
                SshHost = DoorSshHost,
                SshUser = SshUser,
                SshKeyPath = doorKey,
                HttpPort = DoorHttpPort,
            },
            Play = new PlaySettings
            {
                ReservedPublicIp = PlayReservedPublicIp,
                ReservedPublicIpId = PlayReservedPublicIpId,
            },
            ObjectStorage = new ObjectStorageSettings
            {
                Namespace = ObjectStorageNamespace,
                Bucket = ObjectStorageBucket,
                BucketId = ObjectStorageBucketId,
            },
            Rcon = new RconSettings { Password = rconPassword },
        };
    }

    /// <summary>Local private-key path for the game VM (VM1).</summary>
    public static string PrivateKeyPath(SetupWizardState state) =>
        DerivePrivateKeyPath(state.SshPublicKeyPath);

    /// <summary>
    /// Local private-key path for the door. Same as <see cref="PrivateKeyPath"/> unless
    /// Setup split a second door public key.
    /// </summary>
    public static string DoorPrivateKeyPath(SetupWizardState state) =>
        UsesSplitDoorKey(state)
            ? DerivePrivateKeyPath(state.DoorSshPublicKeyPath)
            : PrivateKeyPath(state);

    /// <summary>Public key line tofu installs on the door. Falls back to the game-VM key.</summary>
    public static string DoorPublicKeyLine(SetupWizardState state) =>
        UsesSplitDoorKey(state)
            ? state.DoorSshPublicKey.Trim()
            : (state.SshPublicKey ?? "").Trim();

    public static bool UsesSplitDoorKey(SetupWizardState state) =>
        state.SshSplitDoorKey && SshKeyHelper.LooksLikePublicKey(state.DoorSshPublicKey ?? "");

    public static string DerivePrivateKeyPath(string? publicKeyPath)
    {
        var pub = publicKeyPath?.Trim() ?? "";
        if (pub.EndsWith(".pub", StringComparison.OrdinalIgnoreCase))
            return pub[..^4];
        return SshKeyHelper.DefaultPrivateKeyPath();
    }

    private static string Str(JsonObject root, string name, string fallback = "")
    {
        if (root[name] is JsonObject wrap && wrap["value"] is JsonValue v)
            return v.ToString() ?? fallback;
        return fallback;
    }

    private static string? NullOrStr(JsonObject root, string name)
    {
        if (root[name] is JsonObject wrap)
        {
            if (wrap["value"] is null || wrap["value"] is JsonValue jv && jv.GetValueKind() == JsonValueKind.Null)
                return null;
            return wrap["value"]?.ToString();
        }

        return null;
    }

    private static double Num(JsonObject root, string name, double fallback)
    {
        if (root[name] is JsonObject wrap && wrap["value"] is JsonValue v)
        {
            if (v.TryGetValue<double>(out var d))
                return d;
            if (double.TryParse(v.ToString(), out d))
                return d;
        }

        return fallback;
    }
}
