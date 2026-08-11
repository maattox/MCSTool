using McManager.Core.Config;
using Renci.SshNet;

namespace McManager.Core.Services;

public interface ISshService
{
    Task<ServiceResult> RestartMinecraftAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default);
}

public sealed class SshService : ISshService
{
    public Task<ServiceResult> RestartMinecraftAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RestartMinecraft(vm1), cancellationToken);

    private static ServiceResult RestartMinecraft(Vm1Settings vm1)
    {
        if (string.IsNullOrWhiteSpace(vm1.SshHost))
            return ServiceResult.Fail("vm1.ssh_host is empty.");

        var keyPath = LocalConfigStore.ExpandPath(vm1.SshKeyPath);
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
            return ServiceResult.Fail($"SSH key not found: {keyPath}");

        var user = string.IsNullOrWhiteSpace(vm1.SshUser) ? "ubuntu" : vm1.SshUser;
        var unit = string.IsNullOrWhiteSpace(vm1.MinecraftUnit) ? "minecraft" : vm1.MinecraftUnit.Trim();

        try
        {
            using var keyFile = new PrivateKeyFile(keyPath);
            using var client = new SshClient(vm1.SshHost, user, keyFile);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(20);
            client.Connect();

            // Quote unit name safely for shell; unit is from local config (trusted operator).
            var cmd = client.RunCommand($"sudo systemctl restart {EscapeShellArg(unit)}");
            if (cmd.ExitStatus != 0)
            {
                var err = string.IsNullOrWhiteSpace(cmd.Error) ? cmd.Result : cmd.Error;
                return ServiceResult.Fail($"systemctl restart {unit} failed (exit {cmd.ExitStatus}): {err.Trim()}");
            }

            var active = client.RunCommand($"systemctl is-active {EscapeShellArg(unit)}");
            var state = (active.Result ?? "").Trim();
            if (!string.Equals(state, "active", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(state, "activating", StringComparison.OrdinalIgnoreCase))
            {
                return ServiceResult.Fail($"Minecraft unit is '{state}' after restart (expected active).");
            }

            return ServiceResult.Ok();
        }
        catch (Exception ex)
        {
            return ServiceResult.Fail($"SSH restart failed: {ex.Message}");
        }
    }

    private static string EscapeShellArg(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
