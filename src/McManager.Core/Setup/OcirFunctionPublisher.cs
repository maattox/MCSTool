using System.Diagnostics;
using System.Net.Http;
using System.Text.RegularExpressions;
using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

/// <summary>
/// Best-effort OCIR push for the $1 Function. Prefers a pre-built ARM tarball (no Docker).
/// Falls back to docker buildx. Never fails the whole Setup if Docker/token/artifact is missing.
/// </summary>
public sealed class OcirFunctionPublisher : IFunctionImagePublisher
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

    public Task<ServiceResult<FunctionImagePublishResult>> TryPublishAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default) =>
        TryPublishAsync(outputs, state, log, handler: null, cancellationToken);

    internal static async Task<ServiceResult<FunctionImagePublishResult>> TryPublishAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var artifact = FunctionImageArtifact.Find();
        if (ProductPaths.IsTofuDryRun())
        {
            var dry = DryRunMessage(artifact);
            log?.Report(dry);
            return ServiceResult<FunctionImagePublishResult>.Fail(dry);
        }

        if (!WindowsCredentialStore.TryRead(WindowsCredentialStore.OcirTarget, out var token)
            || string.IsNullOrWhiteSpace(token))
        {
            return ServiceResult<FunctionImagePublishResult>.Fail(
                $"No Auth Token in Windows Credential Manager ({WindowsCredentialStore.OcirTarget}). Function/Events stay skipped.");
        }

        var region = string.IsNullOrWhiteSpace(outputs.Region) ? state.OciRegion : outputs.Region;
        var ns = outputs.ObjectStorageNamespace;
        if (string.IsNullOrWhiteSpace(region) || string.IsNullOrWhiteSpace(ns))
        {
            return ServiceResult<FunctionImagePublishResult>.Fail(
                "Region or Object Storage namespace missing; cannot form OCIR image.");
        }

        var ocirUser = await ResolveLoginAsync(ns, state, log, cancellationToken).ConfigureAwait(false);
        if (!ocirUser.Succeeded || string.IsNullOrWhiteSpace(ocirUser.Value))
        {
            return ServiceResult<FunctionImagePublishResult>.Fail(
                ocirUser.Error
                ?? "Could not derive OCIR username. Function/Events stay skipped.");
        }

        var host = $"{region}.ocir.io";
        var image = $"{host}/{ns}/{ImageRepository}:{ImageTag}";
        log?.Report($"OCIR image: {image}");

        if (!string.IsNullOrWhiteSpace(artifact))
        {
            log?.Report("Using pre-built Function image (Docker not required): " + artifact);
            return await CopyPrebuiltAsync(
                artifact,
                host,
                ns,
                ocirUser.Value,
                token,
                image,
                log,
                handler,
                cancellationToken).ConfigureAwait(false);
        }

        var docker = FindOnPath("docker.exe") ?? FindOnPath("docker");
        var fn = FindOnPath("fn.exe") ?? FindOnPath("fn");
        if (docker is null)
        {
            return ServiceResult<FunctionImagePublishResult>.Fail(SkipNoArtifactNoDocker(fn is not null));
        }

        var funcDir = ProductPaths.FindFunctionDirectory();
        if (funcDir is null)
        {
            return ServiceResult<FunctionImagePublishResult>.Fail(
                "Product functions/shutdown_vm/ not found (expected OCI-mc-server/functions/shutdown_vm).");
        }

        var login = await RunAsync(
            docker,
            ["login", host, "-u", ocirUser.Value, "--password-stdin"],
            token,
            log,
            cancellationToken).ConfigureAwait(false);
        if (!login.Succeeded)
            return ServiceResult<FunctionImagePublishResult>.Fail("docker login failed: " + login.Output);

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
                return ServiceResult<FunctionImagePublishResult>.Fail("docker buildx push failed: " + build.Output);
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
        return ServiceResult<FunctionImagePublishResult>.Ok(new FunctionImagePublishResult { Image = image, Copied = true });
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

    private static async Task<ServiceResult<string>> ResolveLoginAsync(
        string ns,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var env = Environment.GetEnvironmentVariable(OcirUsername.EnvVar);
        if (!string.IsNullOrWhiteSpace(env))
        {
            log?.Report("OCIR login user: " + OcirUsername.EnvVar + " override.");
            return ServiceResult<string>.Ok(env.Trim());
        }

        var looked = await OcirUsernameLookup.LookupAsync(state, log, cancellationToken)
            .ConfigureAwait(false);
        if (!looked.Succeeded || looked.Value is null)
        {
            return ServiceResult<string>.Fail(
                looked.Error
                ?? "Could not resolve IAM user name for OCIR login. Function/Events stay skipped.");
        }

        var derived = OcirUsername.Derive(ns, looked.Value.IamUserName, looked.Value.IdentityDomain);
        if (derived.Succeeded)
        {
            log?.Report(
                string.IsNullOrWhiteSpace(looked.Value.IdentityDomain)
                    ? "OCIR login user derived as {namespace}/{IAM user name}."
                    : "OCIR login user derived as {namespace}/{identity-domain}/{IAM user name}.");
        }

        return derived;
    }

    internal static string SkipNoArtifactNoDocker(bool fnPresent) =>
        fnPresent
            ? "No pre-built Function image (" + FunctionImageArtifact.FileName
              + " next to the app or in artifacts/) and Docker was not found. "
              + "fn CLI is present, but without an artifact Setup still needs Docker buildx. Function/Events stay skipped."
            : "No pre-built Function image (" + FunctionImageArtifact.FileName
              + " next to the app or in artifacts/) and Docker was not found. Function/Events stay skipped.";

    private static async Task<ServiceResult<FunctionImagePublishResult>> CopyPrebuiltAsync(
        string artifactPath,
        string registryHost,
        string ns,
        string username,
        string password,
        string image,
        IProgress<string>? log,
        HttpMessageHandler? handler,
        CancellationToken cancellationToken)
    {
        var work = Path.Combine(Path.GetTempPath(), "mcmgr-fn-copy-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            Directory.CreateDirectory(work);
            var prepared = DockerArchiveFunctionImage.Prepare(artifactPath, work);
            if (!prepared.Succeeded || prepared.Value is null)
            {
                return ServiceResult<FunctionImagePublishResult>.Fail(
                    prepared.Error ?? "Failed to read pre-built Function image.");
            }

            var repository = ns.Trim() + "/" + ImageRepository;
            var bundled = prepared.Value.ManifestDigest;
            log?.Report("Bundled Function image digest " + bundled + ".");
            var live = await OcirRegistryPusher.TryGetManifestDigestAsync(
                registryHost,
                repository,
                ImageTag,
                username,
                password,
                log,
                handler,
                cancellationToken).ConfigureAwait(false);
            if (!live.Succeeded)
            {
                return ServiceResult<FunctionImagePublishResult>.Fail(
                    live.Error ?? "Could not read live Function image digest.");
            }

            if (!FunctionImageDigest.NeedsCopy(bundled, live.Value))
            {
                log?.Report("Live Function image digest matches the bundled tar; copy skipped.");
                return ServiceResult<FunctionImagePublishResult>.Ok(
                    new FunctionImagePublishResult { Image = image, Copied = false });
            }

            if (string.IsNullOrWhiteSpace(live.Value))
                log?.Report("No live Function image tag (missing or Function not created yet); copying.");
            else
                log?.Report("Live Function image digest differs; copying bundled tar.");

            var pushed = await OcirRegistryPusher.PushAsync(
                registryHost,
                repository,
                ImageTag,
                username,
                password,
                prepared.Value,
                log,
                handler,
                cancellationToken).ConfigureAwait(false);
            return pushed.Succeeded
                ? ServiceResult<FunctionImagePublishResult>.Ok(
                    new FunctionImagePublishResult { Image = image, Copied = true })
                : ServiceResult<FunctionImagePublishResult>.Fail(pushed.Error ?? "Pre-built Function image copy failed.");
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
