using System.Formats.Tar;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using McManager.Core.Setup;
using Xunit;

namespace McManager.Core.Tests;

public sealed class FunctionImageArtifactTests
{
    [Fact]
    public void Candidates_prefer_env_then_app_dir_then_repo_artifacts()
    {
        var paths = FunctionImageArtifact.ListCandidatePaths(
            envPath: @"C:\tmp\override.tar",
            appDirectory: @"C:\app",
            repoRoot: @"C:\repo");

        Assert.Equal(
            [
                @"C:\tmp\override.tar",
                @"C:\app\mcmgr-fn-softstop-linux-arm64.tar",
                @"C:\app\artifacts\mcmgr-fn-softstop-linux-arm64.tar",
                @"C:\repo\artifacts\mcmgr-fn-softstop-linux-arm64.tar",
            ],
            paths);
    }

    [Fact]
    public void FirstExisting_returns_the_first_real_file()
    {
        var dir = NewTempDir("fn-art");
        try
        {
            var missing = Path.Combine(dir, "missing.tar");
            var hit = Path.Combine(dir, "hit.tar");
            File.WriteAllText(hit, "x");
            var later = Path.Combine(dir, "later.tar");
            File.WriteAllText(later, "y");

            var found = FunctionImageArtifact.FirstExisting([missing, hit, later]);
            Assert.Equal(Path.GetFullPath(hit), found);
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void FirstExisting_returns_null_when_nothing_is_present()
    {
        Assert.Null(FunctionImageArtifact.FirstExisting(
        [
            Path.Combine(Path.GetTempPath(), "mcmgr-no-such-fn-image-" + Guid.NewGuid().ToString("N") + ".tar"),
        ]));
    }

    [Fact]
    public void Dry_run_copy_does_not_require_docker()
    {
        var withArtifact = OcirFunctionPublisher.DryRunMessage(@"C:\repo\artifacts\mcmgr-fn-softstop-linux-arm64.tar");
        Assert.Contains("would copy pre-built Function image", withArtifact, StringComparison.Ordinal);
        Assert.Contains("Docker not required", withArtifact, StringComparison.Ordinal);
        Assert.DoesNotContain("buildx", withArtifact, StringComparison.OrdinalIgnoreCase);

        var without = OcirFunctionPublisher.DryRunMessage(null);
        Assert.Contains("no pre-built artifact", without, StringComparison.Ordinal);
        Assert.Contains("docker buildx", without, StringComparison.Ordinal);
    }

    [Fact]
    public void Skip_without_artifact_or_docker_is_explicit()
    {
        var text = OcirFunctionPublisher.SkipNoArtifactNoDocker(fnPresent: false);
        Assert.Contains(FunctionImageArtifact.FileName, text, StringComparison.Ordinal);
        Assert.Contains("Docker was not found", text, StringComparison.Ordinal);
        Assert.Contains("Function/Events stay skipped", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_legacy_docker_archive_builds_a_v2_manifest()
    {
        var dir = NewTempDir("fn-legacy");
        try
        {
            var tar = Path.Combine(dir, "image.tar");
            WriteLegacyDockerArchive(tar);
            var prepared = DockerArchiveFunctionImage.Prepare(tar, Path.Combine(dir, "work"));
            Assert.True(prepared.Succeeded, prepared.Error);
            Assert.NotNull(prepared.Value);
            Assert.Equal(DockerArchiveFunctionImage.DockerManifestMediaType, prepared.Value.ManifestMediaType);
            Assert.Equal(2, prepared.Value.Blobs.Count);

            using var doc = JsonDocument.Parse(prepared.Value.ManifestJson);
            Assert.Equal(2, doc.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(1, doc.RootElement.GetProperty("layers").GetArrayLength());
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Prepare_oci_layout_reuses_existing_blobs()
    {
        var dir = NewTempDir("fn-oci");
        try
        {
            var tar = Path.Combine(dir, "image.tar");
            WriteOciLayoutArchive(tar);
            var prepared = DockerArchiveFunctionImage.Prepare(tar, Path.Combine(dir, "work"));
            Assert.True(prepared.Succeeded, prepared.Error);
            Assert.NotNull(prepared.Value);
            Assert.Equal(DockerArchiveFunctionImage.OciManifestMediaType, prepared.Value.ManifestMediaType);
            Assert.Equal(2, prepared.Value.Blobs.Count);

            using var doc = JsonDocument.Parse(prepared.Value.ManifestJson);
            Assert.Equal("application/vnd.oci.image.manifest.v1+json", doc.RootElement.GetProperty("mediaType").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("layers").GetArrayLength());
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public async Task Registry_pusher_uploads_blobs_and_manifest_without_docker()
    {
        var dir = NewTempDir("fn-push");
        try
        {
            var tar = Path.Combine(dir, "image.tar");
            WriteLegacyDockerArchive(tar);
            var prepared = DockerArchiveFunctionImage.Prepare(tar, Path.Combine(dir, "work"));
            Assert.True(prepared.Succeeded, prepared.Error);

            var handler = new ScriptedRegistryHandler();
            var result = await OcirRegistryPusher.PushAsync(
                "sjc.ocir.io",
                "examplens/mcmgr-fn/softstop",
                "setup",
                "examplens/user",
                "token-not-a-secret",
                prepared.Value!,
                log: null,
                handler,
                CancellationToken.None);

            Assert.True(result.Succeeded, result.Error);
            Assert.Contains(handler.Methods, m => m == "PUT" && handler.Uris.Any(u => u.Contains("/manifests/setup", StringComparison.Ordinal)));
            Assert.DoesNotContain(handler.Uris, u => u.Contains("docker", StringComparison.OrdinalIgnoreCase) && u.Contains("buildx", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDir(dir);
        }
    }

    [Fact]
    public void Bearer_challenge_parses_ocir_realm()
    {
        var ok = OcirRegistryPusher.TryParseBearerChallenge(
            @"Bearer realm=""https://sjc.ocir.io/20180419/docker/token"",service=""sjc.ocir.io""",
            out var realm,
            out var service);
        Assert.True(ok);
        Assert.Equal("https://sjc.ocir.io/20180419/docker/token", realm);
        Assert.Equal("sjc.ocir.io", service);
    }

    private static void WriteLegacyDockerArchive(string tarPath)
    {
        var staging = Path.Combine(Path.GetDirectoryName(tarPath)!, "legacy-src");
        Directory.CreateDirectory(staging);
        var layerDir = Path.Combine(staging, "layer0");
        Directory.CreateDirectory(layerDir);
        var layerTar = Path.Combine(layerDir, "layer.tar");
        using (var layerStream = File.Create(layerTar))
        using (var writer = new TarWriter(layerStream, TarEntryFormat.Gnu, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "hello.txt")
            {
                DataStream = new MemoryStream("hi"u8.ToArray()),
            };
            writer.WriteEntry(entry);
        }

        var config = new JsonObject
        {
            ["architecture"] = "arm64",
            ["os"] = "linux",
            ["rootfs"] = new JsonObject
            {
                ["type"] = "layers",
                ["diff_ids"] = new JsonArray("sha256:" + DockerArchiveFunctionImage.Sha256FileHex(layerTar)),
            },
        };
        var configName = "config.json";
        File.WriteAllBytes(Path.Combine(staging, configName), JsonSerializer.SerializeToUtf8Bytes(config));

        var manifest = new JsonArray
        {
            new JsonObject
            {
                ["Config"] = configName,
                ["RepoTags"] = new JsonArray("mcmgr-fn/softstop:setup"),
                ["Layers"] = new JsonArray("layer0/layer.tar"),
            },
        };
        File.WriteAllBytes(Path.Combine(staging, "manifest.json"), JsonSerializer.SerializeToUtf8Bytes(manifest));

        using var tar = File.Create(tarPath);
        using var tarWriter = new TarWriter(tar, TarEntryFormat.Gnu, leaveOpen: false);
        WriteTree(tarWriter, staging, staging);
    }

    private static void WriteOciLayoutArchive(string tarPath)
    {
        var staging = Path.Combine(Path.GetDirectoryName(tarPath)!, "oci-src");
        var blobDir = Path.Combine(staging, "blobs", "sha256");
        Directory.CreateDirectory(blobDir);

        var layerTar = Path.Combine(staging, "layer.tar");
        using (var layerStream = File.Create(layerTar))
        using (var writer = new TarWriter(layerStream, TarEntryFormat.Gnu, leaveOpen: false))
        {
            var entry = new PaxTarEntry(TarEntryType.RegularFile, "hello.txt")
            {
                DataStream = new MemoryStream("hi"u8.ToArray()),
            };
            writer.WriteEntry(entry);
        }

        var layerGz = Path.Combine(staging, "layer.tar.gz");
        DockerArchiveFunctionImage.GzipFile(layerTar, layerGz);
        var layerDigest = "sha256:" + DockerArchiveFunctionImage.Sha256FileHex(layerGz);
        File.Copy(layerGz, Path.Combine(blobDir, layerDigest[7..]), overwrite: true);

        var configBytes = JsonSerializer.SerializeToUtf8Bytes(new JsonObject
        {
            ["architecture"] = "arm64",
            ["os"] = "linux",
            ["rootfs"] = new JsonObject
            {
                ["type"] = "layers",
                ["diff_ids"] = new JsonArray("sha256:" + DockerArchiveFunctionImage.Sha256FileHex(layerTar)),
            },
        });
        var configDigest = "sha256:" + DockerArchiveFunctionImage.Sha256Hex(configBytes);
        File.WriteAllBytes(Path.Combine(blobDir, configDigest[7..]), configBytes);

        var imageManifest = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = DockerArchiveFunctionImage.OciManifestMediaType,
            ["config"] = new JsonObject
            {
                ["mediaType"] = "application/vnd.oci.image.config.v1+json",
                ["digest"] = configDigest,
                ["size"] = configBytes.LongLength,
            },
            ["layers"] = new JsonArray
            {
                new JsonObject
                {
                    ["mediaType"] = "application/vnd.oci.image.layer.v1.tar+gzip",
                    ["digest"] = layerDigest,
                    ["size"] = new FileInfo(layerGz).Length,
                },
            },
        };
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(imageManifest);
        var manifestDigest = "sha256:" + DockerArchiveFunctionImage.Sha256Hex(manifestBytes);
        File.WriteAllBytes(Path.Combine(blobDir, manifestDigest[7..]), manifestBytes);

        File.WriteAllText(Path.Combine(staging, "oci-layout"), """{"imageLayoutVersion":"1.0.0"}""");
        var index = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["mediaType"] = "application/vnd.oci.image.index.v1+json",
            ["manifests"] = new JsonArray
            {
                new JsonObject
                {
                    ["mediaType"] = DockerArchiveFunctionImage.OciManifestMediaType,
                    ["digest"] = manifestDigest,
                    ["size"] = manifestBytes.LongLength,
                    ["annotations"] = new JsonObject
                    {
                        ["org.opencontainers.image.ref.name"] = "setup",
                    },
                },
            },
        };
        File.WriteAllBytes(Path.Combine(staging, "index.json"), JsonSerializer.SerializeToUtf8Bytes(index));

        using var tar = File.Create(tarPath);
        using var tarWriter = new TarWriter(tar, TarEntryFormat.Gnu, leaveOpen: false);
        WriteTree(tarWriter, staging, staging);
    }

    private static void WriteTree(TarWriter tarWriter, string root, string current)
    {
        foreach (var dir in Directory.GetDirectories(current))
            WriteTree(tarWriter, root, dir);

        foreach (var file in Directory.GetFiles(current))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
            tarWriter.WriteEntry(file, rel);
        }
    }

    private static string NewTempDir(string prefix)
    {
        var dir = Path.Combine(Path.GetTempPath(), "mcmgr-" + prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void TryDeleteDir(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    private sealed class ScriptedRegistryHandler : HttpMessageHandler
    {
        public List<string> Methods { get; } = [];
        public List<string> Uris { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Methods.Add(request.Method.Method);
            var uri = request.RequestUri?.ToString() ?? "";
            Uris.Add(uri);

            if (request.Method == HttpMethod.Get && uri.EndsWith("/v2/", StringComparison.Ordinal))
                return Task.FromResult(Ok());

            if (request.Method == HttpMethod.Head && uri.Contains("/blobs/", StringComparison.Ordinal))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            if (request.Method == HttpMethod.Post && uri.Contains("/blobs/uploads/", StringComparison.Ordinal))
            {
                var resp = new HttpResponseMessage(HttpStatusCode.Accepted);
                resp.Headers.Location = new Uri("https://sjc.ocir.io/v2/examplens/mcmgr-fn/softstop/blobs/uploads/uuid-1");
                return Task.FromResult(resp);
            }

            if (request.Method == HttpMethod.Put)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Created));

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("unexpected " + request.Method + " " + uri),
            });
        }

        private static HttpResponseMessage Ok()
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new StringContent("{}");
            resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return resp;
        }
    }
}
