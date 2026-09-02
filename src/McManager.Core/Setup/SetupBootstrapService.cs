using System.IO.Compression;
using System.Text;
using System.Text.Json;
using McManager.Core.Config;
using McManager.Core.Onbox;
using McManager.Core.Services;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace McManager.Core.Setup;

/// <summary>SSH wait/cloud-init + door / onbox / idle-agent upload.</summary>
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
            if (probe.Succeeded && MarkerProbeReady(probe.Value))
            {
                log?.Report($"cloud-init ready: {remoteMarker}");
                return ServiceResult.Ok();
            }

            if (probe.Succeeded && CloudInitFinishedWithoutMarker(probe.Value))
            {
                log?.Report(
                    $"cloud-init finished without {remoteMarker} (likely invalid #cloud-config). "
                    + "Continuing; guest repair applies OS baseline (SETUP-ISSUE-10).");
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

        var key = TofuApplyOutputs.DoorPrivateKeyPath(state);
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
        var eula = state.EulaAccepted;
        return Task.Run(
            () =>
            {
                try
                {
                    return DeployVm1(onbox, agent, outputs, key, state, eula, log);
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail("VM1 bootstrap failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// Day-2 full pack replace (blueprint §28.1). Stops Minecraft, clears the previous game
    /// install, re-runs Setup bootstrap + pack copy, starts, health-checks. Keeps the world
    /// unless <see cref="PackReplaceRequest.WipeWorld"/>. Does not redeploy the idle agent.
    /// </summary>
    public Task<ServiceResult<PackReplaceResult>> ReplacePackAsync(
        Vm1Settings vm1,
        PackReplaceRequest request,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(vm1);
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(
            () =>
            {
                try
                {
                    return ReplacePack(vm1, request, log);
                }
                catch (Exception ex)
                {
                    return ServiceResult<PackReplaceResult>.Fail("Pack replace failed: " + ex.Message);
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
        var vm1Key = TofuApplyOutputs.PrivateKeyPath(state);
        var doorKey = TofuApplyOutputs.DoorPrivateKeyPath(state);
        return Task.Run(
            () =>
            {
                try
                {
                    EnsureDoorRuntime(outputs, doorKey, log);
                    EnsureVm1Runtime(outputs, vm1Key, log);
                    PromotePlayableAfterVm1(outputs, doorKey, log);
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
    /// Restart Minecraft after Setup seeds <c>messages/chat.json</c> and the list icon.
    /// <c>record_boot</c> applies identity Before=minecraft; without this restart Paper
    /// and vanilla create never write <c>/opt/mcmgr/server/server-icon.png</c>.
    /// </summary>
    public Task<ServiceResult> RestartMinecraftForIdentityAsync(
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
                    return RestartMinecraftForIdentity(outputs, key, log);
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail(
                        "Minecraft restart after identity seed failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    /// <summary>
    /// After VM1 netplan + Minecraft are up: reserved play IP → VM1, door PLAYABLE.
    /// Must not force DOOR_IDLE with the IP still on the door (black hole / MOTD-only).
    /// Call again after a spend-brake Function tofu apply — that apply used to move
    /// the reserved IP back to the door (SETUP-ISSUE-15).
    /// </summary>
    public Task<ServiceResult> PromotePlayableAfterVm1Async(
        TofuApplyOutputs outputs,
        SetupWizardState state,
        IProgress<string>? log,
        CancellationToken cancellationToken = default)
    {
        var key = TofuApplyOutputs.DoorPrivateKeyPath(state);
        return Task.Run(
            () =>
            {
                try
                {
                    PromotePlayableAfterVm1(outputs, key, log);
                    return ServiceResult.Ok();
                }
                catch (Exception ex)
                {
                    return ServiceResult.Fail("Parking reserved play IP failed: " + ex.Message);
                }
            },
            cancellationToken);
    }

    private static ServiceResult RestartMinecraftForIdentity(
        TofuApplyOutputs outputs,
        string keyPath,
        IProgress<string>? log)
    {
        using var client = Connect(outputs.Vm1SshHost, outputs.SshUser, keyPath);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "HOME=\"${HOME:-/home/ubuntu}\"; "
                + "systemctl restart minecraft"),
            TimeSpan.FromMinutes(3),
            log);
        return WaitRcon(client, log);
    }

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
    /// Canonical Ubuntu also enables ufw.service (even with ENABLED=no). Product
    /// SoT is firewalld-only. Distro firewalld Before=network-pre.target races
    /// cloud-init/dbus (Debian #1025618); a full unit override (not a drop-in)
    /// omits that Before= so systemd does not delete dbus at boot (OS-ISSUE-9).
    /// </summary>
    private static void EnsureVm1HostFirewall(SshClient client, IProgress<string>? log)
    {
        log?.Report("Ensuring firewalld owns the host filter (25565 + SSH)…");
        var infra = ProductPaths.FindInfraDirectory()
            ?? throw new InvalidOperationException("Product infra/ not found.");
        var unitPath = Path.Combine(infra, "cloud-init", "firewalld-mcmgr.service");
        if (!File.Exists(unitPath))
            throw new InvalidOperationException("Missing infra/cloud-init/firewalld-mcmgr.service.");
        var unit = File.ReadAllText(unitPath).Replace("\r\n", "\n", StringComparison.Ordinal);
        if (!unit.EndsWith('\n'))
            unit += "\n";

        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "export DEBIAN_FRONTEND=noninteractive; "
                + "apt-get update -qq; "
                + "apt-get install -y -qq firewalld unzip jq curl ca-certificates gnupg; "
                + "systemctl disable --now netfilter-persistent 2>/dev/null || true; "
                + "systemctl mask netfilter-persistent 2>/dev/null || true; "
                + "ufw --force disable 2>/dev/null || true; "
                + "systemctl disable ufw 2>/dev/null || true; "
                + "systemctl mask ufw 2>/dev/null || true; "
                + "rm -rf /etc/systemd/system/firewalld.service.d; "
                + "cat > /etc/systemd/system/firewalld.service <<'EOF'\n"
                + unit
                + "EOF\n"
                + "rm -f /etc/systemd/system/dbus-org.fedoraproject.FirewallD1.service; "
                + "systemctl daemon-reload; "
                + "systemctl unmask firewalld 2>/dev/null || true; "
                + "systemctl enable --now firewalld; "
                + "firewall-cmd --permanent --add-service=ssh; "
                + "firewall-cmd --permanent --add-port=25565/tcp; "
                + "firewall-cmd --permanent --add-port=25565/udp; "
                + "firewall-cmd --reload; "
                + "mkdir -p /etc/mcmgr; "
                + "if [ ! -f /etc/mcmgr/cloud-init-done ]; then date -u +%Y-%m-%dT%H:%M:%SZ > /etc/mcmgr/cloud-init-done; fi"),
            TimeSpan.FromMinutes(8),
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
        SetupWizardState state,
        bool eulaAccepted,
        IProgress<string>? log)
    {
        if (!eulaAccepted)
            return ServiceResult.Fail("EULA was not accepted; refusing to run bootstrap.");

        var minecraftVersion = state.MinecraftVersion.Trim();
        var dist = SetupPackImport.ToDistribution(state);
        if (!SetupPackImport.IsOnboxDistribution(dist))
        {
            return ServiceResult.Fail(
                "Setup cannot bootstrap this game type (need Vanilla, Paper, Fabric, Forge, or NeoForge).");
        }

        if (SetupServerType.IsModded(state.ServerType)
            && (string.IsNullOrWhiteSpace(state.PackPath) || !File.Exists(state.PackPath)))
        {
            return ServiceResult.Fail(
                "Modded Setup needs the pack file you confirmed. The original path is missing — pick the pack again.");
        }

        using var client = Connect(outputs.Vm1SshHost, outputs.SshUser, keyPath);
        EnsureVm1HostFirewall(client, log);

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
        log?.Report($"onbox src: {onbox} DISTRIBUTION={dist} MINECRAFT_VERSION={minecraftVersion}");
        Exec(client, $"rm -rf {onboxStaging} && mkdir -p {onboxStaging}", TimeSpan.FromSeconds(30), log);
        UploadTree(client, onbox, onboxStaging, log);
        RunOnboxDriver(client, onboxStaging, state, analyzedJavaMajor: null, log);

        if (SetupServerType.IsModded(state.ServerType))
        {
            var pack = InstallModdedPack(client, onboxStaging, state, keepWorld: false, log);
            if (!pack.Succeeded)
                return pack;
        }

        var health = WaitRcon(client, log);
        if (!health.Succeeded)
            return health;

        if (!string.IsNullOrWhiteSpace(health.Warning))
            log?.Report(health.Warning);

        log?.Report("VM1 bootstrap finished.");
        return health;
    }

    private static ServiceResult<PackReplaceResult> ReplacePack(
        Vm1Settings vm1,
        PackReplaceRequest request,
        IProgress<string>? log)
    {
        var onbox = ProductPaths.FindOnboxDirectory();
        if (onbox is null)
            return ServiceResult<PackReplaceResult>.Fail("Product onbox/mcmgr/ not found.");

        if (string.IsNullOrWhiteSpace(vm1.SshHost))
            return ServiceResult<PackReplaceResult>.Fail("VM1 SSH host is missing.");

        var user = string.IsNullOrWhiteSpace(vm1.SshUser) ? "ubuntu" : vm1.SshUser.Trim();
        var analysis = SetupPackImport.AnalyzeFile(request.PackPath);
        if (!analysis.Succeeded)
            return ServiceResult<PackReplaceResult>.Fail(analysis.Error!);

        var preview = analysis.Value!;
        if (!preview.CanContinue)
        {
            return ServiceResult<PackReplaceResult>.Fail(
                preview.BlockReason ?? "This pack cannot be installed.");
        }

        var state = PackReplacePlanner.ToWizardState(preview);
        state.JvmXmx = JvmHeapChoice.Normalize(vm1.JvmXmx);
        var dist = SetupPackImport.ToDistribution(state);
        if (!SetupPackImport.IsOnboxDistribution(dist))
        {
            return ServiceResult<PackReplaceResult>.Fail(
                "Pack replace needs a Fabric, Forge, or NeoForge pack.");
        }

        using var client = Connect(vm1.SshHost, user, vm1.SshKeyPath);
        TryReadCurrentGame(client, out var currentMc, out var currentLoader);
        var warning = request.WipeWorld
            ? null
            : PackReplaceSaveCompatibility.Warn(
                currentMc,
                currentLoader,
                preview.MinecraftVersion,
                preview.Loader);
        if (!string.IsNullOrWhiteSpace(warning))
            log?.Report(warning);

        const string onboxStaging = "/tmp/mcmgr-onbox";
        log?.Report(
            $"Pack replace: {preview.PackName} DISTRIBUTION={dist} "
            + $"MINECRAFT_VERSION={preview.MinecraftVersion} wipe_world={request.WipeWorld}");
        Exec(client, $"rm -rf {onboxStaging} && mkdir -p {onboxStaging}", TimeSpan.FromSeconds(30), log);
        UploadTree(client, onbox, onboxStaging, log);

        var keep = request.WipeWorld ? "0" : "1";
        var wipe = request.WipeWorld ? "1" : "0";
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "HOME=\"${HOME:-/home/ubuntu}\"; "
                + $"KEEP_WORLD={keep} WIPE_WORLD={wipe} "
                + $"bash {onboxStaging}/prepare-pack-replace.sh"),
            TimeSpan.FromMinutes(3),
            log);

        RunOnboxDriver(client, onboxStaging, state, preview.JavaMajor, log);

        var pack = InstallModdedPack(
            client,
            onboxStaging,
            state,
            keepWorld: !request.WipeWorld,
            log,
            request.DataDirectory);
        if (!pack.Succeeded)
            return ServiceResult<PackReplaceResult>.Fail(pack.Error ?? "Pack copy failed.");

        var health = WaitRcon(client, log);
        if (!health.Succeeded)
            return ServiceResult<PackReplaceResult>.Fail(health.Error ?? "RCON health check failed.");

        if (!string.IsNullOrWhiteSpace(health.Warning))
            log?.Report(health.Warning);

        log?.Report("Pack replace finished.");
        return ServiceResult<PackReplaceResult>.Ok(
            new PackReplaceResult(
                preview.PackName,
                preview.MinecraftVersion,
                preview.Loader,
                request.WipeWorld,
                warning,
                health.Warning));
    }

    private static void RunOnboxDriver(
        SshClient client,
        string onboxStaging,
        SetupWizardState state,
        int? analyzedJavaMajor,
        IProgress<string>? log)
    {
        var exports = OnboxDriverExports.Build(state, analyzedJavaMajor);
        var driver =
            "set -euo pipefail; "
            + $"find {onboxStaging} -type f \\( -name '*.sh' -o -name '*.in' -o -name '*.py' \\) -exec sed -i 's/\\r$//' {{}} +; "
            + $"{exports}; "
            + $"sudo -E bash {onboxStaging}/common/driver.sh";
        Exec(client, "bash -c " + ShQuote(driver), TimeSpan.FromMinutes(20), log);
    }

    private static void TryReadCurrentGame(
        SshClient client,
        out string? minecraftVersion,
        out string? loaderOrDistribution)
    {
        minecraftVersion = null;
        loaderOrDistribution = null;
        var text = ExecAllowFail(
            client,
            "sudo cat /etc/mcmgr/game-manifest.json",
            TimeSpan.FromSeconds(20));
        if (string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            if (root.TryGetProperty("minecraft_version", out var mc)
                && mc.ValueKind == JsonValueKind.String)
            {
                var s = mc.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                    minecraftVersion = s.Trim();
            }

            string? loader = null;
            if (root.TryGetProperty("loader", out var loaderEl)
                && loaderEl.ValueKind == JsonValueKind.String)
            {
                loader = loaderEl.GetString();
            }

            string? dist = null;
            if (root.TryGetProperty("distribution", out var distEl)
                && distEl.ValueKind == JsonValueKind.String)
            {
                dist = distEl.GetString();
            }

            if (!string.IsNullOrWhiteSpace(loader)
                && !loader.Equals("none", StringComparison.OrdinalIgnoreCase)
                && !loader.Equals("vanilla", StringComparison.OrdinalIgnoreCase))
            {
                loaderOrDistribution = loader.Trim();
            }
            else if (!string.IsNullOrWhiteSpace(dist))
            {
                loaderOrDistribution = dist.Trim();
            }
        }
        catch (JsonException)
        {
            // Warning is best-effort; replace can still proceed.
        }
    }

    private static ServiceResult InstallModdedPack(
        SshClient client,
        string onboxStaging,
        SetupWizardState state,
        bool keepWorld,
        IProgress<string>? log,
        string? dataDirectory = null)
    {
        var dataDir = dataDirectory ?? LocalConfigStore.TryFindDataDirectory();
        var dest = Path.Combine(Path.GetTempPath(), "mcmgr-setup-pack-" + Guid.NewGuid().ToString("N"));
        log?.Report("Installing server-side pack files on this PC, then copying them to the game VM…");

        try
        {
            if (ShouldUseManualInstaller(state.PackPath, state.PackKind))
            {
                var result = ManualServerPackInstaller.Install(state.PackPath, dest, dataDir);
                if (!result.Succeeded)
                    return ServiceResult.Fail(result.Error ?? "Server-pack zip install failed.");
                log?.Report(result.Value!.Summary);
            }
            else if (string.Equals(state.PackKind, SetupPackImport.KindMrpack, StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(state.PackPath).Equals(".mrpack", StringComparison.OrdinalIgnoreCase))
            {
                var installer = MrpackInstaller.Create(state.PackPath, dataDir);
                var result = installer.InstallAsync(state.PackPath, dest, dataDir).GetAwaiter().GetResult();
                if (!result.Succeeded)
                    return ServiceResult.Fail(result.Error ?? "Modrinth pack install failed.");
                log?.Report(result.Value!.Summary);
            }
            else
            {
                var result = ManualServerPackInstaller.Install(state.PackPath, dest, dataDir);
                if (!result.Succeeded)
                    return ServiceResult.Fail(result.Error ?? "Server-pack zip install failed.");
                log?.Report(result.Value!.Summary);
            }

            return CopyStagedPackToVm1(client, onboxStaging, dest, keepWorld, log);
        }
        finally
        {
            try
            {
                if (Directory.Exists(dest))
                    Directory.Delete(dest, recursive: true);
            }
            catch (IOException)
            {
                // temp leftover is fine
            }
        }
    }

    private static ServiceResult CopyStagedPackToVm1(
        SshClient client,
        string onboxStaging,
        string localDest,
        bool keepWorld,
        IProgress<string>? log)
    {
        const string remoteStaging = "/tmp/mcmgr-pack";
        Exec(client, $"rm -rf {remoteStaging} && mkdir -p {remoteStaging}", TimeSpan.FromSeconds(30), log);
        UploadPackTree(client, localDest, remoteStaging, keepWorld, log);
        Exec(
            client,
            "sudo bash -c " + ShQuote(
                "set -euo pipefail; "
                + "HOME=\"${HOME:-/home/ubuntu}\"; "
                + "systemctl stop minecraft || true; "
                + $"cp -a {remoteStaging}/. /opt/mcmgr/server/; "
                + $"bash {onboxStaging}/repair-permissions.sh; "
                + "systemctl start minecraft"),
            TimeSpan.FromMinutes(10),
            log);
        log?.Report(
            keepWorld
                ? "Copied server-side pack files to /opt/mcmgr/server (kept world, eula.txt, and server.properties)."
                : "Copied server-side pack files to /opt/mcmgr/server (kept bootstrap eula.txt and server.properties).");
        return ServiceResult.Ok();
    }

    private static void UploadPackTree(
        SshClient client,
        string localDir,
        string remoteDir,
        bool keepWorld,
        IProgress<string>? log)
    {
        using var sftp = new SftpClient(client.ConnectionInfo);
        sftp.Connect();
        var skipped = 0;
        var uploaded = 0;
        foreach (var file in Directory.EnumerateFiles(localDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(localDir, file).Replace('\\', '/');
            var name = Path.GetFileName(file);
            if (name.Equals("eula.txt", StringComparison.OrdinalIgnoreCase)
                || name.Equals("server.properties", StringComparison.OrdinalIgnoreCase)
                || (keepWorld && PackReplaceSaveCompatibility.IsWorldOverlayRelative(rel)))
            {
                skipped++;
                continue;
            }

            var remote = remoteDir + "/" + rel;
            var parent = remote[..remote.LastIndexOf('/')];
            EnsureRemoteDir(sftp, parent);
            UploadFile(sftp, file, remote);
            uploaded++;
        }

        log?.Report($"uploaded pack files ({uploaded} files, skipped {skipped} eula/properties/world) → {remoteDir}");
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
        var sinceRaw = ExecAllowFail(
            client,
            MinecraftReadiness.JournalSinceDateCommand,
            TimeSpan.FromSeconds(20)).Trim();
        var since = MinecraftReadiness.IsSafeSinceTimestamp(sinceRaw) ? sinceRaw : null;
        MinecraftHealthProbe? last = null;
        var quarantined = false;
        string? quarantineNotice = null;
        string? quarantinePath = null;
        var likelyClient = false;
        string? blamedMod = null;

        for (var i = 0; i < MinecraftReadiness.MaxRconAttempts; i++)
        {
            var blob = ExecAllowFail(
                client,
                MinecraftReadiness.ProbeCommand(since),
                TimeSpan.FromSeconds(30));
            last = MinecraftReadiness.ParseProbe(blob);
            var report = MinecraftReadiness.Classify(last);

            if (report.Kind == MinecraftReadinessKind.Joinable)
            {
                log?.Report(MinecraftReadiness.JoinableLog);
                if (quarantined && !string.IsNullOrWhiteSpace(quarantinePath))
                {
                    ExecAllowFail(
                        client,
                        CrashQuarantine.RemoteCommand("set-retry", relativePath: quarantinePath),
                        TimeSpan.FromSeconds(30));
                    quarantineNotice = CrashQuarantine.NotifyMessage(
                        blamedMod ?? "a mod",
                        quarantinePath,
                        likelyClient,
                        retrySucceeded: true);
                    log?.Report(quarantineNotice);
                }

                return ServiceResult.Ok(quarantineNotice);
            }

            if (report.Kind == MinecraftReadinessKind.Crash)
            {
                if (quarantined)
                {
                    log?.Report(MinecraftReadiness.CrashDetectedLog);
                    ExecAllowFail(
                        client,
                        MinecraftReadiness.StopUnitCommand,
                        TimeSpan.FromSeconds(45));
                    var fail = MinecraftReadiness.FormatCrashMessage(report);
                    if (!string.IsNullOrWhiteSpace(quarantineNotice))
                        fail = quarantineNotice + "\n\n" + fail;
                    else if (!string.IsNullOrWhiteSpace(blamedMod))
                    {
                        fail = CrashQuarantine.NotifyMessage(
                            blamedMod,
                            quarantinePath ?? "",
                            likelyClient,
                            retrySucceeded: false)
                            + "\n\n" + fail;
                    }

                    return ServiceResult.Fail(fail);
                }

                var crashReport = ReadCrashReport(client);
                var blame = CrashModAttributor.TryExactlyOne(last.Journal, crashReport);
                if (blame is null)
                {
                    log?.Report(MinecraftReadiness.CrashDetectedLog);
                    ExecAllowFail(
                        client,
                        MinecraftReadiness.StopUnitCommand,
                        TimeSpan.FromSeconds(45));
                    return ServiceResult.Fail(MinecraftReadiness.FormatCrashMessage(report));
                }

                log?.Report(CrashQuarantine.MovingLog);
                var moved = CrashQuarantine.ParseRemote(
                    ExecAllowFail(
                        client,
                        CrashQuarantine.RemoteCommand(
                            "move",
                            modId: blame.ModId,
                            jarFileName: blame.JarFileName),
                        TimeSpan.FromSeconds(60)));
                if (!moved.Ok)
                {
                    log?.Report(MinecraftReadiness.CrashDetectedLog);
                    ExecAllowFail(
                        client,
                        MinecraftReadiness.StopUnitCommand,
                        TimeSpan.FromSeconds(45));
                    return ServiceResult.Fail(MinecraftReadiness.FormatCrashMessage(report));
                }

                quarantined = true;
                blamedMod = moved.ModId ?? blame.ModId;
                quarantinePath = moved.Path ?? CrashQuarantine.NormalizeRelative(blame.JarFileName);
                likelyClient = moved.LikelyClientOnly;
                quarantineNotice = CrashQuarantine.NotifyMessage(
                    blamedMod,
                    quarantinePath ?? "",
                    likelyClient,
                    retrySucceeded: false);
                log?.Report(CrashQuarantine.RetryingLog);

                var sinceAfter = ExecAllowFail(
                    client,
                    MinecraftReadiness.JournalSinceDateCommand,
                    TimeSpan.FromSeconds(20)).Trim();
                since = MinecraftReadiness.IsSafeSinceTimestamp(sinceAfter) ? sinceAfter : since;
                i = -1;
                continue;
            }

            log?.Report(MinecraftReadiness.StillStartingLog(i + 1, last));
            Thread.Sleep(MinecraftReadiness.RetryDelay);
        }

        if (quarantined && !string.IsNullOrWhiteSpace(quarantineNotice))
            return ServiceResult.Fail(quarantineNotice + "\n\n" + MinecraftReadiness.FormatTimeoutMessage(last));
        return ServiceResult.Fail(MinecraftReadiness.FormatTimeoutMessage(last));
    }

    private static string? ReadCrashReport(SshClient client)
    {
        var parsed = CrashQuarantine.ParseRemote(
            ExecAllowFail(
                client,
                CrashQuarantine.RemoteCommand("read-crash"),
                TimeSpan.FromSeconds(30)));
        return parsed.Ok ? parsed.CrashReport : null;
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
    /// password prompt hang on non-interactive SSH. Also prints <c>cloud-init status</c>
    /// so a YAML-parse skip (SETUP-ISSUE-10) does not wait 20 minutes.
    /// </summary>
    internal static string CloudInitProbeCommand(string remoteMarker) =>
        $"sudo -n test -f {ShQuote(remoteMarker)} && echo MARKER_OK || echo MARKER_WAIT; "
        + "sudo -n cloud-init status 2>/dev/null || true";

    internal static bool MarkerProbeReady(string? stdout) =>
        (stdout ?? "").Contains("MARKER_OK", StringComparison.Ordinal);

    /// <summary>
    /// Cloud-init reached <c>done</c> but never wrote the marker — typically invalid
    /// #cloud-config so runcmd never ran. Guest repair applies the OS baseline.
    /// </summary>
    internal static bool CloudInitFinishedWithoutMarker(string? stdout)
    {
        var text = stdout ?? "";
        if (text.Contains("MARKER_OK", StringComparison.Ordinal))
            return false;
        if (!text.Contains("MARKER_WAIT", StringComparison.Ordinal))
            return false;
        return text.Contains("status: done", StringComparison.OrdinalIgnoreCase);
    }

    private static string ShQuote(string value) => "'" + value.Replace("'", "'\\''") + "'";

    private static string JsonString(string value) =>
        "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    internal static bool ShouldUseManualInstaller(string packPath, string? packKind)
    {
        if (string.Equals(packKind, SetupPackImport.KindManualZip, StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.IsNullOrWhiteSpace(packPath) || !File.Exists(packPath))
            return false;
        if (!packPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            using var stream = File.OpenRead(packPath);
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
            return zip.Entries.Any(e =>
            {
                var n = MrpackAnalyzer.NormalizeZipPath(e.FullName);
                return string.Equals(n, DerivedPackIdentity.SidecarEntryName, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
