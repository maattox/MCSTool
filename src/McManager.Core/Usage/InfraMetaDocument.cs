using System.Text.Json.Serialization;
using McManager.Core.Config;

namespace McManager.Core.Usage;

/// <summary>Object Storage <c>meta/infra.json</c> canonical nested document (v2).</summary>
public sealed class InfraMetaDocument
{
    public const int DocumentVersion = 2;
    public const int InfraSchema = 2;
    public const string DefaultStackVersion = "0.1.0";
    public const string DefaultStackName = "mcmgr";
    public const string ModeAlwaysFree = "always_free";

    [JsonPropertyName("version")]
    public int Version { get; set; } = DocumentVersion;

    [JsonPropertyName("infra_schema")]
    public int InfraSchemaValue { get; set; } = InfraSchema;

    [JsonPropertyName("stack_version")]
    public string StackVersion { get; set; } = DefaultStackVersion;

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("stack_name")]
    public string StackName { get; set; } = DefaultStackName;

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = ModeAlwaysFree;

    [JsonPropertyName("region")]
    public string Region { get; set; } = "";

    [JsonPropertyName("tenancy_id")]
    public string TenancyId { get; set; } = "";

    [JsonPropertyName("compartment_id")]
    public string CompartmentId { get; set; } = "";

    [JsonPropertyName("play")]
    public InfraPlaySection Play { get; set; } = new();

    [JsonPropertyName("game")]
    public InfraGameSection Game { get; set; } = new();

    [JsonPropertyName("network")]
    public InfraNetworkSection Network { get; set; } = new();

    [JsonPropertyName("vm1")]
    public InfraVm1Section Vm1 { get; set; } = new();

    [JsonPropertyName("door")]
    public InfraDoorSection Door { get; set; } = new();

    [JsonPropertyName("object_storage")]
    public InfraObjectStorageSection ObjectStorage { get; set; } = new();

    [JsonPropertyName("budget_brake")]
    public InfraBudgetBrakeSection? BudgetBrake { get; set; }

    [JsonPropertyName("ssh")]
    public InfraSshSection Ssh { get; set; } = new();

    /// <summary>
    /// Seed a publishable v2 document from local manage config.
    /// Never copies SSH private key paths, OCI config paths, or RCON passwords.
    /// </summary>
    public static InfraMetaDocument FromLocal(
        ManagerLocalConfig config,
        string? stackVersion = null,
        string? serverKind = null,
        string? minecraftVersion = null,
        string? serverJarSha1 = null,
        string? createdAt = null,
        DateTimeOffset? nowUtc = null)
    {
        var now = FormatUtc(nowUtc ?? DateTimeOffset.UtcNow);
        var prefixes = config.ObjectStorage.Prefixes;
        return new InfraMetaDocument
        {
            Version = DocumentVersion,
            InfraSchemaValue = InfraSchema,
            StackVersion = string.IsNullOrWhiteSpace(stackVersion)
                ? DefaultStackVersion
                : stackVersion.Trim(),
            CreatedAt = string.IsNullOrWhiteSpace(createdAt) ? now : createdAt.Trim(),
            UpdatedAt = now,
            StackName = DefaultStackName,
            Mode = ModeAlwaysFree,
            Region = config.Oci.Region?.Trim() ?? "",
            TenancyId = config.Oci.TenancyId?.Trim() ?? "",
            CompartmentId = config.Oci.CompartmentId?.Trim() ?? "",
            Play = new InfraPlaySection
            {
                ReservedPublicIp = config.Play.ReservedPublicIp?.Trim() ?? "",
                ReservedPublicIpId = config.Play.ReservedPublicIpId?.Trim() ?? "",
            },
            Game = new InfraGameSection
            {
                ServerKind = string.IsNullOrWhiteSpace(serverKind) ? "vanilla" : serverKind.Trim(),
                MinecraftVersion = string.IsNullOrWhiteSpace(minecraftVersion)
                    ? "unspecified"
                    : minecraftVersion.Trim(),
                ServerJarSha1 = string.IsNullOrWhiteSpace(serverJarSha1)
                    ? null
                    : serverJarSha1.Trim(),
            },
            Network = new InfraNetworkSection
            {
                VcnId = config.Network.VcnId?.Trim() ?? "",
                SubnetId = config.Network.SubnetId?.Trim() ?? "",
                SecurityListId = config.Network.SecurityListId?.Trim() ?? "",
                MinecraftPort = config.Network.MinecraftPort > 0 ? config.Network.MinecraftPort : 25565,
                SshPort = config.Network.SshPort > 0 ? config.Network.SshPort : 22,
            },
            Vm1 = new InfraVm1Section
            {
                InstanceId = config.Vm1.InstanceId?.Trim() ?? "",
                DisplayName = string.IsNullOrWhiteSpace(config.Vm1.DisplayName)
                    ? "mcmgr-vm1"
                    : config.Vm1.DisplayName.Trim(),
                Shape = string.IsNullOrWhiteSpace(config.Vm1.Shape)
                    ? "VM.Standard.A1.Flex"
                    : config.Vm1.Shape.Trim(),
                ShapeOcpus = config.Vm1.ShapeOcpus > 0 ? config.Vm1.ShapeOcpus : 4,
                ShapeMemoryGb = config.Vm1.ShapeMemoryGb > 0 ? config.Vm1.ShapeMemoryGb : 24,
                PrimaryPrivateIp = config.Vm1.PrimaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIp = config.Vm1.SecondaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIpId = config.Vm1.SecondaryPrivateIpId?.Trim() ?? "",
                SshHost = NullIfWhiteSpace(config.Vm1.SshHost),
                SshUser = string.IsNullOrWhiteSpace(config.Vm1.SshUser) ? "ubuntu" : config.Vm1.SshUser.Trim(),
                WorldPath = string.IsNullOrWhiteSpace(config.Vm1.WorldPath)
                    ? "/home/ubuntu/minecraft/server/world"
                    : config.Vm1.WorldPath.Trim(),
                MinecraftUnit = string.IsNullOrWhiteSpace(config.Vm1.MinecraftUnit)
                    ? "minecraft"
                    : config.Vm1.MinecraftUnit.Trim(),
            },
            Door = new InfraDoorSection
            {
                InstanceId = config.Door.InstanceId?.Trim() ?? "",
                DisplayName = string.IsNullOrWhiteSpace(config.Door.DisplayName)
                    ? "mcmgr-door"
                    : config.Door.DisplayName.Trim(),
                PrimaryPrivateIp = config.Door.PrimaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIp = config.Door.SecondaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIpId = config.Door.SecondaryPrivateIpId?.Trim() ?? "",
                SshHost = NullIfWhiteSpace(config.Door.SshHost),
                SshUser = string.IsNullOrWhiteSpace(config.Door.SshUser) ? "ubuntu" : config.Door.SshUser.Trim(),
                HttpPort = config.Door.HttpPort > 0 ? config.Door.HttpPort : 8080,
            },
            ObjectStorage = new InfraObjectStorageSection
            {
                Namespace = config.ObjectStorage.Namespace?.Trim() ?? "",
                Bucket = config.ObjectStorage.Bucket?.Trim() ?? "",
                BucketId = config.ObjectStorage.BucketId?.Trim() ?? "",
                SoftCapGb = config.ObjectStorage.SoftCapGb > 0 ? config.ObjectStorage.SoftCapGb : 9.5,
                BackupEnabled = config.ObjectStorage.BackupEnabled,
                Prefixes = new InfraPrefixMap
                {
                    Meta = NormalizePrefix(prefixes.Meta, "meta/"),
                    Ledger = NormalizePrefix(prefixes.Ledger, "ledger/"),
                    Budget = NormalizePrefix(prefixes.Budget, "budget/"),
                    Ip = NormalizePrefix(prefixes.Ip, "ip/"),
                    Messages = NormalizePrefix(prefixes.Messages, "messages/"),
                    Backups = NormalizePrefix(prefixes.Backups, "backups/"),
                },
            },
            BudgetBrake = null,
            Ssh = new InfraSshSection
            {
                PublicKeyFingerprint = null,
                PrivateKeyLocation = "admin_pc_only",
            },
        };
    }

    public void StampUpdated(DateTimeOffset? nowUtc = null)
    {
        UpdatedAt = FormatUtc(nowUtc ?? DateTimeOffset.UtcNow);
        if (string.IsNullOrWhiteSpace(CreatedAt))
            CreatedAt = UpdatedAt;
    }

    /// <summary>
    /// Soft-validate fields needed for Connect existing / Phase 4 hydration.
    /// Returns human-readable problems (empty = ok).
    /// </summary>
    public IReadOnlyList<string> ValidateForPublish()
    {
        var errors = new List<string>();
        if (Version != DocumentVersion)
            errors.Add($"version must be {DocumentVersion}.");
        if (InfraSchemaValue != InfraSchema)
            errors.Add($"infra_schema must be {InfraSchema}.");
        if (string.IsNullOrWhiteSpace(StackVersion))
            errors.Add("stack_version is required.");
        if (string.IsNullOrWhiteSpace(StackName))
            errors.Add("stack_name is required.");
        if (!string.Equals(Mode, ModeAlwaysFree, StringComparison.Ordinal))
            errors.Add($"mode must be '{ModeAlwaysFree}' for MVP.");
        Require(errors, Region, "region");
        Require(errors, TenancyId, "tenancy_id");
        Require(errors, CompartmentId, "compartment_id");
        Require(errors, Play.ReservedPublicIp, "play.reserved_public_ip");
        Require(errors, Play.ReservedPublicIpId, "play.reserved_public_ip_id");
        Require(errors, Game.ServerKind, "game.server_kind");
        Require(errors, Game.MinecraftVersion, "game.minecraft_version");
        Require(errors, Network.VcnId, "network.vcn_id");
        Require(errors, Network.SubnetId, "network.subnet_id");
        Require(errors, Network.SecurityListId, "network.security_list_id");
        if (Network.MinecraftPort <= 0)
            errors.Add("network.minecraft_port must be > 0.");
        if (Network.SshPort <= 0)
            errors.Add("network.ssh_port must be > 0.");
        ValidateVm1(errors);
        ValidateDoor(errors);
        ValidateObjectStorage(errors);
        if (ContainsSecretLeak(out var leak))
            errors.Add(leak!);
        return errors;
    }

    public string FormatSummary()
    {
        var play = string.IsNullOrWhiteSpace(Play.ReservedPublicIp) ? "—" : Play.ReservedPublicIp;
        var vm1 = string.IsNullOrWhiteSpace(Vm1.DisplayName) ? Vm1.InstanceId : Vm1.DisplayName;
        var door = string.IsNullOrWhiteSpace(Door.DisplayName) ? Door.InstanceId : Door.DisplayName;
        return
            $"v{Version} infra_schema={InfraSchemaValue} stack={StackVersion} mode={Mode} "
            + $"region={Region} play={play} vm1={vm1} door={door} "
            + $"game={Game.ServerKind}/{Game.MinecraftVersion} "
            + $"bucket={ObjectStorage.Namespace}/{ObjectStorage.Bucket}";
    }

    /// <summary>
    /// Operator-facing Connect-existing confirm text (profile, region, compartment, play IP, VMs, bucket).
    /// </summary>
    public string FormatConnectSummary(string profileName, string compartmentName)
    {
        var play = string.IsNullOrWhiteSpace(Play.ReservedPublicIp) ? "—" : Play.ReservedPublicIp;
        var vm1 = string.IsNullOrWhiteSpace(Vm1.DisplayName) ? Vm1.InstanceId : Vm1.DisplayName;
        var door = string.IsNullOrWhiteSpace(Door.DisplayName) ? Door.InstanceId : Door.DisplayName;
        var bucket = string.IsNullOrWhiteSpace(ObjectStorage.Bucket)
            ? "—"
            : $"{ObjectStorage.Namespace}/{ObjectStorage.Bucket}";
        return
            $"Profile: {profileName}\n"
            + $"Region: {Region}\n"
            + $"Compartment: {compartmentName}\n"
            + $"Play IP: {play}\n"
            + $"VM1: {vm1}\n"
            + $"Door: {door}\n"
            + $"Bucket: {bucket}";
    }

    /// <summary>
    /// Hydrate local manage config from meta. Never copies SSH private key paths, OCI config
    /// paths, or RCON passwords from Object Storage — those stay operator-local.
    /// </summary>
    public ManagerLocalConfig ToLocalConfig(
        string ociConfigFile,
        string ociProfile,
        string sshKeyPath,
        string? rconPassword = null,
        ManagerLocalConfig? preserveLocal = null)
    {
        var key = FirstNonEmpty(sshKeyPath, preserveLocal?.Vm1.SshKeyPath, preserveLocal?.Door.SshKeyPath);
        var rcon = FirstNonEmpty(rconPassword, preserveLocal?.Rcon.Password);
        var prefixes = ObjectStorage.Prefixes;
        return new ManagerLocalConfig
        {
            SchemaVersion = 1,
            AdminName = string.IsNullOrWhiteSpace(preserveLocal?.AdminName)
                ? "admin"
                : preserveLocal!.AdminName,
            Oci = new OciSettings
            {
                ConfigFile = string.IsNullOrWhiteSpace(ociConfigFile)
                    ? "%USERPROFILE%\\.oci\\config"
                    : ociConfigFile.Trim(),
                Profile = string.IsNullOrWhiteSpace(ociProfile) ? "DEFAULT" : ociProfile.Trim(),
                Region = Region?.Trim() ?? "",
                CompartmentId = CompartmentId?.Trim() ?? "",
                TenancyId = TenancyId?.Trim() ?? "",
            },
            Network = new NetworkSettings
            {
                VcnId = Network.VcnId?.Trim() ?? "",
                SubnetId = Network.SubnetId?.Trim() ?? "",
                SecurityListId = Network.SecurityListId?.Trim() ?? "",
                MinecraftPort = Network.MinecraftPort > 0 ? Network.MinecraftPort : 25565,
                SshPort = Network.SshPort > 0 ? Network.SshPort : 22,
                FirewalldZone = preserveLocal?.Network.FirewalldZone ?? "public",
            },
            Vm1 = new Vm1Settings
            {
                InstanceId = Vm1.InstanceId?.Trim() ?? "",
                DisplayName = Vm1.DisplayName?.Trim() ?? "",
                Shape = Vm1.Shape?.Trim() ?? "",
                ShapeOcpus = Vm1.ShapeOcpus,
                ShapeMemoryGb = Vm1.ShapeMemoryGb,
                PrimaryPrivateIp = Vm1.PrimaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIp = Vm1.SecondaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIpId = Vm1.SecondaryPrivateIpId?.Trim() ?? "",
                SshHost = Vm1.SshHost?.Trim() ?? "",
                SshUser = string.IsNullOrWhiteSpace(Vm1.SshUser) ? "ubuntu" : Vm1.SshUser.Trim(),
                SshKeyPath = key,
                WorldPath = Vm1.WorldPath?.Trim() ?? "",
                MinecraftUnit = string.IsNullOrWhiteSpace(Vm1.MinecraftUnit)
                    ? "minecraft"
                    : Vm1.MinecraftUnit.Trim(),
            },
            Door = new DoorSettings
            {
                InstanceId = Door.InstanceId?.Trim() ?? "",
                DisplayName = Door.DisplayName?.Trim() ?? "",
                PrimaryPrivateIp = Door.PrimaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIp = Door.SecondaryPrivateIp?.Trim() ?? "",
                SecondaryPrivateIpId = Door.SecondaryPrivateIpId?.Trim() ?? "",
                SshHost = Door.SshHost?.Trim() ?? "",
                SshUser = string.IsNullOrWhiteSpace(Door.SshUser) ? "ubuntu" : Door.SshUser.Trim(),
                SshKeyPath = key,
                HttpPort = Door.HttpPort > 0 ? Door.HttpPort : 8080,
            },
            Play = new PlaySettings
            {
                ReservedPublicIp = Play.ReservedPublicIp?.Trim() ?? "",
                ReservedPublicIpId = Play.ReservedPublicIpId?.Trim() ?? "",
            },
            ObjectStorage = new ObjectStorageSettings
            {
                Namespace = ObjectStorage.Namespace?.Trim() ?? "",
                Bucket = ObjectStorage.Bucket?.Trim() ?? "",
                BucketId = ObjectStorage.BucketId?.Trim() ?? "",
                SoftCapGb = ObjectStorage.SoftCapGb > 0 ? ObjectStorage.SoftCapGb : 9.5,
                BackupEnabled = ObjectStorage.BackupEnabled,
                Prefixes = new ObjectStoragePrefixes
                {
                    Meta = NormalizePrefix(prefixes.Meta, "meta/"),
                    Ledger = NormalizePrefix(prefixes.Ledger, "ledger/"),
                    Budget = NormalizePrefix(prefixes.Budget, "budget/"),
                    Ip = NormalizePrefix(prefixes.Ip, "ip/"),
                    Messages = NormalizePrefix(prefixes.Messages, "messages/"),
                    Backups = NormalizePrefix(prefixes.Backups, "backups/"),
                },
            },
            Budget = preserveLocal?.Budget ?? new BudgetSettings(),
            Rcon = new RconSettings
            {
                Port = preserveLocal?.Rcon.Port > 0 ? preserveLocal.Rcon.Port : 25575,
                Password = rcon,
            },
        };
    }

    /// <summary>
    /// Soft-validate for Connect existing. Missing required OCIDs are errors (skip stack).
    /// Schema/version/mode mismatches are warnings (confirm, do not mutate).
    /// <c>ssh_host</c> may be null.
    /// </summary>
    public IReadOnlyList<string> ValidateForConnect(out IReadOnlyList<string> warnings)
    {
        var errors = new List<string>();
        var warns = new List<string>();

        if (Version != DocumentVersion)
        {
            warns.Add(
                $"Document version is {Version} (this Manager writes {DocumentVersion}). "
                + "Connect will not modify the stack.");
        }

        if (InfraSchemaValue != InfraSchema)
        {
            warns.Add(
                $"infra_schema is {InfraSchemaValue} (this Manager expects {InfraSchema}). "
                + "Connect will not modify the stack.");
        }

        if (!string.IsNullOrWhiteSpace(Mode)
            && !string.Equals(Mode, ModeAlwaysFree, StringComparison.Ordinal))
        {
            warns.Add($"mode is '{Mode}' (MVP expects '{ModeAlwaysFree}').");
        }

        if (string.IsNullOrWhiteSpace(StackVersion))
            errors.Add("stack_version is required.");
        if (string.IsNullOrWhiteSpace(StackName))
            errors.Add("stack_name is required.");
        Require(errors, Region, "region");
        Require(errors, TenancyId, "tenancy_id");
        Require(errors, CompartmentId, "compartment_id");
        Require(errors, Play.ReservedPublicIp, "play.reserved_public_ip");
        Require(errors, Play.ReservedPublicIpId, "play.reserved_public_ip_id");
        Require(errors, Game.ServerKind, "game.server_kind");
        Require(errors, Game.MinecraftVersion, "game.minecraft_version");
        Require(errors, Network.VcnId, "network.vcn_id");
        Require(errors, Network.SubnetId, "network.subnet_id");
        Require(errors, Network.SecurityListId, "network.security_list_id");
        if (Network.MinecraftPort <= 0)
            errors.Add("network.minecraft_port must be > 0.");
        if (Network.SshPort <= 0)
            errors.Add("network.ssh_port must be > 0.");
        ValidateVm1(errors);
        ValidateDoor(errors);
        ValidateObjectStorage(errors);
        if (ContainsSecretLeak(out var leak))
            errors.Add(leak!);

        warnings = warns;
        return errors;
    }

    public static bool IsSupportedSchema(int infraSchema) =>
        infraSchema == InfraSchema;

    private void ValidateVm1(List<string> errors)
    {
        Require(errors, Vm1.InstanceId, "vm1.instance_id");
        Require(errors, Vm1.DisplayName, "vm1.display_name");
        Require(errors, Vm1.Shape, "vm1.shape");
        if (Vm1.ShapeOcpus <= 0)
            errors.Add("vm1.shape_ocpus must be > 0.");
        if (Vm1.ShapeMemoryGb <= 0)
            errors.Add("vm1.shape_memory_gb must be > 0.");
        Require(errors, Vm1.PrimaryPrivateIp, "vm1.primary_private_ip");
        Require(errors, Vm1.SecondaryPrivateIp, "vm1.secondary_private_ip");
        Require(errors, Vm1.SecondaryPrivateIpId, "vm1.secondary_private_ip_id");
        Require(errors, Vm1.SshUser, "vm1.ssh_user");
        Require(errors, Vm1.WorldPath, "vm1.world_path");
        Require(errors, Vm1.MinecraftUnit, "vm1.minecraft_unit");
    }

    private void ValidateDoor(List<string> errors)
    {
        Require(errors, Door.InstanceId, "door.instance_id");
        Require(errors, Door.DisplayName, "door.display_name");
        Require(errors, Door.PrimaryPrivateIp, "door.primary_private_ip");
        Require(errors, Door.SecondaryPrivateIp, "door.secondary_private_ip");
        Require(errors, Door.SecondaryPrivateIpId, "door.secondary_private_ip_id");
        Require(errors, Door.SshUser, "door.ssh_user");
        if (Door.HttpPort <= 0)
            errors.Add("door.http_port must be > 0.");
    }

    private void ValidateObjectStorage(List<string> errors)
    {
        Require(errors, ObjectStorage.Namespace, "object_storage.namespace");
        Require(errors, ObjectStorage.Bucket, "object_storage.bucket");
        Require(errors, ObjectStorage.BucketId, "object_storage.bucket_id");
        if (ObjectStorage.SoftCapGb <= 0)
            errors.Add("object_storage.soft_cap_gb must be > 0.");
        Require(errors, ObjectStorage.Prefixes.Meta, "object_storage.prefixes.meta");
        Require(errors, ObjectStorage.Prefixes.Ledger, "object_storage.prefixes.ledger");
        Require(errors, ObjectStorage.Prefixes.Budget, "object_storage.prefixes.budget");
        Require(errors, ObjectStorage.Prefixes.Ip, "object_storage.prefixes.ip");
        Require(errors, ObjectStorage.Prefixes.Messages, "object_storage.prefixes.messages");
        Require(errors, ObjectStorage.Prefixes.Backups, "object_storage.prefixes.backups");
    }

    private bool ContainsSecretLeak(out string? message)
    {
        message = null;
        if (!string.Equals(Ssh.PrivateKeyLocation, "admin_pc_only", StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(Ssh.PrivateKeyLocation)
            && (Ssh.PrivateKeyLocation.Contains('\\', StringComparison.Ordinal)
                || Ssh.PrivateKeyLocation.Contains('/', StringComparison.Ordinal)
                || Ssh.PrivateKeyLocation.EndsWith(".key", StringComparison.OrdinalIgnoreCase)
                || Ssh.PrivateKeyLocation.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)))
        {
            message = "ssh.private_key_location must not contain a local path; use admin_pc_only.";
            return true;
        }

        return false;
    }

    private static void Require(List<string> errors, string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add($"{field} is required.");
    }

    private static string FormatUtc(DateTimeOffset now) =>
        now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string NormalizePrefix(string? value, string fallback)
    {
        var text = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return text.EndsWith('/') ? text : text + "/";
    }
}

public sealed class InfraPlaySection
{
    [JsonPropertyName("reserved_public_ip")]
    public string ReservedPublicIp { get; set; } = "";

    [JsonPropertyName("reserved_public_ip_id")]
    public string ReservedPublicIpId { get; set; } = "";
}

public sealed class InfraGameSection
{
    [JsonPropertyName("server_kind")]
    public string ServerKind { get; set; } = "vanilla";

    [JsonPropertyName("minecraft_version")]
    public string MinecraftVersion { get; set; } = "unspecified";

    [JsonPropertyName("server_jar_sha1")]
    public string? ServerJarSha1 { get; set; }
}

public sealed class InfraNetworkSection
{
    [JsonPropertyName("vcn_id")]
    public string VcnId { get; set; } = "";

    [JsonPropertyName("subnet_id")]
    public string SubnetId { get; set; } = "";

    [JsonPropertyName("security_list_id")]
    public string SecurityListId { get; set; } = "";

    [JsonPropertyName("minecraft_port")]
    public int MinecraftPort { get; set; } = 25565;

    [JsonPropertyName("ssh_port")]
    public int SshPort { get; set; } = 22;
}

public sealed class InfraVm1Section
{
    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("shape")]
    public string Shape { get; set; } = "";

    [JsonPropertyName("shape_ocpus")]
    public double ShapeOcpus { get; set; }

    [JsonPropertyName("shape_memory_gb")]
    public double ShapeMemoryGb { get; set; }

    [JsonPropertyName("primary_private_ip")]
    public string PrimaryPrivateIp { get; set; } = "";

    [JsonPropertyName("secondary_private_ip")]
    public string SecondaryPrivateIp { get; set; } = "";

    [JsonPropertyName("secondary_private_ip_id")]
    public string SecondaryPrivateIpId { get; set; } = "";

    [JsonPropertyName("ssh_host")]
    public string? SshHost { get; set; }

    [JsonPropertyName("ssh_user")]
    public string SshUser { get; set; } = "ubuntu";

    [JsonPropertyName("world_path")]
    public string WorldPath { get; set; } = "";

    [JsonPropertyName("minecraft_unit")]
    public string MinecraftUnit { get; set; } = "minecraft";
}

public sealed class InfraDoorSection
{
    [JsonPropertyName("instance_id")]
    public string InstanceId { get; set; } = "";

    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("primary_private_ip")]
    public string PrimaryPrivateIp { get; set; } = "";

    [JsonPropertyName("secondary_private_ip")]
    public string SecondaryPrivateIp { get; set; } = "";

    [JsonPropertyName("secondary_private_ip_id")]
    public string SecondaryPrivateIpId { get; set; } = "";

    [JsonPropertyName("ssh_host")]
    public string? SshHost { get; set; }

    [JsonPropertyName("ssh_user")]
    public string SshUser { get; set; } = "ubuntu";

    [JsonPropertyName("http_port")]
    public int HttpPort { get; set; } = 8080;
}

public sealed class InfraObjectStorageSection
{
    [JsonPropertyName("namespace")]
    public string Namespace { get; set; } = "";

    [JsonPropertyName("bucket")]
    public string Bucket { get; set; } = "";

    [JsonPropertyName("bucket_id")]
    public string BucketId { get; set; } = "";

    [JsonPropertyName("soft_cap_gb")]
    public double SoftCapGb { get; set; } = 9.5;

    [JsonPropertyName("backup_enabled")]
    public bool BackupEnabled { get; set; } = true;

    [JsonPropertyName("prefixes")]
    public InfraPrefixMap Prefixes { get; set; } = new();
}

public sealed class InfraPrefixMap
{
    [JsonPropertyName("meta")]
    public string Meta { get; set; } = "meta/";

    [JsonPropertyName("ledger")]
    public string Ledger { get; set; } = "ledger/";

    [JsonPropertyName("budget")]
    public string Budget { get; set; } = "budget/";

    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "ip/";

    [JsonPropertyName("messages")]
    public string Messages { get; set; } = "messages/";

    [JsonPropertyName("backups")]
    public string Backups { get; set; } = "backups/";
}

public sealed class InfraBudgetBrakeSection
{
    [JsonPropertyName("budget_id")]
    public string? BudgetId { get; set; }

    [JsonPropertyName("function_id")]
    public string? FunctionId { get; set; }
}

public sealed class InfraSshSection
{
    [JsonPropertyName("public_key_fingerprint")]
    public string? PublicKeyFingerprint { get; set; }

    [JsonPropertyName("private_key_location")]
    public string PrivateKeyLocation { get; set; } = "admin_pc_only";
}
