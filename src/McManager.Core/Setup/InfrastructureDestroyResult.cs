namespace McManager.Core.Setup;

public readonly record struct DestroyProgressUpdate(int Percent, string Caption);

public sealed class InfrastructureDestroyResult
{
    public bool Succeeded { get; init; }
    public string Message { get; init; } = "";

    public static InfrastructureDestroyResult Ok(string message) =>
        new() { Succeeded = true, Message = message };

    public static InfrastructureDestroyResult Fail(string message) =>
        new() { Message = message };
}
