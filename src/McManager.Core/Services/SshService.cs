using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using McManager.Core.Config;
using McManager.Core.Setup;
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

    Task<ServiceResult> WipeWorldAsync(
        Vm1Settings vm1,
        string? levelSeed = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stream a live world zip from VM1 to <paramref name="localZipPath"/> (stdout of
    /// <c>world_backup.py --stream-stdout</c>). Does not upload to Object Storage.
    /// </summary>
    Task<ServiceResult> DownloadLiveWorldZipAsync(
        Vm1Settings vm1,
        string localZipPath,
        IProgress<long>? progress = null,
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

    /// <summary>
    /// Recent Minecraft unit logs via SSH <c>journalctl</c>. Not a live tail / PTY.
    /// </summary>
    Task<SshExecResult> FetchMinecraftLogsAsync(
        Vm1Settings vm1,
        int lineCount = MinecraftConsoleRemote.DefaultLogLines,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One RCON command over SSH to VM1 localhost:25575 using <c>/etc/mcmgr/rcon.secret</c>.
    /// Never opens RCON on the Security List.
    /// </summary>
    Task<SshExecResult> SendMinecraftRconAsync(
        Vm1Settings vm1,
        string command,
        CancellationToken cancellationToken = default);

    Task<SshExecResult> UploadTextFilesAsync(
        SshTarget target,
        IReadOnlyList<(string LocalPath, string RemotePath)> files,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrite guest heap (Xms=Xmx) in the systemd unit and/or <c>user_jvm_args.txt</c>,
    /// daemon-reload, then restart Minecraft. Paper Fill/Aikar flags stay on ExecStart.
    /// </summary>
    Task<ServiceResult> ApplyJvmHeapAsync(
        Vm1Settings vm1,
        string heap,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Read non-heap JVM flags from the guest (Paper JSON or user extras).
    /// Does not restart Minecraft.
    /// </summary>
    Task<ServiceResult<IReadOnlyList<string>>> DumpJvmExtraFlagsAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Write non-heap JVM flags, daemon-reload, then restart Minecraft.
    /// Empty Paper list restores Fill/Aikar (or G1 fallback). Does not change heap.
    /// </summary>
    Task<ServiceResult> ApplyJvmExtraFlagsAsync(
        Vm1Settings vm1,
        IReadOnlyList<string> flags,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> UploadMinecraftPluginAsync(
        Vm1Settings vm1,
        string localJarPath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteMinecraftPluginAsync(
        Vm1Settings vm1,
        string fileName,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> UploadMinecraftModAsync(
        Vm1Settings vm1,
        string localJarPath,
        CancellationToken cancellationToken = default);

    Task<ServiceResult> DeleteMinecraftModAsync(
        Vm1Settings vm1,
        string fileName,
        CancellationToken cancellationToken = default);
}

public sealed class SshService : ISshService
{
    private const string RemoteZipPath = "/tmp/mc-manager-world-replace.zip";
    private const string RemoteAgentConfig = "/etc/mc-manager/config.json";
    private const string RemoteAgentConfigTmp = "/tmp/mc-manager-config-patch.json";
    private const string IdleWatchTimer = "mc-idle-watch.timer";
    private const string RemoteWorldBackupScript = "/opt/mc-manager/world_backup.py";
    private static readonly TimeSpan LiveWorldZipTimeout = TimeSpan.FromHours(3);

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

    public Task<ServiceResult> WipeWorldAsync(
        Vm1Settings vm1,
        string? levelSeed = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => WipeWorld(vm1, levelSeed), cancellationToken);

    public Task<ServiceResult> DownloadLiveWorldZipAsync(
        Vm1Settings vm1,
        string localZipPath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DownloadLiveWorldZip(vm1, localZipPath, progress), cancellationToken);

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

    public Task<SshExecResult> FetchMinecraftLogsAsync(
        Vm1Settings vm1,
        int lineCount = MinecraftConsoleRemote.DefaultLogLines,
        CancellationToken cancellationToken = default) =>
        Task.Run(
            () => RunCommand(
                SshTarget.FromVm1(vm1),
                MinecraftConsoleRemote.LogsCommand(lineCount),
                TimeSpan.FromSeconds(30)),
            cancellationToken);

    public Task<SshExecResult> SendMinecraftRconAsync(
        Vm1Settings vm1,
        string command,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => SendMinecraftRcon(vm1, command), cancellationToken);

    public Task<SshExecResult> UploadTextFilesAsync(
        SshTarget target,
        IReadOnlyList<(string LocalPath, string RemotePath)> files,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => UploadTextFiles(target, files), cancellationToken);

    public Task<ServiceResult> ApplyJvmHeapAsync(
        Vm1Settings vm1,
        string heap,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplyJvmHeap(vm1, heap), cancellationToken);

    public Task<ServiceResult<IReadOnlyList<string>>> DumpJvmExtraFlagsAsync(
        Vm1Settings vm1,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DumpJvmExtraFlags(vm1), cancellationToken);

    public Task<ServiceResult> ApplyJvmExtraFlagsAsync(
        Vm1Settings vm1,
        IReadOnlyList<string> flags,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => ApplyJvmExtraFlags(vm1, flags), cancellationToken);

    public Task<ServiceResult> UploadMinecraftPluginAsync(
        Vm1Settings vm1,
        string localJarPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => UploadMinecraftPlugin(vm1, localJarPath), cancellationToken);

    public Task<ServiceResult> DeleteMinecraftPluginAsync(
        Vm1Settings vm1,
        string fileName,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteMinecraftPlugin(vm1, fileName), cancellationToken);

    public Task<ServiceResult> UploadMinecraftModAsync(
        Vm1Settings vm1,
        string localJarPath,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => UploadMinecraftMod(vm1, localJarPath), cancellationToken);

    public Task<ServiceResult> DeleteMinecraftModAsync(
        Vm1Settings vm1,
        string fileName,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => DeleteMinecraftMod(vm1, fileName), cancellationToken);

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

    private static ServiceResult ApplyJvmHeap(Vm1Settings vm1, string heap)
    {
        var token = JvmHeapChoice.Normalize(heap);
        return WithHeapScript(
            vm1,
            restartMinecraft: true,
            extraUploads: null,
            JvmHeapApply.RunCommand(token),
            combined =>
            {
                if (!JvmHeapApply.TryParseOk(combined, out _, out var parseError))
                    return ServiceResult.Fail(parseError ?? "Heap apply did not confirm OK.");
                return ServiceResult.Ok();
            });
    }

    private static ServiceResult<IReadOnlyList<string>> DumpJvmExtraFlags(Vm1Settings vm1)
    {
        IReadOnlyList<string>? flags = null;
        string? fail = null;
        var ran = WithHeapScript(
            vm1,
            restartMinecraft: false,
            extraUploads: null,
            JvmHeapApply.DumpExtrasCommand(),
            combined =>
            {
                if (!JvmHeapApply.TryParseExtrasDump(combined, out var parsed, out var parseError))
                {
                    fail = parseError;
                    return ServiceResult.Fail(parseError ?? "Flag dump did not confirm OK.");
                }

                flags = parsed;
                return ServiceResult.Ok();
            });
        if (!ran.Succeeded)
            return ServiceResult<IReadOnlyList<string>>.Fail(fail ?? ran.Error ?? "Flag dump failed.");
        return ServiceResult<IReadOnlyList<string>>.Ok(flags ?? []);
    }

    private static ServiceResult ApplyJvmExtraFlags(Vm1Settings vm1, IReadOnlyList<string> flags)
    {
        ArgumentNullException.ThrowIfNull(flags);
        var cleaned = JvmExtraFlags.Parse(string.Join('\n', flags));
        var json = JsonSerializer.Serialize(cleaned);
        return WithHeapScript(
            vm1,
            restartMinecraft: true,
            extraUploads: new (string Text, string RemotePath)[] { (json, JvmHeapApply.RemoteExtrasJsonPath) },
            JvmHeapApply.SetExtrasCommand(),
            combined =>
            {
                if (!JvmHeapApply.TryParseExtrasSet(combined, out var parseError))
                    return ServiceResult.Fail(parseError ?? "Flag apply did not confirm OK.");
                return ServiceResult.Ok();
            });
    }

    private static ServiceResult WithHeapScript(
        Vm1Settings vm1,
        bool restartMinecraft,
        IReadOnlyList<(string Text, string RemotePath)>? extraUploads,
        string runCommand,
        Func<string, ServiceResult> parseStdout)
    {
        var local = JvmHeapApply.FindLocalScript();
        if (local is null)
            return ServiceResult.Fail("Product onbox/mcmgr/common/apply-jvm-heap.py was not found.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var mkdir = client.RunCommand("mkdir -p " + SshShell.Quote(JvmHeapApply.RemoteDir));
                if (mkdir.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(mkdir.Error) ? mkdir.Result : mkdir.Error;
                    return ServiceResult.Fail("Could not create heap staging dir: " + err.Trim());
                }

                using (var sftp = new SftpClient(client.ConnectionInfo))
                {
                    sftp.Connect();
                    var text = File.ReadAllText(local).Replace("\r\n", "\n").Replace("\r", "\n");
                    using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                        sftp.UploadFile(ms, JvmHeapApply.RemoteScriptPath, canOverride: true);
                    if (extraUploads is not null)
                    {
                        foreach (var (payload, remote) in extraUploads)
                        {
                            var lf = payload.Replace("\r\n", "\n").Replace("\r", "\n");
                            using var extra = new MemoryStream(Encoding.UTF8.GetBytes(lf));
                            sftp.UploadFile(extra, remote, canOverride: true);
                        }
                    }
                }

                var strip = client.RunCommand(
                    "sed -i 's/\\r$//' " + SshShell.Quote(JvmHeapApply.RemoteScriptPath));
                if (strip.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(strip.Error) ? strip.Result : strip.Error;
                    return ServiceResult.Fail("Could not strip CR from heap script: " + err.Trim());
                }

                var apply = client.RunCommand(runCommand);
                var combined = CombineOutput(apply.Result, apply.Error);
                if (apply.ExitStatus != 0)
                {
                    return ServiceResult.Fail(
                        $"Heap script failed (exit {apply.ExitStatus}): {combined.Trim()}");
                }

                var parsed = parseStdout(combined);
                if (!parsed.Succeeded)
                    return parsed;

                if (!restartMinecraft)
                    return ServiceResult.Ok();

                var restart = RestartMinecraftWithClient(client, unit);
                return restart.Succeeded
                    ? ServiceResult.Ok()
                    : restart;
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH heap script failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult UploadMinecraftPlugin(Vm1Settings vm1, string localJarPath)
    {
        if (string.IsNullOrWhiteSpace(localJarPath) || !File.Exists(localJarPath))
            return ServiceResult.Fail("Plugin jar not found on this PC.");

        var name = Path.GetFileName(localJarPath);
        if (!ServerPluginsInspect.IsSafeJarName(name))
            return ServiceResult.Fail("Plugin file name is not a safe .jar name.");

        var info = new FileInfo(localJarPath);
        if (info.Length <= 0)
            return ServiceResult.Fail("Plugin jar is empty.");
        if (info.Length > ServerPluginsInspect.MaxUploadBytes)
            return ServiceResult.Fail("Plugin jar is larger than 64 MB.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var remote = ServerPluginsInspect.StagingRemotePath(name);
                var parent = RemoteParent(remote);
                var mkdir = client.RunCommand("mkdir -p " + SshShell.Quote(parent));
                if (mkdir.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(mkdir.Error) ? mkdir.Result : mkdir.Error;
                    return ServiceResult.Fail("Could not create plugin staging dir: " + err.Trim());
                }

                using (var sftp = new SftpClient(client.ConnectionInfo))
                {
                    sftp.Connect();
                    using var local = File.OpenRead(localJarPath);
                    sftp.UploadFile(local, remote, canOverride: true);
                }

                var install = client.RunCommand(ServerPluginsInspect.InstallScript(name));
                if (install.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(install.Error) ? install.Result : install.Error;
                    return ServiceResult.Fail(
                        $"Plugin install failed (exit {install.ExitStatus}): {err.Trim()}");
                }

                return RestartMinecraftWithClient(client, unit);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH plugin upload failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult DeleteMinecraftPlugin(Vm1Settings vm1, string fileName)
    {
        if (!ServerPluginsInspect.IsSafeJarName(fileName))
            return ServiceResult.Fail("Plugin file name is not a safe .jar name.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var del = client.RunCommand(ServerPluginsInspect.DeleteScript(fileName));
                if (del.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(del.Error) ? del.Result : del.Error;
                    return ServiceResult.Fail(
                        $"Plugin delete failed (exit {del.ExitStatus}): {err.Trim()}");
                }

                return RestartMinecraftWithClient(client, unit);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH plugin delete failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult UploadMinecraftMod(Vm1Settings vm1, string localJarPath)
    {
        if (string.IsNullOrWhiteSpace(localJarPath) || !File.Exists(localJarPath))
            return ServiceResult.Fail("Mod jar not found on this PC.");

        var name = Path.GetFileName(localJarPath);
        if (!ServerModsInspect.IsSafeJarName(name))
            return ServiceResult.Fail("Mod file name is not a safe .jar name.");

        var info = new FileInfo(localJarPath);
        if (info.Length <= 0)
            return ServiceResult.Fail("Mod jar is empty.");
        if (info.Length > ServerModsInspect.MaxUploadBytes)
            return ServiceResult.Fail("Mod jar is larger than 64 MB.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var remote = ServerModsInspect.StagingRemotePath(name);
                var parent = RemoteParent(remote);
                var mkdir = client.RunCommand("mkdir -p " + SshShell.Quote(parent));
                if (mkdir.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(mkdir.Error) ? mkdir.Result : mkdir.Error;
                    return ServiceResult.Fail("Could not create mod staging dir: " + err.Trim());
                }

                using (var sftp = new SftpClient(client.ConnectionInfo))
                {
                    sftp.Connect();
                    using var local = File.OpenRead(localJarPath);
                    sftp.UploadFile(local, remote, canOverride: true);
                }

                var install = client.RunCommand(ServerModsInspect.InstallScript(name));
                if (install.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(install.Error) ? install.Result : install.Error;
                    return ServiceResult.Fail(
                        $"Mod install failed (exit {install.ExitStatus}): {err.Trim()}");
                }

                return RestartMinecraftWithClient(client, unit);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH mod upload failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult DeleteMinecraftMod(Vm1Settings vm1, string fileName)
    {
        if (!ServerModsInspect.IsSafeJarName(fileName))
            return ServiceResult.Fail("Mod file name is not a safe .jar name.");

        if (!TryOpenSsh(vm1, out var client, out var unit, out var error))
            return ServiceResult.Fail(error!);

        using (client)
        {
            try
            {
                var del = client.RunCommand(ServerModsInspect.DeleteScript(fileName));
                if (del.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(del.Error) ? del.Result : del.Error;
                    return ServiceResult.Fail(
                        $"Mod delete failed (exit {del.ExitStatus}): {err.Trim()}");
                }

                return RestartMinecraftWithClient(client, unit);
            }
            catch (Exception ex)
            {
                return ServiceResult.Fail($"SSH mod delete failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult RestartMinecraftWithClient(SshClient client, string unit)
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

    private static ServiceResult WipeWorld(Vm1Settings vm1, string? levelSeed)
    {
        if (!WorldWipe.TryCreate(vm1.WorldPath, out var plan, out var pathError))
            return ServiceResult.Fail(pathError ?? "vm1.world_path is invalid.");

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

                var seedPatch = client.RunCommand(
                    $"sudo bash -c {EscapeShellArg(WorldSeedPatch.BuildRemoteScript(levelSeed))}");
                if (seedPatch.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(seedPatch.Error) ? seedPatch.Result : seedPatch.Error;
                    TryStartUnit(client, unit);
                    return ServiceResult.Fail(
                        $"Could not update level-seed (exit {seedPatch.ExitStatus}): {err.Trim()}. "
                        + "Attempted to start Minecraft again.");
                }

                var wipe = client.RunCommand($"sudo bash -c {EscapeShellArg(plan.RemoteScript)}");
                if (wipe.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(wipe.Error) ? wipe.Result : wipe.Error;
                    TryStartUnit(client, unit);
                    return ServiceResult.Fail(
                        $"World wipe failed (exit {wipe.ExitStatus}): {err.Trim()}. "
                        + "Attempted to start Minecraft again.");
                }

                var start = client.RunCommand($"sudo systemctl start {EscapeShellArg(unit)}");
                if (start.ExitStatus != 0)
                {
                    var err = string.IsNullOrWhiteSpace(start.Error) ? start.Result : start.Error;
                    return ServiceResult.Fail(
                        $"World wiped but systemctl start {unit} failed "
                        + $"(exit {start.ExitStatus}): {err.Trim()}");
                }

                return ServiceResult.Ok();
            }
            catch (Exception ex)
            {
                if (stopped)
                    TryStartUnit(client, unit);
                return ServiceResult.Fail($"SSH world wipe failed: {ex.Message}");
            }
        }
    }

    private static ServiceResult DownloadLiveWorldZip(
        Vm1Settings vm1,
        string localZipPath,
        IProgress<long>? progress)
    {
        if (string.IsNullOrWhiteSpace(localZipPath))
            return ServiceResult.Fail("Local zip path is empty.");

        var destDir = Path.GetDirectoryName(localZipPath);
        if (!string.IsNullOrEmpty(destDir))
            Directory.CreateDirectory(destDir);

        if (!TryOpenSsh(vm1, out var client, out _, out var error))
            return ServiceResult.Fail(error!);

        // sudo python3 -u: config is root:600; unbuffered so the zip starts flowing immediately.
        var remote =
            "sudo python3 -u "
            + EscapeShellArg(RemoteWorldBackupScript)
            + " --stream-stdout";

        using (client)
        {
            try
            {
                var cmd = client.CreateCommand(remote);
                cmd.CommandTimeout = LiveWorldZipTimeout;
                var asyncResult = cmd.BeginExecute();
                long total = 0;
                try
                {
                    using var local = new FileStream(
                        localZipPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        64 * 1024);
                    var stdout = cmd.OutputStream;
                    var buffer = new byte[64 * 1024];
                    int n;
                    while ((n = stdout.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        local.Write(buffer, 0, n);
                        total += n;
                        progress?.Report(total);
                    }
                }
                catch
                {
                    TryDeleteLocal(localZipPath);
                    throw;
                }

                cmd.EndExecute(asyncResult);
                var exit = cmd.ExitStatus ?? -1;
                var stderr = (cmd.Error ?? "").Trim();
                if (exit != 0)
                {
                    TryDeleteLocal(localZipPath);
                    var hint = string.IsNullOrEmpty(stderr)
                        ? $"Is {RemoteWorldBackupScript} deployed with --stream-stdout? Redeploy the idle agent."
                        : stderr;
                    return ServiceResult.Fail(
                        $"Live world zip over SSH failed (exit {exit}): {hint}");
                }

                if (total < 22)
                {
                    TryDeleteLocal(localZipPath);
                    return ServiceResult.Fail(
                        "SSH world download did not produce a zip. "
                        + (string.IsNullOrEmpty(stderr)
                            ? "Is the world folder present on the game VM?"
                            : stderr));
                }

                return ServiceResult.Ok();
            }
            catch (Exception ex)
            {
                TryDeleteLocal(localZipPath);
                return ServiceResult.Fail($"SSH world download failed: {ex.Message}");
            }
        }
    }

    private static void TryDeleteLocal(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Best-effort; caller already has the primary error.
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

    private static SshExecResult SendMinecraftRcon(Vm1Settings vm1, string command)
    {
        if (!MinecraftConsoleRemote.TryBuildRconCommand(command, out var remote, out var error))
            return SshExecResult.Fail(error ?? MinecraftConsoleRemote.EmptyCommandHint);

        return RunCommand(SshTarget.FromVm1(vm1), remote, TimeSpan.FromSeconds(30));
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
