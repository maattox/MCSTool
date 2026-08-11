using McManager.Core.Config;
using Renci.SshNet;

namespace McManager.Core.Services;

public interface ISshService
{
    Task<ServiceResult> RestartMinecraftAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> ReplaceWorldAsync(
        Vm1Settings vm1,
        string localZipPath,
        CancellationToken cancellationToken = default);
}

public sealed class SshService : ISshService
{
    private const string RemoteZipPath = "/tmp/mc-manager-world-replace.zip";

    public Task<ServiceResult> RestartMinecraftAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RestartMinecraft(vm1), cancellationToken);

    public Task<ServiceResult> ReplaceWorldAsync(
        Vm1Settings vm1,
        string localZipPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReplaceWorld(vm1, localZipPath), cancellationToken);

    private static ServiceResult RestartMinecraft(Vm1Settings vm1)
    {
        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var cmd = client.RunCommand($"sudo systemctl restart {EscapeShellArg(unit)}");
                if (cmd.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(cmd.Error) ? cmd.Result : cmd.Error;
                    return ServiceResult.Fail(
                        $"systemctl restart {unit} failed (exit {cmd.ExitStatus}): {err.Trim()}");
                }

                var active = client.RunCommand($"systemctl is-active {EscapeShellArg(unit)}");
                var state = (active.Result ?? "").Trim();
                if (!string.Equals(state, "active", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(state, "activating", StringComparison.OrdinalIgnoreCase))
                {
                    return ServiceResult.Fail(
                        $"Minecraft unit is '{state}' after restart (expected active).");
                }

                return ServiceResult.Ok();
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH restart failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult ReplaceWorld(Vm1Settings vm1, string localZipPath)
    {
        if (string.IsNullOrWhiteSpace(localZipPath) || !File.Exists(localZipPath))
            return ServiceResult.Fail($"Local zip not found: {localZipPath}");

        var worldPath = (vm1.WorldPath ?? "").Trim();
        if (string.IsNullOrWhiteSpace(worldPath) || !worldPath.StartsWith('/'))
            return ServiceResult.Fail("vm1.world_path must be an absolute path on VM1.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            var stopped = false;
            try
            {
                var stop = client.RunCommand($"sudo systemctl stop {EscapeShellArg(unit)}");
                if (stop.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(stop.Error) ? stop.Result : stop.Error;
                    return ServiceResult.Fail(
                        $"systemctl stop {unit} failed (exit {stop.ExitStatus}): {err.Trim()}");
                }

                stopped = true;

                using (var sftp = new SftpClient(client.ConnectionInfo))
                {
                    sftp.Connect();
                    using var local = File.OpenRead(localZipPath);
                    sftp.UploadFile(local, RemoteZipPath, canOverride: true);
                }

                var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");
                var bak = $"{worldPath}.bak.{stamp}";
                // Lab zips store files relative to the world folder (not a top-level world/ dir).
                var script =
                    "set -euo pipefail; "
                    + $"WORLD={EscapeShellArg(worldPath)}; "
                    + $"BAK={EscapeShellArg(bak)}; "
                    + $"ZIP={EscapeShellArg(RemoteZipPath)}; "
                    + "PARENT=$(dirname \"$WORLD\"); "
                    + "if [ -e \"$WORLD\" ]; then sudo mv \"$WORLD\" \"$BAK\"; fi; "
                    + "sudo mkdir -p \"$WORLD\"; "
                    + "sudo unzip -q \"$ZIP\" -d \"$WORLD\"; "
                    + "sudo chown -R ubuntu:ubuntu \"$WORLD\"; "
                    + "rm -f \"$ZIP\"; "
                    + "echo OK";

                var extract = client.RunCommand($"sudo bash -c {EscapeShellArg(script)}");
                if (extract.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(extract.Error) ? extract.Result : extract.Error;
                    TryStartUnit(client, unit);
                    return ServiceResult.Fail(
                        $"World extract failed (exit {extract.ExitStatus}): {err.Trim()}. "
                        + "Attempted to start Minecraft again.");
                }

                var start = client.RunCommand($"sudo systemctl start {EscapeShellArg(unit)}");
                if (start.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(start.Error) ? start.Result : start.Error;
                    return ServiceResult.Fail(
                        $"World replaced but systemctl start {unit} failed "
                        + $"(exit {start.ExitStatus}): {err.Trim()}");
                }

                return ServiceResult.Ok();
            }
            catch (Exception ex)
            {
                if (stopped)
                    TryStartUnit(client, unit);
                return ServiceResult.Fail($"SSH world replace failed: {ex.Message}");
            }
        }
    }

    private static void TryStartUnit(SshClient client, string unit)
    {
        try
        {
            client.RunCommand($"sudo systemctl start {EscapeShellArg(unit)}");
        }
        catch
        {
            // Best-effort recovery; caller already has the primary error.
        }
    }

    private static bool TryOpenSsh(
        Vm1Settings vm1,
        out SshClient client,
        out string unit,
        out string? error)
    {
        client = null!;
        unit = string.IsNullOrWhiteSpace(vm1.MinecraftUnit) ? "minecraft" : vm1.MinecraftUnit.Trim();
        error = null;

        if (string.IsNullOrWhiteSpace(vm1.SshHost))
        {
            error = "vm1.ssh_host is empty.";
            return false;
        }

        var keyPath = LocalConfigStore.ExpandPath(vm1.SshKeyPath);
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            error = $"SSH key not found: {keyPath}";
            return false;
        }

        var user = string.IsNullOrWhiteSpace(vm1.SshUser) ? "ubuntu" : vm1.SshUser;

        try
        {
            var keyFile = new PrivateKeyFile(keyPath);
            client = new SshClient(vm1.SshHost, user, keyFile);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            client.Connect();
            return true;
        }
        catch (Exception ex)
        {
            client?.Dispose();
            client = null!;
            error = $"SSH connect failed: {ex.Message}";
            return false;
        }
    }

    private static string EscapeShellArg(string value) =>
        "'" + value.Replace("'", "'\\''") + "'";
}
