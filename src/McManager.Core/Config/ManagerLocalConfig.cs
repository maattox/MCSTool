using System.Text.Json.Serialization;

namespace McManager.Core.Config;

public sealed class ManagerLocalConfig
{
    [JsonPropertyName("schema_version")]
    public int SchemaVersion { get; init; } = 1;

    [JsonPropertyName("oci")]
    public OciSettings Oci { get; init; } = new();

    [JsonPropertyName("network")]
    public NetworkSettings Network { get; init; } = new();

    [JsonPropertyName("vm1")]
    public Vm1Settings Vm1 { get; init; } = new();

    [JsonPropertyName("door")]
    public DoorSettings Door { get; init; } = new();

    [JsonPropertyName("play")]
    public PlaySettings Play { get; init; } = new();

    [JsonPropertyName("object_storage")]
    public ObjectStorageSettings ObjectStorage { get; init; } = new();

    [JsonPropertyName("budget")]
    public BudgetSettings Budget { get; init; } = new();

    [JsonPropertyName("rcon")]
    public RconSettings Rcon { get; init; } = new();

    [JsonPropertyName("admin_name")]
    public string AdminName { get; init; } = "";

    [JsonIgnore]
    public string DoorAdminBaseUrl =>
        $"http://{Door.SshHost}:{Door.HttpPort}";
}

public sealed class OciSettings
{
    [JsonPropertyName("config_file")]
    public string ConfigFile { get; init; } = "";

    [JsonPropertyName("profile")]
    public string Profile { get; init; } = "DEFAULT";

    [JsonPropertyName("region")]
    public string Region { get; init; } = "";

    [JsonPropertyName("compartment_id")]
    public string CompartmentId { get; init; } = "";

    [JsonPropertyName("tenancy_id")]
    public string TenancyId { get; init; } = "";
}

public sealed class NetworkSettings
{
    [JsonPropertyName("vcn_id")]
    public string VcnId { get; init; } = "";

    [JsonPropertyName("subnet_id")]
    public string SubnetId { get; init; } = "";

    [JsonPropertyName("security_list_id")]
    public string SecurityListId { get; init; } = "";

    [JsonPropertyName("minecraft_port")]
    public int MinecraftPort { get; init; } = 25565;

    [JsonPropertyName("ssh_port")]
    public int SshPort { get; init; } = 22;

    [JsonPropertyName("firewalld_zone")]
    public string FirewalldZone { get; init; } = "public";
}

public sealed class Vm1Settings
{
    [JsonPropertyName("instance_id")]
    public string InstanceId { get; init; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("shape")]
    public string Shape { get; init; } = "";

    [JsonPropertyName("shape_ocpus")]
    public double ShapeOcpus { get; init; }

    [JsonPropertyName("shape_memory_gb")]
    public double ShapeMemoryGb { get; init; }

    [JsonPropertyName("primary_private_ip")]
    public string PrimaryPrivateIp { get; init; } = "";

    [JsonPropertyName("secondary_private_ip")]
    public string SecondaryPrivateIp { get; init; } = "";

    [JsonPropertyName("secondary_private_ip_id")]
    public string SecondaryPrivateIpId { get; init; } = "";

    [JsonPropertyName("ssh_host")]
    public string SshHost { get; init; } = "";

    [JsonPropertyName("ssh_user")]
    public string SshUser { get; init; } = "ubuntu";

    [JsonPropertyName("ssh_key_path")]
    public string SshKeyPath { get; init; } = "";

    [JsonPropertyName("world_path")]
    public string WorldPath { get; init; } = "";

    [JsonPropertyName("minecraft_unit")]
    public string MinecraftUnit { get; init; } = "minecraft";
}

public sealed class DoorSettings
{
    [JsonPropertyName("instance_id")]
    public string InstanceId { get; init; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; init; } = "";

    [JsonPropertyName("primary_private_ip")]
    public string PrimaryPrivateIp { get; init; } = "";

    [JsonPropertyName("secondary_private_ip")]
    public string SecondaryPrivateIp { get; init; } = "";

    [JsonPropertyName("secondary_private_ip_id")]
    public string SecondaryPrivateIpId { get; init; } = "";

    [JsonPropertyName("ssh_host")]
    public string SshHost { get; init; } = "";

    [JsonPropertyName("ssh_user")]
    public string SshUser { get; init; } = "ubuntu";

    [JsonPropertyName("ssh_key_path")]
    public string SshKeyPath { get; init; } = "";

    [JsonPropertyName("http_port")]
    public int HttpPort { get; init; } = 8080;
}

public sealed class PlaySettings
{
    [JsonPropertyName("reserved_public_ip")]
    public string ReservedPublicIp { get; init; } = "";

    [JsonPropertyName("reserved_public_ip_id")]
    public string ReservedPublicIpId { get; init; } = "";
}

public sealed class ObjectStorageSettings
{
    [JsonPropertyName("namespace")]
    public string Namespace { get; init; } = "";

    [JsonPropertyName("bucket")]
    public string Bucket { get; init; } = "";

    [JsonPropertyName("bucket_id")]
    public string BucketId { get; init; } = "";

    [JsonPropertyName("soft_cap_gb")]
    public double SoftCapGb { get; init; } = 9.5;

    [JsonPropertyName("backup_enabled")]
    public bool BackupEnabled { get; init; } = true;

    [JsonPropertyName("prefixes")]
    public ObjectStoragePrefixes Prefixes { get; init; } = new();
}

public sealed class ObjectStoragePrefixes
{
    [JsonPropertyName("meta")]
    public string Meta { get; init; } = "meta/";

    [JsonPropertyName("ledger")]
    public string Ledger { get; init; } = "ledger/";

    [JsonPropertyName("budget")]
    public string Budget { get; init; } = "budget/";

    [JsonPropertyName("ip")]
    public string Ip { get; init; } = "ip/";

    [JsonPropertyName("messages")]
    public string Messages { get; init; } = "messages/";

    [JsonPropertyName("backups")]
    public string Backups { get; init; } = "backups/";
}

public sealed class BudgetSettings
{
    [JsonPropertyName("monthly_ocpu_target")]
    public double MonthlyOcpuTarget { get; init; } = 1400;

    [JsonPropertyName("monthly_gb_target")]
    public double MonthlyGbTarget { get; init; } = 8800;

    [JsonPropertyName("soft_ocpu_cap")]
    public double SoftOcpuCap { get; init; } = 1375;

    [JsonPropertyName("soft_gb_cap")]
    public double SoftGbCap { get; init; } = 8600;

    [JsonPropertyName("idle_timeout_minutes")]
    public int IdleTimeoutMinutes { get; init; } = 15;

    [JsonPropertyName("budget_warn_minutes")]
    public int BudgetWarnMinutes { get; init; } = 5;

    [JsonPropertyName("idle_agent_enabled")]
    public bool IdleAgentEnabled { get; init; } = true;
}

public sealed class RconSettings
{
    [JsonPropertyName("port")]
    public int Port { get; init; } = 25575;

    [JsonPropertyName("password")]
    public string Password { get; init; } = "";
}
