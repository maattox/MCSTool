# Operator troubleshooting commands (VM1 + door)

**Audience:** operator (and anyone pasting output to an AI agent).  
**Not secrets:** no live OCIDs, IPs, or passwords here. Use values from your own SSH session or gitignored config.

Copy a command from a fenced block, run it on the **correct VM**, then paste the **full output** (plus which host you were on) into chat.

Related: [`Issues.md`](Issues.md) (known bugs), [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md), [`VM-Software.md`](VM-Software.md), [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) (agents). Manager **Troubleshooting** tab (MVP Step **4.4**) wraps the commands below. **Delete infrastructure** is on **Advanced / Danger Zone**, not this tab.

---

## Manager Troubleshooting buttons → commands

Dedicated tab (not Danger Zone). Mutating actions are confirm-gated; output is in the result log (**Copy**). Park-IP variant: if VM1 is **RUNNING**, assign the reserved public IP to VM1’s secondary; otherwise start the door if needed and assign it to the door secondary. Already-on-target is success.

Door OCI scripts are run **as root** with `oci.env` sourced (mode 600; `ubuntu` cannot). `ip_to_vm*.sh` ignore a `--force` argv; they already pass OCI `--force` when a move is needed.

| Button | Host | What it runs |
|--------|------|----------------|
| **Park reserved play IP** | OCI + door | `GetInstance` VM1; if door not `RUNNING`, `InstanceAction START` + wait `RUNNING`; then on door: `sudo bash -c 'set -a; source <(tr -d "\r" < /etc/mccontrol/oci.env); set +a; export HOME="${HOME:-/home/ubuntu}"; export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"; bash -- /opt/mccontrol/oci/ip_to_vm1.sh'` **or** `…/ip_to_vm2.sh` |
| **Re-apply play netplan** | VM1 and/or door if `RUNNING` | Same `99-mcmgr-play.yaml` as Setup (`sudo bash -c` + `netplan apply`) using `secondary_private_ip` from local config |
| **Diagnose wait_forge** | door | `sudo bash /opt/mccontrol/scripts/diagnose_wait_forge.sh` (read-only; no confirm) |
| **Reset door state** | door | `sudo bash /opt/mccontrol/scripts/reset_door_state.sh` (does **not** move the IP) |
| **Unstick after game is up** | door | `sudo bash /opt/mccontrol/scripts/unstick_after_forge_ready.sh` |
| **Refresh door OS budget** | door | `POST http://<door.ssh_host>:8080/api/os-refresh`; on failure: `sudo bash -c '…source oci.env…; bash -- /opt/mccontrol/oci/pull_os_budget.sh --force'` |
| **Heal open ledger** | OCI + door | Refuses unless VM1 lifecycle is **STOPPED**; then `sudo bash -c '…source oci.env…; bash -- /opt/mccontrol/oci/heal_os_ledger.sh'` |
| **Idle timer status** | VM1 | `systemctl` status for `minecraft` + `mc-idle-watch.timer` / `.service` (read-only) |
| **Force-enable idle timer** | VM1 | `sudo systemctl enable --now` equivalent: `enable` + `start` `mc-idle-watch.timer` (does **not** start Minecraft; OS-ISSUE-7 still applies on next boot) |
| **Minecraft CHDIR diagnosis** | VM1 | `journalctl -u minecraft -n 80`; `systemctl show`; `namei -l` on `WorkingDirectory` (read-only) |
| **Repair game permissions** | VM1 | `sudo bash /opt/mcmgr/bin/repair-permissions.sh` if present, else upload product `onbox/mcmgr/` to `/tmp/mcmgr-onbox` and run that. Same §5 contract as Step 4.2. Does **not** start Minecraft |
| *(copy only)* OS-ISSUE-5 | Console | Guest ACPI SoftStop hang — OCI Console Reset / Force stop; then Park play IP; heal only after `STOPPED` |
| **Delete infrastructure** (Advanced / Danger Zone) | admin PC + OCI | Typed `confirm`; empty product bucket + OCIR `mcmgr-fn/softstop` images; temporary bucket `prevent_destroy` override; `tofu destroy -auto-approve` against `%LOCALAPPDATA%\McManager\tofu\<stack-id>\`; then delete `config.local.json` + `setup-wizard.local.json` + that tofu folder. Does **not** delete the tenancy or `friends.local.json` / `~/.oci` / SSH keys. Window stays open until tofu returns. |

Minecraft **Restart** stays on the top bar (not duplicated).

---

## Permissions (`ubuntu` vs `sudo` vs systemd `mcmgr`)

This has bitten **multiple** sessions.

**SSH** lands as `ubuntu`. Many product files are **root-owned** (`/etc/mccontrol/oci.env` is mode **600 root**; `/etc/mc-manager/`, `/etc/mcmgr/`, `/opt/mcmgr/`, systemd units, and most `/opt/mccontrol/` scripts need root to read or change).

- If you see `Permission denied` in an SSH session, **do not keep retrying as `ubuntu`**. Re-run with `sudo`, or check `ls -l` on the path.
- Do **not** `source /etc/mccontrol/oci.env` as `ubuntu` — that failure is expected.
- Diagnose scripts under `/opt/mccontrol/scripts/` should be run as **`sudo bash …`**.

**Minecraft systemd** runs as **`mcmgr`**, not `ubuntu`. A path `ubuntu` can `cd` into can still be impossible for `mcmgr` (SETUP-ISSUE-4: `status=200/CHDIR`). Diagnose with the CHDIR block below. Do not `chmod 0777` or run the game as `ubuntu` as a “fix.”

---

## Which VM?

| Host | Typical hostname (product Setup) | Role |
|------|----------------------------------|------|
| **VM1** | `mcmgr-vm1` (lab Forge host may differ) | Minecraft + idle agent |
| **VM2 (door)** | `mcmgr-door` | MOTD, wake, reserved-IP parking |

Prompt shows the hostname (`ubuntu@mcmgr-vm1` vs `ubuntu@mcmgr-door`). Run door scripts on the **door**.

---

## Reserved play IP times out; VM1 ephemeral works

Friends should use the **reserved play IP**, not either VM’s ephemeral. After Setup with VM1 already up, the reserved IP belongs on **VM1** (`PLAYABLE`). If it times out while VM1 ephemeral `:25565` works, the play address is still on the door (or mcdoor’s accept queue is full).

**On the door:**

```bash
curl -sS --max-time 5 http://127.0.0.1:8080/api/status
sudo ss -lntp | grep 25565
sudo bash -c 'set -a; source <(tr -d "\r" < /etc/mccontrol/oci.env); set +a; export HOME="${HOME:-/home/ubuntu}"; export OCI_CLI_AUTH=instance_principal; bash -- /opt/mccontrol/oci/start_vm1.sh; bash -- /opt/mccontrol/scripts/promote_playable.sh'
```

`start_vm1.sh` should print `already RUNNING` (exit 0) when VM1 is up — do not treat a 409 START as the end of the story. `promote_playable.sh` moves the reserved IP to VM1 **then** sets `PLAYABLE` (SETUP-ISSUE-6 / DOOR-ISSUE-6).

If `ss` shows `Recv-Q` equal to the listen backlog on `:25565`, restart `mccontrol` (the promote script does that). Door ephemeral `:25565` is MOTD, not the Vanilla world.

If door `diagnose_wait_forge` is **FAIL** / `No route to host` while VM1 `ss` shows `:25565` listening, host **firewalld** is probably down and Oracle **`netfilter-persistent`** is applying SSH-only REJECT (SETUP-ISSUE-7). On VM1:

```bash
sudo systemctl disable --now netfilter-persistent || true
sudo systemctl mask netfilter-persistent || true
sudo systemctl enable --now firewalld
sudo firewall-cmd --permanent --add-service=ssh
sudo firewall-cmd --permanent --add-port=25565/tcp
sudo firewall-cmd --permanent --add-port=25565/udp
sudo firewall-cmd --reload
sudo systemctl is-active firewalld
sudo firewall-cmd --list-ports
```

Then re-run **Diagnose wait_forge**. Setup guest repair applies the same mask.

Also check **UFW** and the firewalld/dbus cycle (OS-ISSUE-9 / S2-05). Product SoT is **firewalld-only**:

```bash
sudo systemctl is-enabled ufw netfilter-persistent firewalld
sudo systemctl is-active dbus.socket dbus.service firewalld ufw
sudo ufw status verbose
systemctl show firewalld -p FragmentPath -p Before -p Wants
sudo journalctl -b | grep -E 'Job dbus.service/start deleted|Job dbus.socket/start deleted' || echo 'no dbus job deleted'
```

UFW must be **masked**. `FragmentPath` should be `/etc/systemd/system/firewalld.service` (no `network-pre` in `Before=`). If a boot deleted dbus to break an ordering cycle, ACPI SoftStop will hang — wait **STOPPED** before Wake.

---

## VM1 stuck **STOPPING** after SoftStop (OS-ISSUE-9)

OCI `SOFTSTOP` should become **STOPPED** in a few minutes. If GET stays **STOPPING** for ~15+ min:

1. Do **not** Wake / `POST /api/wake` yet (S2-08 first wake raced leftover STOPPING).  
2. Do **not** fire a second `STOP` while the API returns **409** “currently being modified” — wait.  
3. On VM1 **if SSH still works**:

```bash
sudo systemctl is-active dbus.socket dbus.service firewalld ufw
sudo systemctl list-jobs
sudo systemctl status minecraft --no-pager -n 20
sudo journalctl -b | grep -E 'ordering cycle|Job dbus.*.deleted' | head
sudo journalctl -b -u dbus -u firewalld -u ufw -u minecraft --no-pager | tail -n 80
```

Cause (fixed in product cloud-init / guest repair): systemd deleted dbus to break a firewalld/cloud-init cycle. Confirm `/etc/systemd/system/firewalld.service` is the McManager override (no `network-pre`) and UFW is masked, then wait STOPPED.

4. After **STOPPED**: Park play IP if the reserved IP is not on the door. Heal ledger only when STOPPED.  
5. Full timeline: product `docs/V1-QA-Pass-1-Results.md` Failures expanded OS-ISSUE-9.

---

## Door — `wait_forge` / wake DEGRADED

Use this when Manager status is **Degraded — wait_forge.sh timed out**, or the door stays STARTING.

### Diagnose (door) — start here

```bash
sudo bash /opt/mccontrol/scripts/diagnose_wait_forge.sh
```

Prints `VM1_PRIVATE_IP` from `oci.env` vs `config.json`, recent `mccontrol` wait lines, and a **TCP probe** from the door to VM1 `:25565`.

| Result | Meaning |
|--------|---------|
| `OK: … accepts TCP` | Private game port is reachable. Sticky door state is more likely — reset / unstick below. Reconcile also runs `promote_playable` on the next tick when wake is not in progress. |
| `FAIL: cannot connect` | Minecraft is not listening, wrong private IP, or firewall/Security List is blocking **VCN → 25565**. Fix VM1 / SL before expecting wake to succeed. |

`start_vm1.sh` now waits until VM1 is **RUNNING** before `wait_forge` starts its TCP clock. Manager **Start** stays enabled when the door is **DEGRADED** even if VM1 is already on, so a retry does not require Stop first.

### Door status and logs

```bash
curl -sS http://127.0.0.1:8080/api/status
```

Manager **Stop** should return quickly (`202`). If the app shows `HttpClient.Timeout` on `POST /api/idle-empty`, the door binary is older than DOOR-ISSUE-9 (sync stop). Redeploy `mccontrol` from `door_vm/`. Check `stop_in_progress` in the JSON above while a stop is running.

```bash
sudo systemctl status mccontrol --no-pager
```

```bash
sudo journalctl -u mccontrol -n 80 --no-pager
```

Filter wait/IP lines:

```bash
sudo journalctl -u mccontrol -n 120 --no-pager | grep -E 'Waiting for|still waiting|wait_forge|DEGRADED|ip_to|Forge accepting|PLAYABLE'
```

### Reset sticky door state (clears STARTING / DEGRADED → IDLE)

```bash
sudo bash /opt/mccontrol/scripts/reset_door_state.sh
```

Stops `mccontrol`, writes `door_state=DOOR_IDLE`, starts the unit, prints `/api/status`.

### Unstick after private TCP already works

Runs diagnose → reset → `POST /api/wake` and waits for PLAYABLE:

```bash
sudo bash /opt/mccontrol/scripts/unstick_after_forge_ready.sh
```

### Force-move reserved play IP (door)

Requires instance principal + tenancy IP policy (SETUP-ISSUE-2). `--force` is required when the IP is already on the other VM’s secondary.

```bash
sudo bash /opt/mccontrol/oci/ip_to_vm1.sh --force
```

```bash
sudo bash /opt/mccontrol/oci/ip_to_vm2.sh --force
```

### Reconcile / heal (door)

```bash
sudo systemctl status mccontrol-reconcile.timer --no-pager
```

```bash
sudo journalctl -u mccontrol-reconcile.service -n 60 --no-pager
```

```bash
sudo bash /opt/mccontrol/scripts/reconcile_vm1.sh
```

Object Storage wake pull (as root; needs `oci.env`):

```bash
sudo bash /opt/mccontrol/oci/pull_os_budget.sh --force
```

Spend-brake lock (v1 Step 2.3; after door redeploy from `door_vm/`): the pull should print `SPEND_BRAKE_LOCK=0` when the object is absent. OCI CLI **3.90+** missing-object JSON is `"status": 404` / `error code 404` (`code` is null), not `ObjectNotFound` — if you still see `ERROR: spend-brake lock GET failed (not 404)` while HEAD of the lock is 404, the live script is stale (**DOOR-ISSUE-10**). If `SPEND_BRAKE_LOCK=1`, wake will **not** START VM1 and MOTD/kick say `MONTHLY SPEND BRAKE FIRED` (not the daily budget line). `GET /api/status` shows `"door":"SPEND_BRAKE"`. Do not leave a test lock object in the bucket.

Heal orphan ledger (**only useful when VM1 is STOPPED**):

```bash
sudo bash /opt/mccontrol/oci/heal_os_ledger.sh
```

### Door env (root only)

```bash
sudo grep -E '^(VM1_PRIVATE_IP|INSTANCE_ID|OBJECT_STORAGE_|BUCKET)=' /etc/mccontrol/oci.env
```

```bash
sudo python3 -c 'import json; print(json.load(open("/etc/mccontrol/config.json")).get("vm1_private_ip"))'
```

---

## VM1 — Minecraft not joining / port closed

Run these when diagnose says **TCP FAIL**, or friends time out on the reserved IP while the door claims PLAYABLE.

### Is the game process up?

```bash
sudo systemctl status minecraft --no-pager
```

```bash
sudo systemctl is-active minecraft
```

```bash
sudo journalctl -u minecraft -n 80 --no-pager
```

Product Setup uses `/opt/mcmgr/` + unit `minecraft`. The long-lived lab Forge stack may still use `/home/ubuntu/minecraft/server`. Confirm the unit name from idle-agent config:

```bash
sudo python3 -c 'import json; c=json.load(open("/etc/mc-manager/config.json")); print("unit=", c.get("minecraft_unit")); print("world_path=", c.get("world_path"))'
```

### Is anything listening on 25565?

```bash
ss -lntp | grep 25565 || true
```

Expect `*:25565` or `0.0.0.0:25565` (or `[::]:25565`). Empty output → door `wait_forge` **cannot** succeed.

### systemd `CHDIR` / Permission denied (game never starts)

SETUP-ISSUE-4. If the journal looks like this, Java never runs:

- `Changing to the requested working directory failed: Permission denied`
- `Failed at step CHDIR spawning …/java: Permission denied`
- `status=200/CHDIR`
- restart counter climbing every ~10 seconds

Stop the restart storm while investigating:

```bash
sudo systemctl stop minecraft
```

```bash
sudo systemctl cat minecraft
```

Note `User=`, `Group=`, `WorkingDirectory=`, `ReadWritePaths=`, `ProtectSystem=`, `ProtectHome=`.

```bash
sudo systemctl show minecraft -p User -p Group -p WorkingDirectory -p Result -p ExecMainStatus -p NRestarts
```

Replace `/opt/mcmgr/server` if `WorkingDirectory=` differs:

```bash
namei -l /opt/mcmgr/server
```

```bash
ls -ld /opt /opt/mcmgr /opt/mcmgr/server
```

```bash
getent passwd mcmgr; getent group mcmgr; id mcmgr
```

Every parent must be traversable by **`mcmgr`** (`x` bit). Product contract (blueprint §5, `onbox/mcmgr/common/layout.sh`):

| Path | Owner:Group | Mode |
|------|-------------|------|
| `/opt/mcmgr` | `root:mcmgr` | `0750` |
| `/opt/mcmgr/server` (and `world/`) | `mcmgr:mcmgr` | `0750` |
| `/opt/mcmgr/backups-work` | `mcmgr:mcmgr` | `0750` |
| `/opt/mcmgr/bin` | `root:mcmgr` | `0750` |
| `/etc/mcmgr` | `root:mcmgr` | `0750` |
| `/etc/mcmgr/rcon.secret` | `root:root` | `0600` |
| `/var/lib/mcmgr` | `root:root` | `0750` |

`mcmgr` primary group must be **`mcmgr`** (`id -gn mcmgr`). `ubuntu` is not in that group — use `sudo` to read the tree.

**Setup cloud-init wait (`Last: WAIT`):** `/etc/mcmgr/cloud-init-done` is inside that `0750` directory. `test -f` as `ubuntu` always prints WAIT even when cloud-init already finished (SETUP-ISSUE-5). Probe as root:

```bash
sudo test -f /etc/mcmgr/cloud-init-done && echo OK || echo WAIT
sudo cloud-init status --long
sudo ls -la /etc/mcmgr/cloud-init-done
```

If status is **done** and `sudo test` is **OK**, do not wait longer or reboot. Rebuild Manager (sudo waiter) and resume Setup from **Advanced → Deploy / repair** (`apply_stage=tofu_applied` skips `tofu apply`).

**Do not** `chmod 0777`, change `User=` to `ubuntu`, or `chmod` a single directory and call it fixed. Re-apply the whole contract:

```bash
# After uploading product onbox/mcmgr to ubuntu-writable /tmp/mcmgr-onbox (strip CRLF):
sudo bash /tmp/mcmgr-onbox/repair-permissions.sh
```

```bash
# Once installed on the box:
sudo bash /opt/mcmgr/bin/repair-permissions.sh
```

If that dies with `cp: '/opt/mcmgr/lib/env.sh' and '/opt/mcmgr/lib/env.sh' are the same file` (SETUP-ISSUE-8), the installed `layout.sh` is old — re-upload product `onbox/mcmgr/common/layout.sh` to `/opt/mcmgr/lib/layout.sh` (or run repair from `/tmp/mcmgr-onbox/` staging). Wipe world does not need this script.

**Layer 3 quarantine helper missing (SETUP-ISSUE-13):** VMs that predate Step 8.8 P10 have no `/opt/mcmgr/lib/quarantine_mod.py`. Next Change pack / Setup / `repair-permissions.sh` (current product tree) installs it. One-shot without a full repair — SFTP as `ubuntu` into `/tmp` (do not `sudo mkdir` that staging dir), then:

```bash
sudo bash -c 'set -euo pipefail
sed -i "s/\r$//" /tmp/mcmgr-p10/quarantine_mod.py /tmp/mcmgr-p10/quarantine_mod.sh
install -m 0640 -o root -g mcmgr /tmp/mcmgr-p10/quarantine_mod.py /opt/mcmgr/lib/quarantine_mod.py
install -m 0755 -o root -g mcmgr /tmp/mcmgr-p10/quarantine_mod.sh /opt/mcmgr/bin/quarantine_mod.sh
stat -c "%U:%G %a %n" /opt/mcmgr/lib/quarantine_mod.py /opt/mcmgr/bin/quarantine_mod.sh'
sudo python3 /opt/mcmgr/lib/quarantine_mod.py self-test --self-test-root /tmp/mcmgr-p10-selftest
```

Does **not** start Minecraft. TESTING already has the helper after 2026-08-23.

That script is the same `layout_apply` + `layout_verify` as bootstrap (accounts, per-path owners, `mcmgr` can `cd` + exec Java, unit `ExecStop=+` / `RestartPreventExitStatus=200`). It does **not** start Minecraft. Then:

```bash
sudo systemctl daemon-reload
sudo systemctl reset-failed minecraft
sudo systemctl start minecraft
sudo systemctl is-active minecraft
namei -l /opt/mcmgr/server
```

Expect `active` and no new `200/CHDIR` journal lines. Stop a leftover restart storm first with `sudo systemctl stop minecraft`.

### Does this VM’s private IP match what the door probes?

```bash
ip -4 addr show
```

The address the door has in `VM1_PRIVATE_IP` must exist here (usually the **primary** VNIC address, not the reserved public IP).

### Host firewall

```bash
sudo systemctl is-active firewalld
```

```bash
sudo iptables -L INPUT -n -v | head -40
```

Door poll is **private VCN → TCP 25565**, not a friend’s public `/32`. If firewalld only allows friend IPs, the door probe fails even when Minecraft is up. OCI Security List also needs **VCN CIDR → TCP 25565** in addition to friend `/32`s.

### In-game whitelist vs OCI allowlist

Product bootstrap (MVP Step 4.3) writes **`white-list=false`** / **`enforce-whitelist=false`** so only OCI Security List matters. If players still get `You are not white-listed`:

```bash
sudo grep -E '^(white-list|enforce-whitelist|online-mode)=' /opt/mcmgr/server/server.properties
```

Re-apply the product writer (not a one-off sed), then permissions:

```bash
sudo bash /tmp/mcmgr-onbox/repair-server-properties.sh
sudo bash /tmp/mcmgr-onbox/repair-permissions.sh
# or, once installed:
sudo bash /opt/mcmgr/bin/repair-server-properties.sh
sudo bash /opt/mcmgr/bin/repair-permissions.sh
```

Lab Forge path (if that is the stack you are on):

```bash
grep -E '^(white-list|enforce-whitelist)=' /home/ubuntu/minecraft/server/server.properties
```

---

## VM1 — idle agent did not SoftStop

Idle watch is a **timer → oneshot**. `mc-idle-watch.service` **inactive (dead)** between ticks is **normal**.

After `idle_timeout_minutes`, SoftStop VM1 if Minecraft is empty **or not running** (same timeout; first oneshot tick only starts the clock). When the unit is already down, skip RCON and `systemctl stop`; still cold-backup if the world exists. Changing `vm_agent/` without redeploying `/opt/mc-manager` is not done.

### Timer and last oneshot

```bash
systemctl status mc-idle-watch.timer --no-pager
```

```bash
systemctl status mc-idle-watch.service --no-pager
```

```bash
sudo journalctl -u mc-idle-watch.service -n 40 --no-pager
```

| Log line | Meaning |
|----------|---------|
| `Minecraft not active; idle timer started.` | Unit is not `active`; idle clock started (first tick — no SoftStop yet). |
| `Minecraft not active; idle for X / Y minutes` | Same clock continuing; SoftStop when X ≥ Y. |
| `Minecraft already inactive; skipping RCON and systemctl stop.` | Stop path with the game already down. |
| `No players; idle timer started.` | Unit is `active` and RCON `list` is empty. |
| `Idle agent disabled in config.` | Local config has `idle_agent_enabled=false` (testing only; boot/Minecraft start should force-enable — OS-ISSUE-7). |
| SoftStop / `Stopped instance after` | Idle/budget path ran. |

### Config + enablement

```bash
sudo python3 -c 'import json; c=json.load(open("/etc/mc-manager/config.json")); print("enabled=", c.get("idle_agent_enabled")); print("timeout_min=", c.get("idle_timeout_minutes")); print("unit=", c.get("minecraft_unit"))'
```

```bash
sudo systemctl is-enabled mc-idle-watch.timer
```

```bash
sudo systemctl status mc-boot-ledger.service --no-pager
```

Force-enable the timer (does **not** by itself start Minecraft):

```bash
sudo systemctl enable --now mc-idle-watch.timer
```

---

## Reserved play IP “on the wrong VM”

Friends always use the **reserved** public IP. It must sit on the **secondary** private IP of whichever VM should answer `:25565` (door when idle, VM1 when playable). The ephemeral/primary public IP is SSH/admin only.

From **your PC** (OCI CLI with user API key), or Console: Networking → Public IPs → reserved play IP → assigned private IP.

On each VM, confirm the secondary address exists (Setup writes `/etc/netplan/99-mcmgr-play.yaml`):

```bash
ip -4 addr show
```

```bash
ls -l /etc/netplan/
```

```bash
cat /etc/netplan/99-mcmgr-play.yaml
```

If the secondary is missing, reserved-IP moves succeed in OCI but the guest never answers on that address (SETUP-ISSUE-1).

After a **`$1` budget Function** stop, **both** VMs may be STOPPED and the IP may be left anywhere (FN-ISSUE-1). Start the **door** first, then `ip_to_vm2.sh --force` (or the Manager repair button once Step 4.4 ships).

---

## Object Storage / usage looks wrong

On **VM1**:

```bash
sudo python3 -c 'import json; print(json.dumps(json.load(open("/var/lib/mc-manager/usage.json")), indent=2)[:2000])'
```

```bash
sudo python3 -c 'import json; print(json.dumps(json.load(open("/var/lib/mc-manager/lease.json")), indent=2))'
```

On **door**:

```bash
sudo ls -l /var/lib/mccontrol/os-cache/
```

Prefer Manager **Usage** tab / Troubleshooting when the desktop app is available.

---

## Setup `tofu apply` failed after VMs already exist (SETUP-ISSUE-9)

Do **not** Danger Zone Delete / `tofu destroy` a partial stack that already has VM1. That burns another A1 create. OpenTofu state under `%LOCALAPPDATA%\McManager\tofu\<stack>\` still has the created resources; a second Deploy continues the apply.

Manager **locks Deploy** after the first click even when apply fails. Close Manager fully, reopen the **same** `MCMANAGER_CONFIG_DIR`, Setup resume → last step → **Deploy**. Alternative: Advanced → **Deploy / repair**. Do not start a new empty config directory (that would try a second Always Free A1).

Pass 2 hit this when the `$1` budget description exceeded OCI’s **200**-character cap and OCIR returned **404-DENIED** on a brand-new compartment. Product HCL is fixed; retry uses repo `infra/` (no Manager rebuild).

If Deploy then times out on `/etc/mcmgr/cloud-init-done` with `Last: WAIT` while `sudo cloud-init status` is **done** in seconds and `/etc/mcmgr` does not exist, that is **SETUP-ISSUE-10** (invalid VM1 `#cloud-config`, runcmd never ran). Restart Hybrid so Setup guest repair can install firewalld and write the marker. Do not destroy the A1.

---

## What to paste to an AI agent

1. Which host (`mcmgr-vm1` vs `mcmgr-door`) and what you were trying to do.  
2. Full command + full output (not a one-line paraphrase).  
3. For wake failures: `diagnose_wait_forge.sh` **and** VM1 `systemctl status minecraft` + `ss -lntp | grep 25565`. If the journal shows `200/CHDIR`, also `systemctl cat minecraft` and `namei -l` on `WorkingDirectory`.  
4. For idle failures: `systemctl status mc-idle-watch.timer` **and** `mc-idle-watch.service` **and** `systemctl is-active minecraft`.  

Do not paste RCON passwords, Auth Tokens, PEM keys, or `/etc/mccontrol/oci.env` wholesale. Redact OCIDs if the chat is shared; agents working in this workspace may read gitignored private docs locally.

---

## Changelog

| 2026-08-23 | SETUP-ISSUE-13: Layer 3 `quarantine_mod` one-shot copy for VMs that predate P10. |
| 2026-08-20 | SETUP-ISSUE-10: WAIT with cloud-init already done and no `/etc/mcmgr` — invalid YAML, restart Hybrid, do not destroy. |
| 2026-08-20 | SETUP-ISSUE-9: retry Deploy on a partial stack (do not destroy); budget description 200-char cap + OCIR 404 on new compartment. |
| 2026-08-19 | OS-ISSUE-9: firewalld/cloud-init/dbus cycle (mask UFW + full firewalld unit override); do not wake while STOPPING. |
| 2026-08-17 | SETUP-ISSUE-8: installed `repair-permissions.sh` same-file `cp` of `lib/env.sh`. |
| 2026-08-15 | Manager Troubleshooting tab (MVP Step 4.4): button → SSH/OCI map; preferred park-IP (VM1 if RUNNING else door). |
| 2026-08-15 | §5 permission contract + `repair-permissions.sh` (SETUP-ISSUE-4 / MVP Step 4.2). |
| 2026-08-15 | Idle-watch Step 4.1 live: SoftStop when Minecraft is not running; new journal lines (`idle timer started` / `already inactive`). |
| 2026-08-15 | Added `minecraft.service` CHDIR / `mcmgr` WorkingDirectory commands (SETUP-ISSUE-4). |
| 2026-08-15 | Initial operator runbook (wait_forge, idle watch, IP parking, sudo reminder). |
