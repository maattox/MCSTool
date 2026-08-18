using System.Text;
using McManager.Core.Config;
using McManager.Core.Onbox;
using McManager.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace McManager.Core.Setup;

/// <summary>SSH wait/cloud-init + door / onbox / idle-agent upload. Follows lab Agent-Deploy-Pitfalls.</summary>
public sealed class SetupBootstrapService
{
    public async Task<ServiceResult> WaitCloudInitAsync(
        string host,
        string user,
        string keyPath,
        string remoteMarker,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var delay = 3.0;
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(20);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // SETUP-ISSUE-5: /etc/mcmgr is 0750 root:mcmgr. ubuntu test -f cannot traverse it
            // (Permission denied → WAIT forever) even after cloud-init wrote the marker.
            var probe = await Task.Run(
                () => RunOnce(host, user, keyPath, CloudInitProbeCommand(remoteMarker), TimeSpan.FromSeconds(30)),
                cancellationToken).ConfigureAwait(false);
            if (probe.Succeeded && (probe.Value ?? "").Contains("OK", StringComparison.Ordinal))
            {
                log?.Report($"cloud-init ready: {remoteMarker}");
                return ServiceResult.Ok();
            }

            if (DateTime.UtcNow >= deadline)
            {
                return ServiceResult.Fail(
                    $"Timed out waiting for {remoteMarker} on {host}. Last: {probe.Error ?? probe.Value}");
            }

            log?.Report($"waiting for {remoteMarker} on {host}…");
            await Task.Delay(TimeSpan.FromSeconds(Math.Min(delay, 30)), cancellationToken).ConfigureAwait(false);
            delay = Math.Min(delay * 2, 30);
        }
    }

    public Task<ServiceResult> DeployDoorAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var src = ProductPaths.FindDoorVmDirectory();
        if (src is null)
        {
            return Task.FromResult(ServiceResult.Fail(
                "Product door_vm/ not found. Expected OCI-mc-server/door_vm (not lab development/)."));
        }

        var key = TofuApplyOutputs.PrivateKeyPath(state);
        return Task.Run(
            () =>
            {
                try
                {
                    return DeployDoor(src, outputs, key, log);
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail("Door bootstrap failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    public Task<ServiceResult> DeployVm1Async(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var onbox = ProductPaths.FindOnboxDirectory();
        if (onbox is null)
            return Task.FromResult(ServiceResult.Fail("Product onbox/mcmgr/ not found."));

        var agent = ProductPaths.FindVmAgentDirectory();
        if (agent is null)
        {
            return Task.FromResult(ServiceResult.Fail(
                "Product vm_agent/ not found. Expected OCI-mc-server/vm_agent."));
        }

        var key = TofuApplyOutputs.PrivateKeyPath(state);
        var version = state.MinecraftVersion.Trim();
        var eula = state.EulaAccepted;
        return Task.Run(
            () =>
            {
                try
                {
                    return DeployVm1(onbox, agent, outputs, key, version, eula, log);
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail("VM1 bootstrap failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Idempotent post-bootstrap: secondary play netplan, door oci.env Object Storage vars,
    /// managed server.properties (in-game whitelist off), reserved play IP on VM1 + PLAYABLE.
    /// Safe to re-run when apply_stage is already vm1.
    /// </summary>
    public Task<ServiceResult> EnsureGuestRuntimeAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var key = TofuApplyOutputs.PrivateKeyPath(state);
        return Task.Run(
            () =>
            {
                try
                {
                    EnsureDoorRuntime(outputs, key, log);
                    EnsureVm1Runtime(outputs, key, log);
                    PromotePlayableAfterVm1(outputs, key, log);
                    return ServiceResult.Ok();
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail("Guest runtime repair failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    public Task<ServiceResult<string>> PullRconSecretAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        CancellationToken cancellationToken = default)
    {
        var key = TofuApplyOutputs.PrivateKeyPath(state);
        return Task.Run(
            () => RunOnce(
                outputs.Vm1SshHost,
                outputs.SshUser,
                key,
                "sudo cat /etc/mcmgr/rcon.secret",
                TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    public Task<ServiceResult<string>> PullGameManifestAsync(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        CancellationToken cancellationToken = default)
    {
        var key = TofuApplyOutputs.PrivateKeyPath(state);
        return Task.Run(
            () => RunOnce(
                outputs.Vm1SshHost,
                outputs.SshUser,
                key,
                "sudo cat /etc/mcmgr/game-manifest.json",
                TimeSpan.FromSeconds(30)),
            cancellationToken);
    }

    private static ServiceResult DeployDoor(
        string src,
        TofuApplyOutputs outputs,
        string keyPath,
        IProgress<string>? log)
    {
        const string staging = "/tmp/mcmgr-door-setup";
        log?.Report($"Door src: {src}");
        using var client = Connect(outputs.DoorSshHost, outputs.SshUser, keyPath);
        Exec(client, $"rm -rf {staging} && mkdir -p {staging}", TimeSpan.FromSeconds(30), log);
        UploadTree(client, src, staging, log);
        var env = BuildDoorEnv(outputs);
        UploadText(client, staging + "/oci.env", env);

        // Lab install.sh: deps + gcc + OCI CLI + make + iptables + systemd. HOME for systemd oneshots.
        var script =
            "set -euo pipefail; "
            + "export HOME=\"${HOME:-/home/ubuntu}\"; "
            + $"ST={ShQuote(staging)}; "
            + "find \"$ST\" -type f \\( -name '*.sh' -o -name '*.service' -o -name '*.timer' -o -name '*.py' -o -name Makefile \\) "
            + "-exec sed -i 's/\\r$//' {} +; "
            + "sudo systemctl stop mccontrol.service 2>/dev/null || true; "
            + "cd \"$ST\" && OCI_ENV_FILE=\"$ST/oci.env\" bash install.sh --yes; "
            + "sudo bash -c '"
            + "set -euo pipefail; "
            + "export HOME=/home/ubuntu; "
            + "mkdir -p /opt/mccontrol/scripts /var/lib/mccontrol/os-cache; "
            + "cp -a " + staging + "/scripts/. /opt/mccontrol/scripts/; "
            + "chmod 755 /opt/mccontrol/scripts/*.sh; "
            + "cp -f " + staging + "/systemd/mccontrol-reconcile.service /etc/systemd/system/; "
            + "cp -f " + staging + "/systemd/mccontrol-reconcile.timer /etc/systemd/system/; "
            + "grep -q ^HOME= /etc/mccontrol/oci.env || echo HOME=/home/ubuntu >> /etc/mccontrol/oci.env; "
            + "sed -i \"s/\\r$//\" /opt/mccontrol/oci/*.sh /opt/mccontrol/scripts/*.sh /etc/mccontrol/oci.env /etc/systemd/system/mccontrol*.service /etc/systemd/system/mccontrol*.timer 2>/dev/null || true; "
            + "systemctl daemon-reload; "
            + "systemctl enable --now mccontrol-reconcile.timer; "
            + "echo DOOR_OK'";

        Exec(client, "bash -c " + ShQuote(script), TimeSpan.FromMinutes(30), log);
        PatchDoorConfig(client, outputs, log);
        log?.Report("Door bootstrap finished.");
        return ServiceResult.Ok();
    }

    private static void PatchDoorConfig(SshClient client, TofuApplyOutputs outputs, IProgress<string>? log)
    {
        var ocpus = outputs.Vm1Ocpus > 0 ? outputs.Vm1Ocpus.ToString(System.Globalization.CultureInfo.InvariantCulture) : "4";
        var py =
            "import json\n"
            + "p='/etc/mccontrol/config.json'\n"
            + "cfg=json.load(open(p))\n"
            + $"cfg['vm1_private_ip']={JsonString(outputs.Vm1PrimaryPrivateIp)}\n"
            + "cfg['object_storage_enabled']=True\n"
            + $"cfg['ocpus']=float({ocpus})\n"
            + "json.dump(cfg, open(p,'w'), indent=2)\n"
            + "open(p,'a').write('\\n')\n";
        UploadText(client, "/tmp/mcmgr-patch-door-cfg.py", py);
        Exec(
            client,
            "sudo bash -c " + ShQuote("python3 /tmp/mcmgr-patch-door-cfg.py && rm -f /tmp/mcmgr-patch-door-cfg.py"),
            TimeSpan.FromSeconds(30),
            log);
    }

    private static string BuildDoorEnv(TofuApplyOutputs o) =>
        $"INSTANCE_ID={o.Vm1InstanceId}\n"
        + $"RESERVED_PUBLIC_IP_ID={o.PlayReservedPublicIpId}\n"
        + $"VM1_PRIVATE_IP_ID={o.Vm1SecondaryPrivateIpId}\n"
        + $"VM2_PRIVATE_IP_ID={o.DoorSecondaryPrivateIpId}\n"
        + $"VM1_PRIVATE_IP={o.Vm1PrimaryPrivateIp}\n"
        + "WAIT_TIMEOUT_SEC=600\n"
        + "OCI_CLI_AUTH=instance_principal\n"
        + "PATH=/home/ubuntu/bin:/usr/bin:/bin\n"
        + "HOME=/home/ubuntu\n"
        + $"OBJECT_STORAGE_NAMESPACE={o.ObjectStorageNamespace}\n"
        + $"OBJECT_STORAGE_BUCKET={o.ObjectStorageBucket}\n"
        + "OS_CACHE_DIR=/var/lib/mccontrol/os-cache\n";

    private static void EnsureDoorRuntime(TofuApplyOutputs outputs, string keyPath, IProgress<string>? log)
    {
        log?.Report("Repairing door runtime (oci.env, play netplan)…");
        using var client = Connect(outputs.DoorSshHost, outputs.SshUser, keyPath);
        var env = BuildDoorEnv(outputs);
        UploadText(client, "/tmp/mcmgr-door-oci.env", env);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "install -m 600 /tmp/mcmgr-door-oci.env /etc/mccontrol/oci.env; "
                + "rm -f /tmp/mcmgr-door-oci.env; "
                + "grep -q ^HOME= /etc/mccontrol/oci.env || echo HOME=/home/ubuntu >> /etc/mccontrol/oci.env"),
            TimeSpan.FromSeconds(30),
            log);
        PatchDoorConfig(client, outputs, log);
        ApplyPlayNetplan(client, outputs.DoorSecondaryPrivateIp, "door", log);
        InstallDoorOciOrScript(client, doorSrc: ProductPaths.FindDoorVmDirectory(), log);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "systemctl restart mccontrol.service"),
            TimeSpan.FromSeconds(45),
            log);
        log?.Report("Door runtime repaired.");
    }

    /// <summary>
    /// After VM1 netplan + Minecraft are up: reserved play IP → VM1, door PLAYABLE.
    /// Must not force DOOR_IDLE with the IP still on the door (black hole / MOTD-only).
    /// </summary>
    private static void PromotePlayableAfterVm1(
        TofuApplyOutputs outputs,
        string keyPath,
        IProgress<string>? log)
    {
        log?.Report("Parking reserved play IP on VM1 (game is up)…");
        using var client = Connect(outputs.DoorSshHost, outputs.SshUser, keyPath);
        InstallDoorOciOrScript(client, doorSrc: ProductPaths.FindDoorVmDirectory(), log);
        Exec(
            client,
            "sudo bash /opt/mccontrol/scripts/promote_playable.sh",
            TimeSpan.FromMinutes(5),
            log);
        log?.Report("Reserved play IP is on VM1; door PLAYABLE.");
    }

    private static void InstallDoorOciOrScript(
        SshClient client,
        string? doorSrc,
        IProgress<string>? log)
    {
        if (doorSrc is null)
            return;

        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "pull_os_budget.sh"),
            "/tmp/pull_os_budget.sh",
            "/opt/mccontrol/oci/pull_os_budget.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "start_vm1.sh"),
            "/tmp/start_vm1.sh",
            "/opt/mccontrol/oci/start_vm1.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "wait_forge.sh"),
            "/tmp/wait_forge.sh",
            "/opt/mccontrol/oci/wait_forge.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "ip_to_vm1.sh"),
            "/tmp/ip_to_vm1.sh",
            "/opt/mccontrol/oci/ip_to_vm1.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "ip_to_vm2.sh"),
            "/tmp/ip_to_vm2.sh",
            "/opt/mccontrol/oci/ip_to_vm2.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "oci", "stop_vm1.sh"),
            "/tmp/stop_vm1.sh",
            "/opt/mccontrol/oci/stop_vm1.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "scripts", "promote_playable.sh"),
            "/tmp/promote_playable.sh",
            "/opt/mccontrol/scripts/promote_playable.sh",
            log);
        InstallDoorFile(
            client,
            Path.Combine(doorSrc, "scripts", "reconcile_vm1.sh"),
            "/tmp/reconcile_vm1.sh",
            "/opt/mccontrol/scripts/reconcile_vm1.sh",
            log);
    }

    private static void InstallDoorFile(
        SshClient client,
        string localPath,
        string tmpRemote,
        string destRemote,
        IProgress<string>? log)
    {
        if (!File.Exists(localPath))
            return;
        UploadFile(client, localPath, tmpRemote, log);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "sed -i 's/\\r$//' " + tmpRemote + "; "
                + "install -m 755 " + tmpRemote + " " + destRemote + "; "
                + "rm -f " + tmpRemote),
            TimeSpan.FromSeconds(20),
            log);
    }

    private static void EnsureVm1Runtime(
        TofuApplyOutputs outputs,
        string keyPath,
        IProgress<string>? log)
    {
        log?.Report("Repairing VM1 runtime (play netplan, host firewall, server.properties)…");
        using var client = Connect(outputs.Vm1SshHost, outputs.SshUser, keyPath);
        ApplyPlayNetplan(client, outputs.Vm1SecondaryPrivateIp, "vm1", log);
        EnsureVm1HostFirewall(client, log);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "systemctl reset-failed mc-boot-ledger.service 2>/dev/null || true; "
                + "systemctl start mc-boot-ledger.service 2>/dev/null || true"),
            TimeSpan.FromSeconds(20),
            log);
        var onboxStaging = UploadOnboxRepairHelpers(client, log);
        ApplyManagedProperties(client, onboxStaging, log);
        RepairPermissions(client, onboxStaging, log);
        log?.Report("VM1 runtime repaired.");
    }

    private static void ApplyPlayNetplan(
        SshClient client,
        string secondaryIp,
        string role,
        IProgress<string>? log)
    {
        if (string.IsNullOrWhiteSpace(secondaryIp)
            || !System.Net.IPAddress.TryParse(secondaryIp, out _))
            throw new InvalidOperationException($"Missing/invalid {role} secondary private IP for netplan.");

        var script = GuestPlayNetplan.BuildApplyScript(secondaryIp);
        Exec(client, "sudo bash -c " + ShQuote(script), TimeSpan.FromSeconds(45), log);
    }

    /// <summary>
    /// Oracle images ship netfilter-persistent (SSH-only REJECT) which Conflicts
    /// with firewalld. Cloud-init enables firewalld; after reboot the Oracle
    /// rules win unless netfilter-persistent is masked.
    /// </summary>
    private static void EnsureVm1HostFirewall(SshClient client, IProgress<string>? log)
    {
        log?.Report("Ensuring firewalld owns the host filter (25565 + SSH)…");
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "systemctl disable --now netfilter-persistent 2>/dev/null || true; "
                + "systemctl mask netfilter-persistent 2>/dev/null || true; "
                + "systemctl unmask firewalld 2>/dev/null || true; "
                + "systemctl enable --now firewalld; "
                + "firewall-cmd --permanent --add-service=ssh; "
                + "firewall-cmd --permanent --add-port=25565/tcp; "
                + "firewall-cmd --permanent --add-port=25565/udp; "
                + "firewall-cmd --reload"),
            TimeSpan.FromSeconds(45),
            log);
    }

    private static string UploadOnboxRepairHelpers(SshClient client, IProgress<string>? log)
    {
        var onbox = ProductPaths.FindOnboxDirectory()
            ?? throw new InvalidOperationException("Product onbox/mcmgr/ not found.");
        const string staging = "/tmp/mcmgr-onbox";
        Exec(client, $"rm -rf {staging} && mkdir -p {staging}/common", TimeSpan.FromSeconds(30), log);
        UploadFile(client, Path.Combine(onbox, "repair-server-properties.sh"), staging + "/repair-server-properties.sh", log);
        UploadFile(client, Path.Combine(onbox, "repair-permissions.sh"), staging + "/repair-permissions.sh", log);
        UploadFile(client, Path.Combine(onbox, "common", "env.sh"), staging + "/common/env.sh", log);
        UploadFile(client, Path.Combine(onbox, "common", "layout.sh"), staging + "/common/layout.sh", log);
        UploadFile(
            client,
            Path.Combine(onbox, "common", "server_properties.sh"),
            staging + "/common/server_properties.sh",
            log);
        Exec(
            client,
            "sed -i 's/\\r$//' "
            + staging + "/repair-server-properties.sh "
            + staging + "/repair-permissions.sh "
            + staging + "/common/*.sh",
            TimeSpan.FromSeconds(15),
            log);
        return staging;
    }

    private static void ApplyManagedProperties(SshClient client, string onboxStaging, IProgress<string>? log)
    {
        log?.Report("Applying managed server.properties (in-game whitelist off)…");
        Exec(
            client,
            "sudo bash " + onboxStaging + "/repair-server-properties.sh",
            TimeSpan.FromMinutes(1),
            log);
    }

    private static void RepairPermissions(SshClient client, string onboxStaging, IProgress<string>? log)
    {
        Exec(
            client,
            "sudo bash " + onboxStaging + "/repair-permissions.sh",
            TimeSpan.FromMinutes(2),
            log);
    }

    private static ServiceResult DeployVm1(
        string onbox,
        string agent,
        TofuApplyOutputs outputs,
        string keyPath,
        string minecraftVersion,
        bool eulaAccepted,
        IProgress<string>? log)
    {
        if (!eulaAccepted)
            return ServiceResult.Fail("EULA was not accepted; refusing to run bootstrap.");

        using var client = Connect(outputs.Vm1SshHost, outputs.SshUser, keyPath);

        const string agentStaging = "/tmp/mc-manager-deploy";
        log?.Report($"Idle agent src: {agent}");
        Exec(client, $"rm -rf {agentStaging} && mkdir -p {agentStaging}", TimeSpan.FromSeconds(30), log);
        UploadAgentFiles(client, agent, agentStaging, log);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "mkdir -p /opt/mc-manager /etc/mc-manager /var/lib/mc-manager; "
                + $"cp -a {agentStaging}/. /opt/mc-manager/; "
                + "sed -i 's/\\r$//' /opt/mc-manager/install.sh /opt/mc-manager/*.sh /opt/mc-manager/*.service /opt/mc-manager/*.timer 2>/dev/null || true; "
                + "bash /opt/mc-manager/install.sh"),
            TimeSpan.FromMinutes(12),
            log);

        WriteAgentConfig(client, outputs, log);

        const string onboxStaging = "/tmp/mcmgr-onbox";
        log?.Report($"onbox src: {onbox}");
        Exec(client, $"rm -rf {onboxStaging} && mkdir -p {onboxStaging}", TimeSpan.FromSeconds(30), log);
        UploadTree(client, onbox, onboxStaging, log);
        var driver =
            "set -euo pipefail; "
            + $"find {onboxStaging} -type f \\( -name '*.sh' -o -name '*.in' -o -name '*.py' \\) -exec sed -i 's/\\r$//' {{}} +; "
            + $"export EULA_ACCEPTED=true MINECRAFT_VERSION={ShQuote(minecraftVersion)} DISTRIBUTION=vanilla HOME=/home/ubuntu; "
            + $"sudo -E bash {onboxStaging}/common/driver.sh";
        Exec(client, "bash -c " + ShQuote(driver), TimeSpan.FromMinutes(20), log);

        var health = WaitRcon(client, log);
        if (!health.Succeeded)
            return health;

        log?.Report("VM1 bootstrap finished.");
        return ServiceResult.Ok();
    }

    private static void WriteAgentConfig(SshClient client, TofuApplyOutputs o, IProgress<string>? log)
    {
        var json =
            "{\n"
            + $"  \"instance_id\": {JsonString(o.Vm1InstanceId)},\n"
            + "  \"rcon_host\": \"127.0.0.1\",\n"
            + "  \"rcon_port\": 25575,\n"
            + "  \"rcon_password\": \"\",\n"
            + $"  \"shape_ocpus\": {o.Vm1Ocpus},\n"
            + $"  \"shape_memory_gb\": {o.Vm1MemoryGb},\n"
            + "  \"monthly_ocpu_target\": 1400,\n"
            + "  \"monthly_gb_target\": 8800,\n"
            + "  \"soft_ocpu_cap\": 1375,\n"
            + "  \"soft_gb_cap\": 8600,\n"
            + "  \"idle_timeout_minutes\": 15,\n"
            + "  \"budget_warn_minutes\": 5,\n"
            + "  \"idle_agent_enabled\": true,\n"
            + "  \"minecraft_unit\": \"minecraft\",\n"
            + "  \"ledger_path\": \"/var/lib/mc-manager/usage.json\",\n"
            + "  \"lease_path\": \"/var/lib/mc-manager/lease.json\",\n"
            + "  \"object_storage_enabled\": true,\n"
            + $"  \"object_storage_namespace\": {JsonString(o.ObjectStorageNamespace)},\n"
            + $"  \"object_storage_bucket\": {JsonString(o.ObjectStorageBucket)},\n"
            + "  \"object_storage_soft_cap_gb\": 9.5,\n"
            + "  \"backup_enabled\": true,\n"
            + "  \"backup_prefix\": \"backups/\",\n"
            + "  \"world_path\": \"/opt/mcmgr/server/world\"\n"
            + "}\n";
        UploadText(client, "/tmp/mc-manager-config.json", json);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "cp /tmp/mc-manager-config.json /etc/mc-manager/config.json && "
                + "chown root:root /etc/mc-manager/config.json && chmod 640 /etc/mc-manager/config.json && "
                + "rm -f /tmp/mc-manager-config.json && "
                + "systemctl enable --now mc-idle-watch.timer"),
            TimeSpan.FromSeconds(60),
            log);
    }

    private static ServiceResult WaitRcon(SshClient client, IProgress<string>? log)
    {
        for (var i = 0; i < 12; i++)
        {
            var active = ExecAllowFail(client, "systemctl is-active minecraft", TimeSpan.FromSeconds(20));
            var list = ExecAllowFail(
                client,
                "sudo python3 - <<'PY'\n"
                + "import socket,struct,sys\n"
                + "pw=open('/etc/mcmgr/rcon.secret').read().strip()\n"
                + "def pkt(k,p):\n"
                + " b=struct.pack('<ii',1,k)+p.encode()+b'\\x00\\x00'\n"
                + " return struct.pack('<i',len(b))+b\n"
                + "s=socket.create_connection(('127.0.0.1',25575),timeout=5)\n"
                + "s.sendall(pkt(3,pw)); s.recv(4096)\n"
                + "s.sendall(pkt(2,'list')); data=s.recv(4096); s.close()\n"
                + "sys.stdout.buffer.write(data); sys.exit(0 if data else 1)\n"
                + "PY",
                TimeSpan.FromSeconds(20));
            if (list.Contains("players", StringComparison.OrdinalIgnoreCase)
                || list.Contains("There are", StringComparison.OrdinalIgnoreCase))
            {
                log?.Report("RCON list succeeded.");
                return ServiceResult.Ok();
            }

            log?.Report($"RCON not ready yet (minecraft={active.Trim()}); retry {i + 1}/12…");
            Thread.Sleep(TimeSpan.FromSeconds(10));
        }

        return ServiceResult.Fail(
            "Minecraft unit started but RCON list did not succeed in time. Re-Deploy can resume on-box stages.");
    }

    private static void UploadAgentFiles(SshClient client, string agent, string staging, IProgress<string>? log)
    {
        string[] files =
        [
            "idle_watch.py", "ledger.py", "lease.py", "shape_detect.py", "rcon_client.py",
            "os_publish.py", "world_backup.py", "graceful_stop.sh", "record_boot.py",
            "install.sh", "config.example.json",
        ];
        foreach (var name in files)
        {
            var local = Path.Combine(agent, name);
            if (File.Exists(local))
                UploadFile(client, local, staging + "/" + Path.GetFileName(name), log);
        }

        foreach (var unit in new[] { "mc-idle-watch.service", "mc-idle-watch.timer", "mc-boot-ledger.service" })
        {
            var local = Path.Combine(agent, "systemd", unit);
            if (File.Exists(local))
                UploadFile(client, local, staging + "/" + unit, log);
        }
    }

    private static SshClient Connect(string host, string user, string keyPath)
    {
        keyPath = LocalConfigStore.ExpandPath(keyPath);
        if (!File.Exists(keyPath))
            throw new InvalidOperationException($"SSH key not found: {keyPath}");
        var client = new SshClient(host, user, new PrivateKeyFile(keyPath));
        client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(30);
        client.Connect();
        return client;
    }

    private static ServiceResult<string> RunOnce(
        string host,
        string user,
        string keyPath,
        string command,
        TimeSpan timeout)
    {
        try
        {
            using var client = Connect(host, user, keyPath);
            var text = ExecAllowFail(client, command, timeout);
            return ServiceResult<string>.Ok(text.Trim());
        }
        catch (Exception ex)
        {
            return ServiceResult<string>.Fail(ex.Message);
        }
    }

    private static void Exec(SshClient client, string command, TimeSpan timeout, IProgress<string>? log)
    {
        log?.Report("> " + Truncate(command, 180));
        var cmd = client.CreateCommand(command);
        cmd.CommandTimeout = timeout;
        var result = cmd.Execute();
        var err = cmd.Error ?? "";
        if (!string.IsNullOrWhiteSpace(result))
            log?.Report(Truncate(result.Trim(), 2000));
        if (!string.IsNullOrWhiteSpace(err))
            log?.Report(Truncate(err.Trim(), 2000));
        if (cmd.ExitStatus != 0)
            throw new InvalidOperationException($"SSH command failed (exit {cmd.ExitStatus}): {err.Trim()}\n{result}");
    }

    private static string ExecAllowFail(SshClient client, string command, TimeSpan timeout)
    {
        try
        {
            var cmd = client.CreateCommand(command);
            cmd.CommandTimeout = timeout;
            var result = cmd.Execute();
            return (result ?? "") + (cmd.Error ?? "");
        }
        catch (SshOperationTimeoutException)
        {
            return "";
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static void UploadTree(SshClient client, string localDir, string remoteDir, IProgress<string>? log)
    {
        using var sftp = new SftpClient(client.ConnectionInfo);
        sftp.Connect();
        foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(localDir, file).Replace('\\', '/');
            if (ShouldSkip(rel))
                continue;
            var remote = remoteDir + "/" + rel;
            var parent = remote[..remote.LastIndexOf('/')];
            EnsureRemoteDir(sftp, parent);
            UploadFile(sftp, file, remote);
        }

        log?.Report($"uploaded {localDir} → {remoteDir}");
    }

    private static void UploadFile(SshClient client, string local, string remote, IProgress<string>? log)
    {
        using var sftp = new SftpClient(client.ConnectionInfo);
        sftp.Connect();
        var parent = remote[..remote.LastIndexOf('/')];
        EnsureRemoteDir(sftp, parent);
        UploadFile(sftp, local, remote);
        log?.Report($"put {Path.GetFileName(local)}");
    }

    private static void UploadFile(SftpClient sftp, string local, string remote)
    {
        if (IsTextPath(local))
        {
            var text = File.ReadAllText(local).Replace("\r\n", "\n").Replace("\r", "\n");
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
            sftp.UploadFile(ms, remote, canOverride: true);
            return;
        }

        using var fs = File.OpenRead(local);
        sftp.UploadFile(fs, remote, canOverride: true);
    }

    private static void UploadText(SshClient client, string remote, string content)
    {
        using var sftp = new SftpClient(client.ConnectionInfo);
        sftp.Connect();
        var parent = remote[..remote.LastIndexOf('/')];
        EnsureRemoteDir(sftp, parent);
        var text = content.Replace("\r\n", "\n").Replace("\r", "\n");
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(text));
        sftp.UploadFile(ms, remote, canOverride: true);
    }

    private static void EnsureRemoteDir(SftpClient sftp, string dir)
    {
        var parts = dir.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var cur = dir.StartsWith('/') ? "" : "";
        foreach (var p in parts)
        {
            cur += "/" + p;
            if (!sftp.Exists(cur))
                sftp.CreateDirectory(cur);
        }
    }

    private static bool ShouldSkip(string rel)
    {
        var n = rel.Replace('\\', '/');
        return n.StartsWith(".git/", StringComparison.Ordinal)
            || n.Contains("/.git/", StringComparison.Ordinal)
            || n.Contains("/bin/", StringComparison.Ordinal)
            || n.Contains("/obj/", StringComparison.Ordinal)
            || n.Contains("/__pycache__/", StringComparison.Ordinal)
            || n.Contains("/tests/", StringComparison.Ordinal);
    }

    private static bool IsTextPath(string path)
    {
        var name = Path.GetFileName(path);
        if (name.Equals("Makefile", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Dockerfile", StringComparison.OrdinalIgnoreCase))
            return true;
        var ext = Path.GetExtension(path);
        return ext is ".sh" or ".py" or ".c" or ".h" or ".json" or ".service" or ".timer"
            or ".in" or ".txt" or ".md" or ".yml" or ".yaml" or ".env" or ".example";
    }

    /// <summary>
    /// Probe as root. The VM1 marker lives under <c>/etc/mcmgr</c> (0750 root:mcmgr);
    /// SSH user ubuntu is not in that group (SETUP-ISSUE-5). <c>sudo -n</c> avoids a
    /// password prompt hang on non-interactive SSH.
    /// </summary>
    internal static string CloudInitProbeCommand(string remoteMarker) =>
        $"sudo -n test -f {ShQuote(remoteMarker)} && echo OK || echo WAIT";

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string JsonString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
