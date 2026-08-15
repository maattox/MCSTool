using System.Diagnostics;
using System.Text;

namespace McManager.Core.Setup;

/// <summary>Runs local <c>tofu</c>. Callers must not invoke this from agent tests against a real tenancy.</summary>
public sealed class OpenTofuRunner : IOpenTofuRunner
{
    private readonly string _tofuPath;

    public OpenTofuRunner(string tofuPath) => _tofuPath = tofuPath;

    public Task<TofuCommandResult> InitAsync(
        string infraDirectory,
        IProgress<string>? log,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            infraDirectory,
            ["init", "-input=false", "-no-color"],
            log,
            cancellationToken);

    public Task<TofuCommandResult> ApplyAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            infraDirectory,
            [
                "apply",
                "-auto-approve",
                "-input=false",
                "-no-color",
                $"-state={workspace.StatePath}",
                $"-var-file={workspace.VarFilePath}",
            ],
            log,
            cancellationToken);

    public Task<TofuCommandResult> OutputJsonAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default) =>
        RunAsync(
            infraDirectory,
            ["output", "-json", $"-state={workspace.StatePath}", "-no-color"],
            log,
            cancellationToken);

    private async Task<TofuCommandResult> RunAsync(
        string infraDirectory,
        IReadOnlyList<string> args,
        IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(infraDirectory))
            return TofuCommandResult.Fail(1, $"infra directory not found: {infraDirectory}");

        var psi = new ProcessStartInfo
        {
            FileName = _tofuPath,
            WorkingDirectory = infraDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add($"-chdir={infraDirectory}");
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        log?.Report("$ tofu " + string.Join(" ", psi.ArgumentList.Skip(1)));

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var combined = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            combined.AppendLine(e.Data);
            log?.Report(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
                return;
            combined.AppendLine(e.Data);
            log?.Report(e.Data);
        };

        try
        {
            if (!process.Start())
                return TofuCommandResult.Fail(1, "Failed to start tofu.");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return new TofuCommandResult
            {
                ExitCode = process.ExitCode,
                Output = combined.ToString(),
            };
        }
        catch (Exception ex)
        {
            return TofuCommandResult.Fail(1, $"tofu failed: {ex.Message}\n{combined}");
        }
    }
}
