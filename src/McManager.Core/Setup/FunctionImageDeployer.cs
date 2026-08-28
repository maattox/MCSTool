using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

public sealed class FunctionImageStageResult
{
    public string? SkipReason { get; init; }
    public bool Copied { get; init; }
    public bool Applied { get; init; }
}

/// <summary>
/// Spend-brake Function copy + optional second tofu apply. Runs on first Deploy and on
/// repair even when <c>apply_stage</c> is already <c>function</c> / <c>config_written</c>
/// if a bundled tar exists and the live digest differs.
/// </summary>
public static class FunctionImageDeployer
{
    public static bool ShouldAttempt(string? applyStage, string? artifactPath) =>
        !string.IsNullOrWhiteSpace(artifactPath)
        || !SetupApplyStage.Reached(applyStage, SetupApplyStage.Function);

    public static async Task<FunctionImageStageResult> RunAsync(
        IFunctionImagePublisher publisher,
        IOpenTofuRunner tofu,
        string infraDirectory,
        TofuWorkspace workspace,
        SetupWizardState state,
        TofuApplyOutputs outputs,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var push = await publisher.TryPublishAsync(outputs, state, log, cancellationToken)
            .ConfigureAwait(false);
        if (!push.Succeeded || push.Value is null || string.IsNullOrWhiteSpace(push.Value.Image))
        {
            var reason = push.Error ?? "Function image skipped.";
            log?.Report(reason);
            return new FunctionImageStageResult { SkipReason = reason };
        }

        if (!string.IsNullOrWhiteSpace(push.Value.Image))
            state.FunctionImage = push.Value.Image;

        if (!push.Value.Copied)
        {
            log?.Report("Spend-brake Function image already matches the bundled digest.");
            return new FunctionImageStageResult { Copied = false };
        }

        var rewrite = TfvarsWriter.Write(workspace, state, push.Value.Image);
        if (!rewrite.Succeeded)
            return new FunctionImageStageResult { SkipReason = rewrite.Error ?? "tfvars rewrite failed.", Copied = true };

        var apply2 = await tofu.ApplyAsync(infraDirectory, workspace, log, cancellationToken)
            .ConfigureAwait(false);
        if (!apply2.Succeeded)
        {
            var reason = "Function image pushed but second tofu apply failed: " + apply2.Output;
            log?.Report(reason);
            return new FunctionImageStageResult { SkipReason = reason, Copied = true };
        }

        return new FunctionImageStageResult { Copied = true, Applied = true };
    }
}
