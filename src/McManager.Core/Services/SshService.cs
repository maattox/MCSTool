using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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

    Task<ServiceResult> ApplyIdleSettingsAsync(
        Vm1Settings vm1,
        bool idleAgentEnabled,
        int idleTimeoutMinutes,
        int budgetWarnMinutes,
        CancellationToken cancellationToken = default);

    Task<SshExecResult> RunCommandAsync(
        SshTarget target,
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    Task<SshExecResult> UploadTextFilesAsync(
        SshTarget target,
        IReadOnlyList<(string LocalPath, string RemotePath)> files,
        CancellationToken cancellationToken = default);
}

public sealed class SshService : ISshService
{
    private const string RemoteZipPath = "/tmp/mc-manager-world-replace.zip";
    private const string RemoteAgentConfig = "/etc/mc-manager/config.json";
    private const string RemoteAgentConfigTmp = "/tmp/mc-manager-config-patch.json";
    private const string IdleWatchTimer = "mc-idle-watch.timer";

    private static readonly JsonSerializerOptions JsonWriteOptions = new()
    {
        WriteIndented = true,
    };

    public Task<ServiceResult> RestartMinecraftAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RestartMinecraft(vm1), cancellationToken);

    public Task<ServiceResult> ReplaceWorldAsync(
        Vm1Settings vm1,
        string localZipPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ReplaceWorld(vm1, localZipPath), cancellationToken);

    public Task<ServiceResult> ApplyIdleSettingsAsync(
        Vm1Settings vm1,
        bool idleAgentEnabled,
        int idleTimeoutMinutes,
        int budgetWarnMinutes,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => ApplyIdleSettings(vm1, idleAgentEnabled, idleTimeoutMinutes, budgetWarnMinutes),
            cancellationToken);

    public Task<SshExecResult> RunCommandAsync(
        SshTarget target,
        string command,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => RunCommand(target, command, timeout ?? TimeSpan.FromMinutes(2)), cancellationToken);

    public Task<SshExecResult> UploadTextFilesAsync(
        SshTarget target,
        IReadOnlyList<(string LocalPath, string RemotePath)> files,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => UploadTextFiles(target, files), cancellationToken);

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
                    + "case \"$WORLD\" in "
                    + "/opt/mcmgr/*) sudo chown -R mcmgr:mcmgr \"$WORLD\"; sudo chmod 0750 \"$WORLD\" ;; "
                    + "*) sudo chown -R ubuntu:ubuntu \"$WORLD\" ;; "
                    + "esac; "
                    + "if [ -x /opt/mcmgr/bin/repair-permissions.sh ] && [ \"${WORLD#/opt/mcmgr/}\" != \"$WORLD\" ]; then "
                    + "sudo bash /opt/mcmgr/bin/repair-permissions.sh; "
                    + "fi; "
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

    private static ServiceResult ApplyIdleSettings(
        Vm1Settings vm1,
        bool idleAgentEnabled,
        int idleTimeoutMinutes,
        int budgetWarnMinutes)
    {
        if (idleTimeoutMinutes < 1)
            return ServiceResult.Fail("idle_timeout_minutes must be ≥ 1.");
        if (budgetWarnMinutes < 0)
            return ServiceResult.Fail("budget_warn_minutes must be ≥ 0.");

        if (!TryOpenSsh(vm1, out var client, out _, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var cat = client.RunCommand($"sudo cat {EscapeShellArg(RemoteAgentConfig)}");
                if (cat.ExitStatus != 0 || string.IsNullOrWhiteSpace(cat.Result))
                {
                    var err = string.IsNullOrWhiteSpace(cat.Error) ? cat.Result : cat.Error;
                    return ServiceResult.Fail(
                        $"Could not read {RemoteAgentConfig} (is the idle agent deployed?). {err.Trim()}");
                }

                JsonObject root;
                try
                {
                    var node = JsonNode.Parse(cat.Result);
                    root = node as JsonObject
                           ?? throw new JsonException("Agent config root is not a JSON object.");
                }
                catch (JsonException ex)
                {
                    return ServiceResult.Fail($"Invalid agent config JSON on VM: {ex.Message}");
                }

                root["idle_agent_enabled"] = idleAgentEnabled;
                root["idle_timeout_minutes"] = idleTimeoutMinutes;
                root["budget_warn_minutes"] = budgetWarnMinutes;

                var payload = root.ToJsonString(JsonWriteOptions) + "\n";
                var bytes = Encoding.UTF8.GetBytes(payload);

                using (var sftp = new SftpClient(client.ConnectionInfo))
                {
                    sftp.Connect();
                    using var ms = new MemoryStream(bytes);
                    sftp.UploadFile(ms, RemoteAgentConfigTmp, canOverride: true);
                }

                var install =
                    "set -euo pipefail; "
                    + $"sudo cp {EscapeShellArg(RemoteAgentConfigTmp)} {EscapeShellArg(RemoteAgentConfig)}; "
                    + $"sudo chown root:root {EscapeShellArg(RemoteAgentConfig)}; "
                    + $"sudo chmod 600 {EscapeShellArg(RemoteAgentConfig)}; "
                    + $"rm -f {EscapeShellArg(RemoteAgentConfigTmp)}; "
                    + "echo OK";

                var write = client.RunCommand($"bash -c {EscapeShellArg(install)}");
                if (write.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(write.Error) ? write.Result : write.Error;
                    return ServiceResult.Fail(
                        $"Failed to install agent config (exit {write.ExitStatus}): {err.Trim()}");
                }

                if (idleAgentEnabled)
                {
                    var enable = client.RunCommand(
                        $"sudo systemctl enable {EscapeShellArg(IdleWatchTimer)}");
                    var start = client.RunCommand(
                        $"sudo systemctl start {EscapeShellArg(IdleWatchTimer)}");
                    if (enable.ExitStatus != 0 || start.ExitStatus != 0)
                    {
                        var err = string.Join(
                            " ",
                            new[] { enable.Error, start.Error, enable.Result, start.Result }
                                .Where(s => !string.IsNullOrWhiteSpace(s)));
                        return ServiceResult.Fail(
                            $"Config written but enabling {IdleWatchTimer} failed: {err.Trim()}");
                    }
                }
                else
                {
                    var stop = client.RunCommand(
                        $"sudo systemctl stop {EscapeShellArg(IdleWatchTimer)}");
                    var disable = client.RunCommand(
                        $"sudo systemctl disable {EscapeShellArg(IdleWatchTimer)}");
                    if (stop.ExitStatus != 0 || disable.ExitStatus != 0)
                    {
                        var err = string.Join(
                            " ",
                            new[] { stop.Error, disable.Error, stop.Result, disable.Result }
                                .Where(s => !string.IsNullOrWhiteSpace(s)));
                        return ServiceResult.Fail(
                            $"Config written but disabling {IdleWatchTimer} failed: {err.Trim()}");
                    }
                }

                return ServiceResult.Ok();
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH idle apply failed: {ex.Message}");
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

    private static SshExecResult RunCommand(SshTarget target, string command, TimeSpan timeout)
    {
        if (!TryOpenSsh(target, out var client, out var error))
            return SshExecResult.Fail(error!);

        using (client)
        {
            try
            {
                var cmd = client.CreateCommand(command);
                cmd.CommandTimeout = timeout;
                var stdout = cmd.Execute() ?? "";
                var stderr = cmd.Error ?? "";
                var combined = CombineOutput(stdout, stderr);
                var exit = cmd.ExitStatus ?? -1;
                if (exit != 0)
                {
                    return SshExecResult.Fail(
                        $"{target.Label} SSH command failed (exit {exit}).",
                        combined,
                        exit);
                }

                return SshExecResult.Ok(combined, exit);
            }
            catch (Exception ex)
            {
                return SshExecResult.Fail($"{target.Label} SSH command failed: {ex.Message}");
            }
        }
    }

    private static SshExecResult UploadTextFiles(
        SshTarget target,
        IReadOnlyList<(string LocalPath, string RemotePath)> files)
    {
        if (files.Count == 0)
            return SshExecResult.Ok("(no files)");

        if (!TryOpenSsh(target, out var client, out var error))
            return SshExecResult.Fail(error!);

        using (client)
        {
            try
            {
                var parents = files
                    .Select(f => RemoteParent(f.RemotePath))
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                foreach (var parent in parents)
                {
                    var mkdir = client.RunCommand("mkdir -p " + SshShell.Quote(parent));
                    if (mkdir.ExitStatus != 0)
                    {
                        var err = string.IsNullOrWhiteSpace(mkdir.Error) ? mkdir.Result : mkdir.Error;
                        return SshExecResult.Fail(
                            $"mkdir {parent} failed (ubuntu-writable staging only): {err.Trim()}",
                            CombineOutput(mkdir.Result, mkdir.Error),
                            mkdir.ExitStatus ?? -1);
                    }
                }

                using var sftp = new SftpClient(client.ConnectionInfo);
                sftp.Connect();
                var names = new List<string>();
                foreach (var (localPath, remotePath) in files)
                {
                    if (!File.Exists(localPath))
                        return SshExecResult.Fail($"Local file not found: {localPath}");

                    var text = File.ReadAllText(localPath).Replace("\r\n", "\n").Replace("\r", "\n");
                    using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
                    sftp.UploadFile(ms, remotePath, canOverride: true);
                    names.Add(Path.GetFileName(localPath));
                }

                return SshExecResult.Ok("uploaded " + string.Join(", ", names));
            }
            catch (Exception ex)
            {
                return SshExecResult.Fail($"{target.Label} SFTP upload failed: {ex.Message}");
            }
        }
    }

    private static bool TryOpenSsh(
        Vm1Settings vm1,
        out SshClient client,
        out string unit,
        out string? error)
    {
        unit = string.IsNullOrWhiteSpace(vm1.MinecraftUnit) ? "minecraft" : vm1.MinecraftUnit.Trim();
        return TryOpenSsh(SshTarget.FromVm1(vm1), out client, out error);
    }

    private static bool TryOpenSsh(SshTarget target, out SshClient client, out string? error)
    {
        client = null!;
        error = null;

        if (string.IsNullOrWhiteSpace(target.Host))
        {
            error = $"{target.Label} ssh_host is empty.";
            return false;
        }

        var keyPath = LocalConfigStore.ExpandPath(target.KeyPath);
        if (string.IsNullOrWhiteSpace(keyPath) || !File.Exists(keyPath))
        {
            error = $"SSH key not found: {keyPath}";
            return false;
        }

        var user = string.IsNullOrWhiteSpace(target.User) ? "ubuntu" : target.User;

        try
        {
            var keyFile = new PrivateKeyFile(keyPath);
            client = new SshClient(target.Host, user, keyFile);
            client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
            client.Connect();
            return true;
        }
        catch (Exception ex)
        {
            client?.Dispose();
            client = null!;
            error = $"{target.Label} SSH connect failed: {ex.Message}";
            return false;
        }
    }

    private static string CombineOutput(string? stdout, string? stderr)
    {
        var outText = (stdout ?? "").TrimEnd();
        var errText = (stderr ?? "").TrimEnd();
        if (string.IsNullOrEmpty(errText))
            return outText;
        if (string.IsNullOrEmpty(outText))
            return errText;
        return outText + Environment.NewLine + errText;
    }

    private static string RemoteParent(string remotePath)
    {
        var n = remotePath.Replace('\\', '/');
        var i = n.LastIndexOf('/');
        return i <= 0 ? "" : n[..i];
    }

    private static string EscapeShellArg(string value) => SshShell.Quote(value);
}
