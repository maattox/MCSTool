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
        var bytes = await _objectStorage.GetBytesAsync(InfraObjectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
        {
            if (OciErrorFormatter.IsNotFoundMessage(bytes.Error))
            {
                return ServiceResult<InfraMetaReadResult>.Ok(new InfraMetaReadResult
                {
                    Missing = true,
                    Notes = $"{InfraObjectName} is missing.",
                });
            }

            return ServiceResult<InfraMetaReadResult>.Fail(bytes.Error ?? $"Get {InfraObjectName} failed.");
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(Encoding.UTF8.GetString(bytes.Value));
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
            Notes = $"Loaded {InfraObjectName}: {doc.FormatSummary()}",
        });
    }

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
        if (!existing.Succeeded || existing.Value is null)
            return ServiceResult<InfraMetaPublishResult>.Fail(existing.Error ?? "Failed to read existing meta.");

        if (existing.Value.Document is { } prior)
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

        var put = await PutJsonAsync(InfraObjectName, doc, cancellationToken);
        if (!put.Succeeded)
            return ServiceResult<InfraMetaPublishResult>.Fail(put.Error ?? "Put infra meta failed.");

        var flagsResult = await GetJsonAsync<MetaFlagsDocument>(FlagsObjectName, cancellationToken);
        MetaFlagsDocument flags;
        if (flagsResult.Succeeded && flagsResult.Value is not null)
        {
            flags = flagsResult.Value;
            flags.Normalize();
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
        var putFlags = await PutJsonAsync(FlagsObjectName, flags, cancellationToken);
        if (!putFlags.Succeeded)
        {
            return ServiceResult<InfraMetaPublishResult>.Fail(
                putFlags.Error ?? "Infra meta saved but failed to update flags.");
        }

        var migrated = existing.Value.IsLegacy || existing.Value.Missing;
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

    private async Task<ServiceResult<T>> GetJsonAsync<T>(
        string objectName,
        CancellationToken cancellationToken)
        where T : class
    {
        var bytes = await _objectStorage.GetBytesAsync(objectName, cancellationToken);
        if (!bytes.Succeeded || bytes.Value is null)
            return ServiceResult<T>.Fail(bytes.Error ?? $"GetObject {objectName} failed.");

        try
        {
            var doc = JsonSerializer.Deserialize<T>(bytes.Value, JsonOptions);
            if (doc is null)
                return ServiceResult<T>.Fail($"{objectName} is empty or invalid JSON.");
            return ServiceResult<T>.Ok(doc);
        }
        catch (JsonException ex)
        {
            return ServiceResult<T>.Fail($"{objectName} JSON parse failed: {ex.Message}");
        }
    }

    private async Task<ServiceResult> PutJsonAsync<T>(
        string objectName,
        T document,
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
    public string Notes { get; init; } = "";
}

public sealed class InfraMetaPublishResult
{
    public required InfraMetaDocument Document { get; init; }
    public required MetaFlagsDocument Flags { get; init; }
    public bool MigratedFromLegacy { get; init; }
    public required string Message { get; init; }
}
