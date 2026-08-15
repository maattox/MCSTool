using McManager.Core.Config;

namespace McManager.Core.Services;

public sealed class SshTarget
{
    public string Host { get; init; } = "";
    public string User { get; init; } = "ubuntu";
    public string KeyPath { get; init; } = "";
    public string Label { get; init; } = "ssh";

    public static SshTarget FromVm1(Vm1Settings vm1) =>
        new()
        {
            Host = vm1.SshHost,
            User = string.IsNullOrWhiteSpace(vm1.SshUser) ? "ubuntu" : vm1.SshUser,
            KeyPath = vm1.SshKeyPath,
            Label = "VM1",
        };

    public static SshTarget FromDoor(DoorSettings door) =>
        new()
        {
            Host = door.SshHost,
            User = string.IsNullOrWhiteSpace(door.SshUser) ? "ubuntu" : door.SshUser,
            KeyPath = door.SshKeyPath,
            Label = "door",
        };
}

public sealed class SshExecResult
{
    public bool Succeeded { get; init; }
    public int ExitStatus { get; init; }
    public string Output { get; init; } = "";
    public string? Error { get; init; }

    public static SshExecResult Ok(string output, int exitStatus = 0) =>
        new() { Succeeded = true, ExitStatus = exitStatus, Output = output };

    public static SshExecResult Fail(string error, string output = "", int exitStatus = -1) =>
        new()
        {
            Succeeded = false,
            ExitStatus = exitStatus,
            Output = output,
            Error = error,
        };

    public string Format()
    {
        var body = (Output ?? "").TrimEnd();
        if (Succeeded)
            return string.IsNullOrEmpty(body) ? "(no output)" : body;

        var err = string.IsNullOrWhiteSpace(Error) ? "" : Error.Trim();
        if (string.IsNullOrEmpty(body))
            return string.IsNullOrEmpty(err) ? $"failed (exit {ExitStatus})" : err;

        return string.IsNullOrEmpty(err) || body.Contains(err, StringComparison.Ordinal)
            ? body
            : body + Environment.NewLine + err;
    }
}

public static class SshShell
{
    public static string Quote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
