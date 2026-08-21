using System.Formats.Tar;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>Turns a <c>docker save</c> tarball into registry blobs + an image manifest (no Docker daemon).</summary>
public static class DockerArchiveFunctionImage
{
    public const string DockerManifestMediaType = "application/vnd.docker.distribution.manifest.v2+json";
    public const string OciManifestMediaType = "application/vnd.oci.image.manifest.v1+json";
    public const string DockerConfigMediaType = "application/vnd.docker.container.image.v1+json";
    public const string DockerLayerMediaType = "application/vnd.docker.image.rootfs.diff.tar.gzip";

    public static ServiceResult<PreparedFunctionImage> Prepare(string archivePath, string workDirectory)
    {
        if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
            return ServiceResult<PreparedFunctionImage>.Fail("Function image archive not found: " + archivePath);

        Directory.CreateDirectory(workDirectory);
        var extractDir = Path.Combine(workDirectory, "extract");
        Directory.CreateDirectory(extractDir);
        try
        {
            ExtractTar(archivePath, extractDir);
        }
        catch (Exception ex)
        {
            return ServiceResult<PreparedFunctionImage>.Fail("Failed to read Function image tar: " + ex.Message);
        }

        var ociLayout = Path.Combine(extractDir, "oci-layout");
        var indexPath = Path.Combine(extractDir, "index.json");
        if (File.Exists(ociLayout) && File.Exists(indexPath))
            return PrepareOci(extractDir, indexPath);

        var manifestPath = Path.Combine(extractDir, "manifest.json");
        if (File.Exists(manifestPath))
            return PrepareLegacy(extractDir, workDirectory, manifestPath);

        return ServiceResult<PreparedFunctionImage>.Fail(
            "Function image tar is not a docker-archive or OCI layout (missing manifest.json / index.json).");
    }

    internal static void ExtractTar(string archivePath, string dest)
    {
        using var fs = File.OpenRead(archivePath);
        using var reader = new TarReader(fs);
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
        {
            var name = entry.Name.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(name) || name.Contains("..", StringComparison.Ordinal))
                continue;

            var destPath = Path.Combine(dest, name.Replace('/', Path.DirectorySeparatorChar));
            if (entry.EntryType is TarEntryType.Directory)
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            if (entry.DataStream is null)
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using var outFile = File.Create(destPath);
            entry.DataStream.CopyTo(outFile);
        }
    }

    private static ServiceResult<PreparedFunctionImage> PrepareOci(string extractDir, string indexPath)
    {
        JsonNode? index;
        try
        {
            index = JsonNode.Parse(File.ReadAllText(indexPath));
        }
        catch (Exception ex)
        {
            return ServiceResult<PreparedFunctionImage>.Fail("Invalid OCI index.json: " + ex.Message);
        }

        var chosen = PickArm64OrFirst(index?["manifests"] as JsonArray);
        var digest = chosen?["digest"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(digest) || !digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            return ServiceResult<PreparedFunctionImage>.Fail("OCI index.json has no image manifest digest.");

        var manifestPath = BlobPath(extractDir, digest);
        if (!File.Exists(manifestPath))
            return ServiceResult<PreparedFunctionImage>.Fail("OCI image manifest blob missing: " + digest);

        JsonNode? imageManifest;
        try
        {
            imageManifest = JsonNode.Parse(File.ReadAllBytes(manifestPath));
        }
        catch (Exception ex)
        {
            return ServiceResult<PreparedFunctionImage>.Fail("Invalid OCI image manifest: " + ex.Message);
        }

        if (imageManifest is null)
            return ServiceResult<PreparedFunctionImage>.Fail("Empty OCI image manifest.");

        var mediaType = imageManifest["mediaType"]?.GetValue<string>()
            ?? chosen?["mediaType"]?.GetValue<string>()
            ?? OciManifestMediaType;

        var blobs = new List<PreparedFunctionBlob>();
        var missing = CollectDescriptorBlobs(extractDir, imageManifest["config"], blobs)
            ?? CollectDescriptorBlobs(extractDir, imageManifest["layers"] as JsonArray, blobs);
        if (missing is not null)
            return ServiceResult<PreparedFunctionImage>.Fail(missing);

        var manifestBytes = File.ReadAllBytes(manifestPath);
        return ServiceResult<PreparedFunctionImage>.Ok(new PreparedFunctionImage
        {
            ManifestJson = manifestBytes,
            ManifestMediaType = mediaType,
            Blobs = blobs,
        });
    }

    private static ServiceResult<PreparedFunctionImage> PrepareLegacy(string extractDir, string workDirectory, string manifestPath)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(File.ReadAllText(manifestPath));
        }
        catch (Exception ex)
        {
            return ServiceResult<PreparedFunctionImage>.Fail("Invalid docker-archive manifest.json: " + ex.Message);
        }

        var first = (root as JsonArray)?[0] as JsonObject;
        var configRel = first?["Config"]?.GetValue<string>();
        var layers = first?["Layers"] as JsonArray;
        if (string.IsNullOrWhiteSpace(configRel) || layers is null || layers.Count == 0)
            return ServiceResult<PreparedFunctionImage>.Fail("docker-archive manifest.json is missing Config or Layers.");

        var configPath = Path.Combine(extractDir, configRel.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(configPath))
            return ServiceResult<PreparedFunctionImage>.Fail("docker-archive config blob missing: " + configRel);

        var blobsDir = Path.Combine(workDirectory, "blobs");
        Directory.CreateDirectory(blobsDir);

        var blobs = new List<PreparedFunctionBlob>();
        var configBytes = File.ReadAllBytes(configPath);
        var configDigest = "sha256:" + Sha256Hex(configBytes);
        var configOut = Path.Combine(blobsDir, configDigest[7..]);
        File.WriteAllBytes(configOut, configBytes);
        blobs.Add(new PreparedFunctionBlob
        {
            Digest = configDigest,
            FilePath = configOut,
            Size = configBytes.LongLength,
            MediaType = DockerConfigMediaType,
        });

        var layerDescriptors = new JsonArray();
        var i = 0;
        foreach (var layerNode in layers)
        {
            var rel = layerNode?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(rel))
                return ServiceResult<PreparedFunctionImage>.Fail("docker-archive layer path is empty.");

            var layerPath = Path.Combine(extractDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(layerPath))
                return ServiceResult<PreparedFunctionImage>.Fail("docker-archive layer missing: " + rel);

            var gzPath = Path.Combine(blobsDir, "layer-" + i + ".tar.gz");
            GzipFile(layerPath, gzPath);
            var gzBytesLen = new FileInfo(gzPath).Length;
            var digest = "sha256:" + Sha256FileHex(gzPath);
            var hashed = Path.Combine(blobsDir, digest[7..]);
            if (!string.Equals(gzPath, hashed, StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(gzPath, hashed, overwrite: true);
                File.Delete(gzPath);
            }

            blobs.Add(new PreparedFunctionBlob
            {
                Digest = digest,
                FilePath = hashed,
                Size = gzBytesLen,
                MediaType = DockerLayerMediaType,
            });
            layerDescriptors.Add(new JsonObject
            {
                ["mediaType"] = DockerLayerMediaType,
                ["size"] = gzBytesLen,
                ["digest"] = digest,
            });
            i++;
        }

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = DockerManifestMediaType,
            ["config"] = new JsonObject
            {
                ["mediaType"] = DockerConfigMediaType,
                ["size"] = configBytes.LongLength,
                ["digest"] = configDigest,
            },
            ["layers"] = layerDescriptors,
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);

        return ServiceResult<PreparedFunctionImage>.Ok(new PreparedFunctionImage
        {
            ManifestJson = manifestBytes,
            ManifestMediaType = DockerManifestMediaType,
            Blobs = blobs,
        });
    }

    private static JsonNode? PickArm64OrFirst(JsonArray? manifests)
    {
        if (manifests is null || manifests.Count == 0)
            return null;

        foreach (var item in manifests)
        {
            var arch = item?["platform"]?["architecture"]?.GetValue<string>();
            var os = item?["platform"]?["os"]?.GetValue<string>();
            if (string.Equals(os, "linux", StringComparison.OrdinalIgnoreCase)
                && (string.Equals(arch, "arm64", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(arch, "aarch64", StringComparison.OrdinalIgnoreCase)))
            {
                return item;
            }
        }

        return manifests[0];
    }

    private static string? CollectDescriptorBlobs(string extractDir, JsonNode? node, List<PreparedFunctionBlob> blobs)
    {
        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var err = CollectDescriptorBlobs(extractDir, item, blobs);
                if (err is not null)
                    return err;
            }

            return null;
        }

        if (node is not JsonObject)
            return null;

        var digest = node["digest"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(digest))
            return null;

        var path = BlobPath(extractDir, digest);
        if (!File.Exists(path))
            return "OCI blob missing: " + digest;

        blobs.Add(new PreparedFunctionBlob
        {
            Digest = digest,
            FilePath = path,
            Size = new FileInfo(path).Length,
            MediaType = node["mediaType"]?.GetValue<string>() ?? "application/octet-stream",
        });
        return null;
    }

    private static string BlobPath(string extractDir, string digest)
    {
        var hex = digest.Contains(':', StringComparison.Ordinal)
            ? digest[(digest.IndexOf(':') + 1)..]
            : digest;
        return Path.Combine(extractDir, "blobs", "sha256", hex);
    }

    internal static void GzipFile(string sourcePath, string destPath)
    {
        using var input = File.OpenRead(sourcePath);
        using var output = File.Create(destPath);
        using var gzip = new GZipStream(output, CompressionLevel.SmallestSize);
        input.CopyTo(gzip);
    }

    internal static string Sha256Hex(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();

    internal static string Sha256FileHex(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}

public sealed class PreparedFunctionImage
{
    public required byte[] ManifestJson { get; init; }
    public required string ManifestMediaType { get; init; }
    public required IReadOnlyList<PreparedFunctionBlob> Blobs { get; init; }
}

public sealed class PreparedFunctionBlob
{
    public required string Digest { get; init; }
    public required string FilePath { get; init; }
    public required long Size { get; init; }
    public required string MediaType { get; init; }
}
