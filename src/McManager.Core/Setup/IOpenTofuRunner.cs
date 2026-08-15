namespace McManager.Core.Setup;

public sealed class TofuCommandResult
{
    public int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public bool Succeeded => ExitCode == 0;
    public bool IsCapacityError => CapacityErrors.IsCapacityFailure(Output);

    public static TofuCommandResult Ok(string output = "") =>
        new() { ExitCode = 0, Output = output };

    public static TofuCommandResult Fail(int exitCode, string output) =>
        new() { ExitCode = exitCode, Output = output };
}

public static class CapacityErrors
{
    public static bool IsCapacityFailure(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        return text.Contains("OutOfCapacity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Out of host capacity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("out of capacity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("InsufficientCapacity", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Cannot launch instance due to failure in capacity", StringComparison.OrdinalIgnoreCase);
    }
}

public interface IOpenTofuRunner
{
    Task<TofuCommandResult> InitAsync(
        string infraDirectory,
        IProgress<string>? log,
        CancellationToken cancellationToken = default);

    Task<TofuCommandResult> ApplyAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default);

    Task<TofuCommandResult> OutputJsonAsync(
        string infraDirectory,
        TofuWorkspace workspace,
        IProgress<string>? log,
        CancellationToken cancellationToken = default);
}
