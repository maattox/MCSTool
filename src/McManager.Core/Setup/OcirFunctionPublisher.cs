using System.Diagnostics;
using System.Text.RegularExpressions;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Best-effort OCIR push for the $1 Function. Prefers a pre-built ARM tarball (no Docker).
/// Falls back to docker buildx. Never fails the whole Setup if Docker/token/artifact is missing.
/// </summary>
public static class OcirFunctionPublisher
{
    public const string ImageRepository = "mcmgr-fn/softstop";
    public const string ImageTag = "setup";

    private const string Dockerfile = """
        FROM fnproject/python:3.12-dev AS build
        WORKDIR /function
        ADD requirements.txt /function/
        RUN pip3 install --target /python/ --no-cache --no-cache-dir -r requirements.txt && \
            rm -fr ~/.cache/pip /tmp* && chmod -R o+r /python
        ADD . /function/

        FROM fnproject/python:3.12
        WORKDIR /function
        COPY --from=build /python /python
        COPY --from=build /function /function
        RUN chmod -R o+r /function
        ENV PYTHONPATH=/function:/python
        ENTRYPOINT ["/python/bin/fdk", "/function/func.py", "handler"]
        """;

    public static async Task<ServiceResult<string>> TryPushAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var artifact = FunctionImageArtifact.Find();
        if (ProductPaths.IsTofuDryRun())
        {
            var dry = DryRunMessage(artifact);
            log?.Report(dry);
            return ServiceResult<string>.Fail(dry);
        }

        if (!WindowsCredentialStore.TryRead(WindowsCredentialStore.OcirTarget, out var token)
            || string.IsNullOrWhiteSpace(token))
        {
            return ServiceResult<string>.Fail(
                "No Auth Token in Windows Credential Manager (McManager/ocir). Function/Events stay skipped.");
        }

        var region = string.IsNullOrWhiteSpace(outputs.Region) ? state.OciRegion : outputs.Region;
        var ns = outputs.ObjectStorageNamespace;
        if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(ns))
            return ServiceResult<string>.Fail("Region or Object Storage namespace missing; cannot form OCIR image.");

        var ocirUser = Environment.GetEnvironmentVariable("MCMANAGER_OCIR_USERNAME");
        if (string.IsNullOrWhiteSpace(ocirUser))
        {
            return ServiceResult<string>.Fail(
                "Set MCMANAGER_OCIR_USERNAME to <namespace>/<username> for OCIR login. "
                + "Function/Events stay skipped. (Deriving this from the OCI user is V1 Step 8.6.1.)");
        }

        var host = $"{region}.ocir.io";
        var image = $"{host}/{ns}/{ImageRepository}:{ImageTag}";
        log?.Report($"OCIR image: {image}");

        if (!string.IsNullOrWhiteSpace(artifact))
        {
            log?.Report("Using pre-built Function image (Docker not required): " + artifact);
            var copied = await CopyPrebuiltAsync(
                artifact,
                host,
                ns,
                ocirUser,
                token,
                log,
                cancellationToken).ConfigureAwait(false);
            return copied.Succeeded
                ? ServiceResult<string>.Ok(image)
                : ServiceResult<string>.Fail(copied.Error ?? "Pre-built Function image copy failed.");
        }

        var docker = FindOnPath("docker.exe") ?? FindOnPath("docker");
        var fn = FindOnPath("fn.exe") ?? FindOnPath("fn");
        if (docker is null)
        {
            return ServiceResult<string>.Fail(SkipNoArtifactNoDocker(fn is not null));
        }

        var funcDir = ProductPaths.FindFunctionDirectory();
        if (funcDir is null)
        {
            return ServiceResult<string>.Fail(
                "Product functions/shutdown_vm/ not found (expected OCI-mc-server/functions/shutdown_vm).");
        }

        var login = await RunAsync(
            docker,
            ["login", host, "-u", ocirUser, "--password-stdin"],
            token,
            log,
            cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded)
            return ServiceResult<string>.Fail("docker login failed: " + login.Output);

        var staging = Path.Combine(Path.GetTempPath(), "mcmgr-fn-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            Directory.CreateDirectory(staging);
            StageFunctionSources(funcDir, staging);

            var build = await RunAsync(
                docker,
                ["buildx", "build", "--platform", "linux/arm64", "--provenance=false", "--sbom=false", "-t", image, "--push", staging],
                stdin: null,
                log,
                cancellationToken).ConfigureAwait(false);
            if (!build.Succeeded)
                return ServiceResult<string>.Fail("docker buildx push failed: " + build.Output);
        }
        finally
        {
            try
            {
                if (Directory.Exists(staging))
                    Directory.Delete(staging, recursive: true);
            }
            catch
            {
                // temp cleanup is best-effort
            }
        }

        log?.Report("Pushed Function image.");
        return ServiceResult<string>.Ok(image);
    }

    internal static void StageFunctionSources(string funcDir, string staging)
    {
        foreach (var name in new[] { "func.py", "requirements.txt", "func.yaml" })
        {
            var src = Path.Combine(funcDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(staging, name), overwrite: true);
        }

        var pyPath = Path.Combine(staging, "func.py");
        if (File.Exists(pyPath))
        {
            var py = File.ReadAllText(pyPath);
            py = Regex.Replace(
                py,
                @"INSTANCE_OCIDS\s*=\s*\[[\s\S]*?\]",
                "INSTANCE_OCIDS = [x.strip() for x in __import__('os').environ.get('INSTANCE_OCIDS', '').split(',') if x.strip() and not x.strip().startswith('<')]",
                RegexOptions.Multiline);
            File.WriteAllText(pyPath, py.Replace("\r\n", "\n"));
        }

        File.WriteAllText(Path.Combine(staging, "Dockerfile"), Dockerfile.Replace("\r\n", "\n"));
    }

    internal static string DryRunMessage(string? artifactPath) =>
        string.IsNullOrWhiteSpace(artifactPath)
            ? "[dry-run] OCIR push skipped (no pre-built artifact; would docker buildx)."
            : "[dry-run] would copy pre-built Function image into OCIR (Docker not required): " + artifactPath;

    internal static string SkipNoArtifactNoDocker(bool fnPresent) =>
        fnPresent
            ? "No pre-built Function image (" + FunctionImageArtifact.FileName
              + " next to the app or in artifacts/) and Docker was not found. "
              + "fn CLI is present, but without an artifact Setup still needs Docker buildx. Function/Events stay skipped."
            : "No pre-built Function image (" + FunctionImageArtifact.FileName
              + " next to the app or in artifacts/) and Docker was not found. Function/Events stay skipped.";

    private static async Task<ServiceResult> CopyPrebuiltAsync(
        string artifactPath,
        string registryHost,
        string ns,
        string username,
        string password,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var work = Path.Combine(Path.GetTempPath(), "mcmgr-fn-copy-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            Directory.CreateDirectory(work);
            var prepared = DockerArchiveFunctionImage.Prepare(artifactPath, work);
            if (!prepared.Succeeded || prepared.Value is null)
                return ServiceResult.Fail(prepared.Error ?? "Failed to read pre-built Function image.");

            var repository = ns.Trim() + "/" + ImageRepository;
            return await OcirRegistryPusher.PushAsync(
                registryHost,
                repository,
                ImageTag,
                username,
                password,
                prepared.Value,
                log,
                handler: null,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                if (Directory.Exists(work))
                    Directory.Delete(work, recursive: true);
            }
            catch
            {
                // temp cleanup is best-effort
            }
        }
    }

    private static async Task<TofuCommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> args,
        string? stdin,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        log?.Report("$ " + fileName + " " + string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a)));

        try
        {
            using var p = Process.Start(psi);
            if (p is null)
                return TofuCommandResult.Fail(1, "Failed to start " + fileName);
            if (stdin is not null)
            {
                await p.StandardInput.WriteLineAsync(stdin).ConfigureAwait(false);
                p.StandardInput.Close();
            }

            var stdout = await p.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            var stderr = await p.StandardError.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await p.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var combined = (stdout + "\n" + stderr).Trim();
            if (!string.IsNullOrWhiteSpace(combined))
                log?.Report(combined);
            return p.ExitCode == 0
                ? TofuCommandResult.Ok(combined)
                : TofuCommandResult.Fail(p.ExitCode, combined);
        }
        catch (Exception ex)
        {
            return TofuCommandResult.Fail(1, ex.Message);
        }
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir.Trim().Trim('"'), fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }
}
