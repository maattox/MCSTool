using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McManager.Core.Config;
using McManager.Core.Usage;

namespace McManager.Core.Services;

/// <summary>Read/publish Object Storage <c>meta/infra.json</c> (canonical v2).</summary>
public sealed class InfraMetaStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
    };

    private readonly IObjectStorageService _objectStorage;
    private readonly ObjectStoragePrefixes _prefixes;

    public InfraMetaStore(IObjectStorageService objectStorage, ObjectStoragePrefixes prefixes)
    {
        _objectStorage = objectStorage;
        _prefixes = prefixes;
    }

    public string InfraObjectName => Combine(_prefixes.Meta, "infra.json");
    public string FlagsObjectName => Combine(_prefixes.Meta, "flags.json");

    public async Task<ServiceResult<InfraMetaReadResult>> GetAsync(
        CancellationToken cancellationToken = default)
    {
        var got = await _objectStorage.GetObjectAsync(InfraObjectName, cancellationToken);
        if (!got.Succeeded || got.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(got.Error))
            {
                return ServiceResult<InfraMetaReadResult>.Ok(new InfraMetaReadResult
                {
                    Missing = true,
                    Notes = $"{InfraObjectName} is missing.",
                });
            }

            return ServiceResult<InfraMetaReadResult>.Fail(got.Error ?? $"Get {InfraObjectName} failed.");
        }

        var etag = got.Value.Etag;
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(got.Value.Content));
        }
        catch (JsonException ex)
        {
            return ServiceResult<InfraMetaReadResult>.Fail(
                $"{InfraObjectName} JSON parse failed: {ex.Message}");
        }

        if (root is not JsonObject obj)
            return ServiceResult<InfraMetaReadResult>.Fail($"{InfraObjectName} root is not a JSON object.");

        var version = ReadInt(obj, "version");
        var schema = ReadInt(obj, "infra_schema");
        var looksNestedV2 = obj["play"] is JsonObject
                            && obj["vm1"] is JsonObject
                            && obj["door"] is JsonObject
                            && obj["object_storage"] is JsonObject;

        if (version > InfraMetaDocument.DocumentVersion
            || schema > InfraMetaDocument.InfraSchema)
        {
            return ServiceResult<InfraMetaReadResult>.Fail(
                $"{InfraObjectName} is newer than this Manager supports "
                + $"(version={version}, infra_schema={schema}; "
                + $"max version={InfraMetaDocument.DocumentVersion}, "
                + $"max infra_schema={InfraMetaDocument.InfraSchema}).");
        }

        if (!looksNestedV2 || version < InfraMetaDocument.DocumentVersion
            || schema < InfraMetaDocument.InfraSchema)
        {
            return ServiceResult<InfraMetaReadResult>.Ok(new InfraMetaReadResult
            {
                IsLegacy = true,
                LegacyVersion = version > 0 ? version : 1,
                LegacyInfraSchema = schema > 0 ? schema : 1,
                LegacySummary = SummarizeLegacy(obj),
                Etag = etag,
                Notes =
                    $"{InfraObjectName} is legacy flat/incomplete "
                    + $"(version={version}, infra_schema={schema}). Publish to migrate to nested v2.",
            });
        }

        InfraMetaDocument doc;
        try
        {
            doc = root.Deserialize<InfraMetaDocument>(JsonOptions)
                  ?? throw new JsonException("Deserialized null document.");
        }
        catch (JsonException ex)
        {
            return ServiceResult<InfraMetaReadResult>.Fail(
                $"{InfraObjectName} v2 deserialize failed: {ex.Message}");
        }

        if (!InfraMetaDocument.IsSupportedSchema(doc.InfraSchemaValue))
        {
            return ServiceResult<InfraMetaReadResult>.Fail(
                $"Unsupported infra_schema={doc.InfraSchemaValue} "
                + $"(expected {InfraMetaDocument.InfraSchema}).");
        }

        return ServiceResult<InfraMetaReadResult>.Ok(new InfraMetaReadResult
        {
            Document = doc,
            Etag = etag,
            Notes = $"Loaded {InfraObjectName}: {doc.FormatSummary()}",
        });
    }

    /// <summary>
    /// Parse for Connect existing without writing or migrating. Missing OCIDs skip the stack.
    /// Newer/older schema stay on the candidate so
    /// <see cref="ConnectExistingCompatibility"/> can block or extra-confirm.
    /// </summary>
    public static ServiceResult<InfraMetaConnectRead> ParseForConnect(byte[] jsonBytes)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(jsonBytes));
        }
        catch (JsonException ex)
        {
            return ServiceResult<InfraMetaConnectRead>.Fail($"meta/infra.json JSON parse failed: {ex.Message}");
        }

        if (root is not JsonObject obj)
            return ServiceResult<InfraMetaConnectRead>.Fail("meta/infra.json root is not a JSON object.");

        var version = ReadInt(obj, "version");
        var schema = ReadInt(obj, "infra_schema");
        var looksNestedV2 = obj["play"] is JsonObject
                            && obj["vm1"] is JsonObject
                            && obj["door"] is JsonObject
                            && obj["object_storage"] is JsonObject;

        if (!looksNestedV2)
        {
            var mapped = TryMapLegacy(obj);
            if (mapped is null)
            {
                return ServiceResult<InfraMetaConnectRead>.Ok(new InfraMetaConnectRead
                {
                    IsLegacy = true,
                    Skipped = true,
                    Notes =
                        "meta/infra.json is legacy/incomplete and does not contain enough OCIDs to connect. "
                        + SummarizeLegacy(obj),
                });
            }

            var legacyErrors = mapped.ValidateForConnect(out var legacyWarns);
            if (legacyErrors.Count > 0)
            {
                return ServiceResult<InfraMetaConnectRead>.Ok(new InfraMetaConnectRead
                {
                    IsLegacy = true,
                    Skipped = true,
                    Notes =
                        "Legacy meta/infra.json is missing required OCIDs: "
                        + string.Join(" ", legacyErrors),
                });
            }

            var legacyWarnings = new List<string>
            {
                "meta/infra.json is a legacy flat document. Connect will not migrate it; publish nested v2 later from Advanced.",
            };
            legacyWarnings.AddRange(legacyWarns);
            return ServiceResult<InfraMetaConnectRead>.Ok(new InfraMetaConnectRead
            {
                Document = mapped,
                IsLegacy = true,
                SchemaWarnings = legacyWarnings,
                Notes = "Parsed legacy meta/infra.json with a schema warning.",
            });
        }

        InfraMetaDocument doc;
        try
        {
            doc = root.Deserialize<InfraMetaDocument>(JsonOptions)
                  ?? throw new JsonException("Deserialized null document.");
        }
        catch (JsonException ex)
        {
            return ServiceResult<InfraMetaConnectRead>.Fail(
                $"meta/infra.json deserialize failed: {ex.Message}");
        }

        var errors = doc.ValidateForConnect(out var warnings);
        var warnList = warnings.ToList();
        if (version > InfraMetaDocument.DocumentVersion || schema > InfraMetaDocument.InfraSchema)
        {
            warnList.Add(
                $"This stack's meta is newer than this Manager "
                + $"(version={version}, infra_schema={schema}; "
                + $"max version={InfraMetaDocument.DocumentVersion}, "
                + $"max infra_schema={InfraMetaDocument.InfraSchema}). "
                + "Connect will not modify the stack.");
        }

        if (errors.Count > 0)
        {
            return ServiceResult<InfraMetaConnectRead>.Ok(new InfraMetaConnectRead
            {
                Document = doc,
                Skipped = true,
                SchemaWarnings = warnList,
                Notes = "meta/infra.json is missing required OCIDs: " + string.Join(" ", errors),
            });
        }

        return ServiceResult<InfraMetaConnectRead>.Ok(new InfraMetaConnectRead
        {
            Document = doc,
            SchemaWarnings = warnList,
            Notes = $"Parsed meta/infra.json: {doc.FormatSummary()}",
        });
    }

    private static InfraMetaDocument? TryMapLegacy(JsonObject obj)
    {
        string Flat(string key) => obj[key]?.ToString()?.Trim().Trim('"') ?? "";
        string Nested(string parent, string key)
        {
            if (obj[parent] is JsonObject nested)
                return nested[key]?.ToString()?.Trim().Trim('"') ?? "";
            return "";
        }

        var playIp = FirstNonEmpty(Flat("reserved_play_ip"), Nested("play", "reserved_public_ip"));
        var playId = FirstNonEmpty(Flat("reserved_play_ip_id"), Nested("play", "reserved_public_ip_id"));
        var vm1Id = FirstNonEmpty(Flat("vm1_instance_id"), Nested("vm1", "instance_id"));
        var doorId = FirstNonEmpty(
            Flat("vm2_instance_id"),
            Flat("door_instance_id"),
            Nested("door", "instance_id"));
        var bucket = FirstNonEmpty(Nested("object_storage", "bucket"), Flat("bucket"));
        var ns = FirstNonEmpty(Nested("object_storage", "namespace"), Flat("namespace"));
        if (string.IsNullOrWhiteSpace(playIp)
            || string.IsNullOrWhiteSpace(vm1Id)
            || string.IsNullOrWhiteSpace(doorId)
            || string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        return new InfraMetaDocument
        {
            Version = ReadInt(obj, "version") is var v && v > 0 ? v : 1,
            InfraSchemaValue = ReadInt(obj, "infra_schema") is var s && s > 0 ? s : 1,
            StackVersion = FirstNonEmpty(Flat("stack_version"), InfraMetaDocument.DefaultStackVersion),
            StackName = FirstNonEmpty(Flat("stack_name"), InfraMetaDocument.DefaultStackName),
            Mode = FirstNonEmpty(Flat("mode"), InfraMetaDocument.ModeAlwaysFree),
            Region = Flat("region"),
            TenancyId = Flat("tenancy_id"),
            CompartmentId = Flat("compartment_id"),
            Play = new InfraPlaySection
            {
                ReservedPublicIp = playIp,
                ReservedPublicIpId = playId,
            },
            Game = new InfraGameSection
            {
                ServerKind = FirstNonEmpty(Nested("game", "server_kind"), Flat("server_kind"), "vanilla"),
                MinecraftVersion = FirstNonEmpty(
                    Nested("game", "minecraft_version"),
                    Flat("minecraft_version"),
                    "unspecified"),
            },
            Network = new InfraNetworkSection
            {
                VcnId = FirstNonEmpty(Nested("network", "vcn_id"), Flat("vcn_id")),
                SubnetId = FirstNonEmpty(Nested("network", "subnet_id"), Flat("subnet_id")),
                SecurityListId = FirstNonEmpty(
                    Nested("network", "security_list_id"),
                    Flat("security_list_id")),
                MinecraftPort = 25565,
                SshPort = 22,
            },
            Vm1 = new InfraVm1Section
            {
                InstanceId = vm1Id,
                DisplayName = FirstNonEmpty(Nested("vm1", "display_name"), Flat("vm1_display_name"), "mcmgr-vm1"),
                Shape = FirstNonEmpty(Nested("vm1", "shape"), Flat("vm1_shape"), "VM.Standard.A1.Flex"),
                ShapeOcpus = 4,
                ShapeMemoryGb = 24,
                PrimaryPrivateIp = FirstNonEmpty(
                    Nested("vm1", "primary_private_ip"),
                    Flat("vm1_primary_private_ip")),
                SecondaryPrivateIp = FirstNonEmpty(
                    Nested("vm1", "secondary_private_ip"),
                    Flat("vm1_secondary_private_ip")),
                SecondaryPrivateIpId = FirstNonEmpty(
                    Nested("vm1", "secondary_private_ip_id"),
                    Flat("vm1_secondary_private_ip_id")),
                SshHost = NullIfEmpty(FirstNonEmpty(Nested("vm1", "ssh_host"), Flat("vm1_ssh_host"))),
                SshUser = FirstNonEmpty(Nested("vm1", "ssh_user"), "ubuntu"),
                WorldPath = FirstNonEmpty(
                    Nested("vm1", "world_path"),
                    Flat("world_path"),
                    "/home/ubuntu/minecraft/server/world"),
                MinecraftUnit = FirstNonEmpty(Nested("vm1", "minecraft_unit"), "minecraft"),
            },
            Door = new InfraDoorSection
            {
                InstanceId = doorId,
                DisplayName = FirstNonEmpty(Nested("door", "display_name"), Flat("door_display_name"), "mcmgr-door"),
                PrimaryPrivateIp = FirstNonEmpty(
                    Nested("door", "primary_private_ip"),
                    Flat("door_primary_private_ip")),
                SecondaryPrivateIp = FirstNonEmpty(
                    Nested("door", "secondary_private_ip"),
                    Flat("door_secondary_private_ip")),
                SecondaryPrivateIpId = FirstNonEmpty(
                    Nested("door", "secondary_private_ip_id"),
                    Flat("door_secondary_private_ip_id")),
                SshHost = NullIfEmpty(FirstNonEmpty(Nested("door", "ssh_host"), Flat("door_ssh_host"))),
                SshUser = FirstNonEmpty(Nested("door", "ssh_user"), "ubuntu"),
                HttpPort = 8080,
            },
            ObjectStorage = new InfraObjectStorageSection
            {
                Namespace = ns,
                Bucket = bucket,
                BucketId = FirstNonEmpty(Nested("object_storage", "bucket_id"), Flat("bucket_id")),
                SoftCapGb = 9.5,
                BackupEnabled = true,
            },
        };
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        return "";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public async Task<ServiceResult<InfraMetaPublishResult>> PublishFromLocalAsync(
        ManagerLocalConfig config,
        string? stackVersion = null,
        string? serverKind = null,
        string? minecraftVersion = null,
        string? serverJarSha1 = null,
        CancellationToken cancellationToken = default)
    {
        string? preserveCreatedAt = null;
        string? preserveJar = serverJarSha1;
        string? preserveKind = serverKind;
        string? preserveMcVersion = minecraftVersion;
        string? preserveStack = stackVersion;

        var existing = await GetAsync(cancellationToken);
        InfraMetaReadResult? priorRead = existing.Value;
        if (!existing.Succeeded || priorRead is null)
        {
            // Greenfield Setup: the object does not exist yet. Treat any not-found
            // GET as create-new rather than aborting the first publish.
            if (OciErrorFormatter.IsNotFoundMessage(existing.Error))
            {
                priorRead = new InfraMetaReadResult
                {
                    Missing = true,
                    Notes = $"{InfraObjectName} is missing.",
                };
            }
            else
            {
                return ServiceResult<InfraMetaPublishResult>.Fail(
                    existing.Error ?? "Failed to read existing meta.");
            }
        }

        if (priorRead.Document is { } prior)
        {
            preserveCreatedAt = prior.CreatedAt;
            if (string.IsNullOrWhiteSpace(preserveStack))
                preserveStack = prior.StackVersion;
            if (string.IsNullOrWhiteSpace(preserveKind))
                preserveKind = prior.Game.ServerKind;
            if (string.IsNullOrWhiteSpace(preserveMcVersion))
                preserveMcVersion = prior.Game.MinecraftVersion;
            if (string.IsNullOrWhiteSpace(preserveJar))
                preserveJar = prior.Game.ServerJarSha1;
        }

        var doc = InfraMetaDocument.FromLocal(
            config,
            stackVersion: preserveStack,
            serverKind: preserveKind,
            minecraftVersion: preserveMcVersion,
            serverJarSha1: preserveJar,
            createdAt: preserveCreatedAt);
        doc.StampUpdated();

        var problems = doc.ValidateForPublish();
        if (problems.Count > 0)
        {
            return ServiceResult<InfraMetaPublishResult>.Fail(
                "Cannot publish meta/infra.json: " + string.Join(" ", problems));
        }

        var metaExists = !priorRead.Missing;
        var metaEtag = priorRead.Etag;
        var requireMeta = ObjectStorageConditional.RequireEtagIfPresent(
            InfraObjectName, metaExists, metaEtag);
        if (!requireMeta.Succeeded)
        {
            return ServiceResult<InfraMetaPublishResult>.Fail(
                requireMeta.Error ?? ObjectStorageConflict.MissingEtag(InfraObjectName));
        }

        var put = await PutJsonAsync(InfraObjectName, doc, metaEtag, cancellationToken);
        if (!put.Succeeded)
            return ServiceResult<InfraMetaPublishResult>.Fail(put.Error ?? "Put infra meta failed.");

        var flagsResult = await GetJsonAsync<MetaFlagsDocument>(FlagsObjectName, cancellationToken);
        MetaFlagsDocument flags;
        string? flagsEtag = null;
        if (flagsResult.Succeeded && flagsResult.Value is not null)
        {
            flags = flagsResult.Value.Document;
            flagsEtag = flagsResult.Value.Etag;
            flags.Normalize();
            var requireFlags = ObjectStorageConditional.RequireEtagIfPresent(
                FlagsObjectName, objectExists: true, flagsEtag);
            if (!requireFlags.Succeeded)
            {
                return ServiceResult<InfraMetaPublishResult>.Fail(
                    requireFlags.Error ?? ObjectStorageConflict.MissingEtag(FlagsObjectName));
            }
        }
        else if (OciErrorFormatter.IsNotFoundMessage(flagsResult.Error))
        {
            flags = MetaFlagsDocument.Empty();
        }
        else
        {
            return ServiceResult<InfraMetaPublishResult>.Fail(
                flagsResult.Error ?? "Infra meta saved but failed to load flags.");
        }

        flags.MarkDirty("meta", ["door", "vm1"], clearWriter: "manager");
        var putFlags = await PutJsonAsync(FlagsObjectName, flags, flagsEtag, cancellationToken);
        if (!putFlags.Succeeded)
        {
            return ServiceResult<InfraMetaPublishResult>.Fail(
                putFlags.Error ?? "Infra meta saved but failed to update flags.");
        }

        var migrated = priorRead.IsLegacy || priorRead.Missing;
        return ServiceResult<InfraMetaPublishResult>.Ok(new InfraMetaPublishResult
        {
            Document = doc,
            Flags = flags,
            MigratedFromLegacy = migrated,
            Message =
                $"Published {InfraObjectName} "
                + $"(infra_schema={doc.InfraSchemaValue}, stack={doc.StackVersion}); "
                + "set meta flags door=true, vm1=true; manager=false."
                + (migrated ? " Migrated from missing/legacy object." : ""),
        });
    }

    private static int ReadInt(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node is null)
            return 0;
        try
        {
            return node.GetValue<int>();
        }
        catch (FormatException)
        {
            return int.TryParse(node.ToString(), out var parsed) ? parsed : 0;
        }
        catch (InvalidOperationException)
        {
            return int.TryParse(node.ToString(), out var parsed) ? parsed : 0;
        }
    }

    private static string SummarizeLegacy(JsonObject obj)
    {
        string Get(string key) => obj[key]?.ToString()?.Trim('"') ?? "—";
        var os = obj["object_storage"] as JsonObject;
        var bucket = os?["bucket"]?.ToString()?.Trim('"') ?? "—";
        return
            $"legacy version={Get("version")} infra_schema={Get("infra_schema")} "
            + $"region={Get("region")} play={Get("reserved_play_ip")} "
            + $"vm1={Get("vm1_instance_id")} door={Get("vm2_instance_id")} bucket={bucket}";
    }

    private sealed class JsonGet<T> where T : class
    {
        public required T Document { get; init; }
        public string? Etag { get; init; }
    }

    private async Task<ServiceResult<JsonGet<T>>> GetJsonAsync<T>(
        string objectName,
        CancellationToken cancellationToken)
        where T : class
    {
        var got = await _objectStorage.GetObjectAsync(objectName, cancellationToken);
        if (!got.Succeeded || got.Value is null)
            return ServiceResult<JsonGet<T>>.Fail(got.Error ?? $"GetObject {objectName} failed.");

        try
        {
            var doc = JsonSerializer.Deserialize<T>(got.Value.Content, JsonOptions);
            if (doc is null)
                return ServiceResult<JsonGet<T>>.Fail($"{objectName} is empty or invalid JSON.");
            return ServiceResult<JsonGet<T>>.Ok(new JsonGet<T>
            {
                Document = doc,
                Etag = got.Value.Etag,
            });
        }
        catch (JsonException ex)
        {
            return ServiceResult<JsonGet<T>>.Fail($"{objectName} JSON parse failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult> PutJsonAsync<T>(
        string objectName,
        T document,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(document, JsonOptions);
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!json.EndsWith('\n'))
        {
            var withNl = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, withNl, 0, bytes.Length);
            withNl[^1] = (byte)'\n';
            bytes = withNl;
        }

        return await _objectStorage.PutBytesAsync(
            objectName,
            bytes,
            "application/json",
            ifMatch,
            cancellationToken);
    }

    private static string Combine(string prefix, string name)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            return name;
        return prefix.EndsWith('/') ? prefix + name : prefix + "/" + name;
    }
}

public sealed class InfraMetaReadResult
{
    public InfraMetaDocument? Document { get; init; }
    public bool Missing { get; init; }
    public bool IsLegacy { get; init; }
    public int? LegacyVersion { get; init; }
    public int? LegacyInfraSchema { get; init; }
    public string? LegacySummary { get; init; }
    public string? Etag { get; init; }
    public string Notes { get; init; } = "";
}

public sealed class InfraMetaPublishResult
{
    public required InfraMetaDocument Document { get; init; }
    public required MetaFlagsDocument Flags { get; init; }
    public bool MigratedFromLegacy { get; init; }
    public required string Message { get; init; }
}

public sealed class InfraMetaConnectRead
{
    public InfraMetaDocument? Document { get; init; }
    public bool IsLegacy { get; init; }
    public bool Skipped { get; init; }
    public IReadOnlyList<string> SchemaWarnings { get; init; } = [];
    public string Notes { get; init; } = "";
}
