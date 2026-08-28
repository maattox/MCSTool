using McManager.Core.Config;
using McManager.Core.Services;

namespace McManager.Core.Setup;

public sealed class FunctionImagePublishResult
{
    public required string Image { get; init; }
    public bool Copied { get; init; }
}

public interface IFunctionImagePublisher
{
    Task<ServiceResult<FunctionImagePublishResult>> TryPublishAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default);
}
