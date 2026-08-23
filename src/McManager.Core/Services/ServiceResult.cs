namespace McManager.Core.Services;

public sealed class ServiceResult<T>
{
    public bool Succeeded { get; private init; }
    public T? Value { get; private init; }
    public string? Error { get; private init; }

    public static ServiceResult<T> Ok(T value) =>
        new() { Succeeded = true, Value = value };

    public static ServiceResult<T> Fail(string error) =>
        new() { Succeeded = false, Error = error };
}

public sealed class ServiceResult
{
    public bool Succeeded { get; private init; }
    public string? Error { get; private init; }

    public static ServiceResult Ok(string? warning = null) =>
        new() { Succeeded = true, Warning = warning };

    public static ServiceResult Fail(string error) =>
        new() { Succeeded = false, Error = error };

    public string? Warning { get; private init; }
}
