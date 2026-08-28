namespace McManager.Core.Setup;

/// <summary>
/// Records tofu argv and returns canned success. Never starts <c>tofu</c> — dry-run / tests only.
/// </summary>
public sealed class RecordingOpenTofuRunner : IOpenTofuRunner
{
    public List<string> Commands { get; } = [];

    public string CannedOutputJson { get; set; } = TofuApplyOutputs.CannedDryRunJson;

    public Task<TofuCommandResult> InitAsync(
        string infraDirectory,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        Commands.Add("init");
        log?.Report("[dry-run] tofu init (no process, no OCI)");
        return Task.FromResult(TofuCommandResult.Ok("OpenTofu initialized (dry-run)."));
    }

    public Task<TofuCommandResult> ApplyAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        Commands.Add("apply");
        log?.Report("[dry-run] tofu apply skipped — no cloud resources created.");
        return Task.FromResult(TofuCommandResult.Ok("Apply skipped (RecordingOpenTofuRunner)."));
    }

    public Task<TofuCommandResult> OutputJsonAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        Commands.Add("output");
        log?.Report("[dry-run] tofu output -json (canned)");
        File.WriteAllText(workspace.OutputsPath, CannedOutputJson);
        return Task.FromResult(TofuCommandResult.Ok(CannedOutputJson));
    }

    public Task<TofuCommandResult> PlanDestroyAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        Commands.Add("plan-destroy");
        log?.Report("[dry-run] tofu plan -destroy skipped — no OCI.");
        return Task.FromResult(TofuCommandResult.Ok("Plan: 0 to add, 0 to change, 12 to destroy."));
    }

    public Task<TofuCommandResult> DestroyAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        Commands.Add("destroy");
        log?.Report("[dry-run] tofu destroy skipped — no cloud resources deleted.");
        return Task.FromResult(TofuCommandResult.Ok("Destroy complete! Resources: 0 destroyed."));
    }
}
