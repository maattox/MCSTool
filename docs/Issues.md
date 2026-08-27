# Known issues (operator stack)

Living list of **known bugs / quirks** in the live Always Free Minecraft + door + Manager stack.
Product roadmap stays in `PRODUCT-IDEAS.md`. Architecture stays in `Infrastructure-Information.md`.
**What is built / live** (for agents): [`VM-Software.md`](VM-Software.md) status table.
**Operator copy-paste commands:** [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md).

**Status values:** Open · Workaround · Parked · Fixed (leave briefly for history)

---

## Object Storage / usage ledger

### OS-ISSUE-1 — Door orphan heal historically skipped by reconcile
**Status:** Fixed (2026-08-09; keep for regression)  
**Summary:** When VM1 was STOPPED with an open `ledger/usage.json` interval, auto-heal via `mccontrol-reconcile.timer` did not close it, while **manual** Testing2 heal (SSH as `ubuntu`) worked.  
**Causes (stacked):**  
1. Early: reconcile gated heal on lifecycle probe + `set -e` (fixed earlier — always invoke heal).  
2. **2026-08-09 root cause:** `pull_os_budget.sh` / `heal_os_ledger.sh` used `${HOME}` under `set -u`. Systemd oneshots omit `HOME`, so both scripts aborted immediately with `HOME: unbound variable` every minute.  
**Fix:** `export HOME="${HOME:-/home/ubuntu}"` before PATH; redeployed to live door.  
**Verify:** Testing2 → **Door reconcile journal** → expect `HEAL_OS_OK` / `HEAL_SKIP …` without `HOME: unbound`.  
**Refs:** `door_vm/oci/heal_os_ledger.sh`, `door_vm/oci/pull_os_budget.sh`

### OS-ISSUE-2 — VM1 repair once extended door heal past real stop
**Status:** Fixed (keep for regression)  
**Summary:** After door `stop_uncertain` close, VM1 boot “repair” moved `stopped_at` **later** toward the new boot time (journal noise), creating tiny extra intervals.  
**Cause:** journal search included current-boot lines and allowed moving stop after the door estimate.  
**Fix:** door estimate is an **upper bound**; repair may only move earlier using `Stopped`/`Stopping` journal lines ≤ door estimate, else accept door time.  
**Observed good run (2026-08-08):** door closed open boot interval at `23:03:43Z` uncertain; VM1 repaired to `22:56:44Z` via journal (earlier), then one new open boot interval — expected.  
**Refs:** `vm_agent/ledger.py` `repair_uncertain_stops`

### OS-ISSUE-3 — Force Start can still dual-write start intervals
**Status:** Open / mitigated  
**Summary:** Manager Force Start fallback (`source=force_start`) can race `mc-boot-ledger` (`source=boot`), producing short `normalize_open` rows.  
**Mitigation:** Manager waits longer and prefers OS/VM pull before fallback; prefer door **Wake** for play path.  
**Refs:** `app/server_manager.py` `force_start`

### OS-ISSUE-4 — Door Object Storage I/O is wake + one-shot heal (not every tick)
**Status:** By design (updated 2026-08-09)  
**Summary:** Budget/ledger **pull** is wake-path (`do_wake` / Testing2 / `/api/os-refresh`), not every reconcile tick. Reconcile may **heal** the OS ledger **once per VM1-down episode** (`ledger_heal_verified` latch), then skip further OS I/O until VM1 is RUNNING / IP moved to VM1.  
**Refs:** `door_vm/scripts/reconcile_vm1.sh`, `docs/Object-Storage-Phase3.md`, `docs/Object-Storage-Phase4.md`

### OS-ISSUE-5 — SoftStop hang + failed OS publish leaves open ledger
**Status:** Mitigated (2026-08-09)  
**Summary:** Idle SoftStop entered `STOPPING` ~07:50–08:04 UTC (guest ACPI/orderly poweroff hang; dbus-down hypothesis). OS ledger stayed open because auto-heal was dead (OS-ISSUE-1) and/or idle publish failed before SoftStop. Morning boot then force-pulled door heal and merged/published local rows (e.g. `boot_preclose` near shutdown) instead of only refining the door-closed id.  
**Mitigations:** HOME fix for door heal; idle path closes+saves locally, `timeout` on `systemctl stop`, retries OS publish before SoftStop; boot always force-pulls OS and `merge_ledgers_for_boot` with local. **Phase 5:** lease heartbeats bound crash error; door heal waits for **STOPPED** (not STOPPING) and closes at heartbeat.  
**Guest SoftStop hang itself** is **OS-ISSUE-9** (fixed 2026-08-19: firewalld/cloud-init/dbus cycle drop-in + mask UFW). Manager **Troubleshooting** tab still documents Console reset if stuck — after Console reset, use **Park play IP**, then **Heal open ledger** only when VM1 is **STOPPED**.  
**Refs:** `vm_agent/idle_watch.py`, `vm_agent/record_boot.py`, `vm_agent/ledger.py` `merge_ledgers_for_boot`, `docs/Object-Storage-Phase5.md`, OS-ISSUE-9

### OS-ISSUE-9 — Guest ACPI SoftStop stuck in OCI STOPPING (TESTING QA)
**Status:** Fixed (2026-08-19) — product cloud-init + Setup guest repair  
**Summary:** On TESTING (2026-08-19), after an S1 Compute START whose boot never started **dbus** or **firewalld**, `SOFTSTOP` stayed **STOPPING ~17 min** (OCI waiter 600s timed out, exit 2). Hard `STOP` while STOPPING returned **409** “currently being modified”. Waking when API first showed STOPPED still overlapped leftover STOPPING (door briefly PLAYABLE; play IP raced back to the door; second STOPPING ~16 min). Later SoftStops on boots where dbus/firewalld **were** up finished in ~1–3 min.  
**Root cause:** Distro `firewalld.service` has `Before=network-pre.target` and `After=dbus.service`. That races **cloud-init** (`Before=network-pre` and `Before=sysinit`) — Debian #1025618 / firewalld#414. systemd **non-deterministically** deletes a job to break the cycle. On the S1 START boot (2026-08-19 19:10 UTC) it deleted **`dbus.service/start` and `dbus.socket/start`**. Journal that boot: UFW oneshot only; no firewalld; `pam_systemd` “Failed to connect to system bus”; **no** `Power key` / logind shutdown. Hypervisor stayed STOPPING until its timeout. Other boots deleted `cloud-init.service` instead, so dbus+firewalld survived.  
**UFW:** Installed and **systemd-enabled** (`active` oneshot) but `/etc/ufw/ufw.conf` `ENABLED=no`. Not the job-deletion cause. Still must not run beside firewalld (both own nftables; Oracle Ubuntu guidance). Product SoT is **firewalld-only** — disable + mask UFW.  
**Not SETUP-ISSUE-7:** `netfilter-persistent` was already **masked**.  
**Relation:** OS-ISSUE-5 already noted a guest ACPI/orderly poweroff hang (dbus-down hypothesis) and shipped **ledger** mitigations; the hang itself is this issue. OS-ISSUE-6 (heal only when STOPPED) is working (S2-10 Pass).  
**Fix (product path):** Full unit override `/etc/systemd/system/firewalld.service` from `infra/cloud-init/firewalld-mcmgr.service` (omits `Before=`/`Wants=` `network-pre.target`; drop-ins cannot reset those). Cloud-init + `EnsureVm1HostFirewall` also **disable + mask UFW** (do not `systemctl stop ufw` while firewalld is up). Security List remains the IP allowlist.  
**Verify (2026-08-19, TESTING):** After override: `FragmentPath=/etc/systemd/system/firewalld.service`, `Before=` has no `network-pre`. Three SoftStops (including two after Compute START) reached **STOPPED in 43s**. Post-START boots: dbus+firewalld active, 25565 tcp/udp, ufw masked, no `Job dbus.*.deleted`.  
**Workaround (still):** Wait until lifecycle is **STOPPED** (not STOPPING) before Wake/`POST /api/wake`. Do not issue a second STOP during 409.  
**Refs:** `archive/V1-QA-Pass-1-Results.md` (Failures expanded timeline), `archive/V1-Bug-Fix-Plan-Pass-1.md` P1, `infra/cloud-init/vm1.yaml.tftpl`, `infra/cloud-init/firewalld-mcmgr.service`, `SetupBootstrapService.EnsureVm1HostFirewall`, Operator-Troubleshooting VM1 STOPPING block

### OS-ISSUE-10 — Server name / icon / MOTD never applied on VM1 (S4-12)
**Status:** Fixed (2026-08-20) — product `vm_agent` + `minecraft.service` ordering  
**Summary:** Pass 1 **S4-12**: Manager Save wrote `messages/chat.json` + `messages/server-icon.png` and set `messages.vm1`, but the Java multiplayer list never showed the new name, MOTD, or icon after Start or Restart.  
**Causes (stacked):**  
1. TESTING `/opt/mc-manager` was still pre–Step 7.6 for identity: `record_boot.py` did not call `pull_messages_if_dirty`, so motd/icon were never written (`motd=A Minecraft Server`, no `server-icon.png`).  
2. Product SoT applied identity **After=minecraft**. Vanilla loads `server.properties` at start and **writes that in-memory copy back on stop**, so a post-start patch was always overwritten before the next start. Client pings use in-memory MOTD, so even a successful after-start write did not change the list until a start that already had the new file.  
**Fix:** Redeploy idle agent from product `vm_agent/` (includes `_apply_identity`). `mc-boot-ledger.service` is **Before=minecraft.service** (no `Requires=minecraft`). Greenfield `minecraft.service.in` **After=**/**Wants=** `mc-boot-ledger.service`; `install.sh` writes drop-in `minecraft.service.d/mcmgr-identity.conf` for existing VMs.  
**Verify:** Save identity → Restart Minecraft → `motd=` and `server-icon.png` under `/opt/mcmgr/server/` match Object Storage; Java list ping on the play IP while VM1 holds it. Door MOTD unchanged.  
**Refs:** `vm_agent/os_publish.py` `pull_messages_if_dirty`, `vm_agent/systemd/mc-boot-ledger.service`, `onbox/mcmgr/templates/minecraft.service.in`, `archive/V1-Bug-Fix-Plan-Pass-1.md` P5

### OS-ISSUE-11 — VM1 color list icon: ImageIO `/tmp` + ubuntu `.tmp` (P8)
**Status:** Fixed (2026-08-24) — product `vm_agent` + `minecraft.service` PrivateTmp  
**Summary:** Object Storage and `/opt/mcmgr/server/server-icon.png` already held a 64×64 color PNG (`mcmgr:mcmgr` `0644`), but the Java multiplayer list still showed the door greyscale. Journal: **Couldn't load server icon**. This is **not** a regression of OS-ISSUE-10 (boot ordering).  
**Cause (stacked):**  
1. **User-visible:** `minecraft.service` `ProtectSystem=strict` leaves host `/tmp` read-only. Vanilla/Fabric `ImageIO.write` encodes the favicon via `/tmp/imageio*.tmp` → `IIOException: Can't create cache file!` / `FileSystemException: Read-only file system`.  
2. Product `_apply_identity` also wrote `server-icon.png.tmp` **in** the `0750 mcmgr:mcmgr` server dir. `ubuntu` is not in `mcmgr` → **EACCES** (SSH/SFTP or non-root apply). Root boot-ledger could still land the file.  
**Fix:** `PrivateTmp=true` on the game unit (template + idle-agent drop-in `mcmgr-private-tmp.conf`). Stage the PNG under `/var/lib/mc-manager` or `/tmp`, then `install -o mcmgr -g mcmgr -m 644`. Do not chmod the live server tree. Redeploy idle agent from `vm_agent/`.  
**Verify:** After Minecraft restart, no “Couldn't load server icon”; status ping includes a color favicon; `ls -l` `server-icon.png` is `mcmgr:mcmgr` `0644`.  
**Refs:** `onbox/mcmgr/templates/minecraft.service.in`, `vm_agent/install.sh`, `vm_agent/os_publish.py` `_install_server_icon`, Step **8.10** P8

### OS-ISSUE-6 — Heal during STOPPING could race SoftStop publish
**Status:** Fixed (Phase 5)  
**Summary:** Door heal previously accepted `STOPPED|STOPPING`, so a slow SoftStop publish could race concurrent ledger puts.  
**Fix:** reconcile + `heal_os_ledger.sh` heal only when lifecycle is **STOPPED**; STOPPING skips heal.  
**Refs:** `door_vm/scripts/reconcile_vm1.sh`, `door_vm/oci/heal_os_ledger.sh`, `docs/Object-Storage-Phase5.md`

### OS-ISSUE-7 — Ledger shape must follow live VM resize
**Status:** Fixed (Phase 5 shape detect)  
**Summary:** Interval `ocpus`/`memory_gb` used to be stamped only from pushed agent config. A Console resize to a smaller shape without updating config would overstate usage.  
**Fix:** VM1 detects live OCPU/memory from `/proc` on boot (and idle-watch reshape check), stamps new intervals from that, and syncs shape into local config + OS `budget/config.json` when changed.  
**Refs:** `vm_agent/shape_detect.py`, `vm_agent/record_boot.py`, `docs/Object-Storage-Phase5.md`

---

## Door / Minecraft client UX

### DOOR-ISSUE-1 — First join kick missing custom “server starting” text
**Status:** Parked  
**Summary:** With VM1 off, the **first** Minecraft client connect sometimes gets a generic disconnect instead of the custom “server is starting / try again” kick. A second attempt usually shows the correct message.  
**Likely cause:** race between async wake start and MOTD/kick text selection in `mcdoor`.  
**Refs:** `docs/Object-Storage-Phase3.md` known issues, `door_vm/src/mcdoor.c`

### DOOR-ISSUE-2 — BUDGET_EXHAUSTED stuck after raising limits (historical)
**Status:** Fixed  
**Summary:** After refuse-on-budget, raising monthly/soft in Manager did not clear door state until force paths.  
**Cause:** login only woke from `DOOR_IDLE`; `control_wake` rejected from stale cache before OS pull.  
**Fix:** wake from exhausted; OS-mode wake always `do_wake` (pull first); `/api/os-refresh`.  
**Refs:** `door_vm/src/mcdoor.c`, `door_vm/src/control.c`

### DOOR-ISSUE-3 — Budget refuse ignored monthly/soft UI fields (historical)
**Status:** Fixed  
**Summary:** Manager published hardcoded `daily_ocpu_limit_phase_a: 45` while UI edited monthly/soft; MOTD stayed ~45.  
**Fix:** publish derives daily from monthly; door prefers monthly + soft MTD gate.  
**Refs:** `app/object_storage.py` `build_budget_config`

---

## Desktop Manager

### APP-ISSUE-1 — Wrong `python` on PATH lacks `oci`
**Status:** Workaround  
**Summary:** Shell `python` may be Altair/other without deps.  
**Workaround:** `run.bat` or Python 3.13 explicit path.  
**Refs:** `docs/Object-Storage-Testing.md`

### APP-ISSUE-2 — Phase 4 deploy Permission denied on `/tmp/door-p4`
**Status:** Fixed  
**Summary:** Staging dir created with `sudo` (root-owned); SFTP as `ubuntu` failed.  
**Fix:** create `/tmp` staging as ubuntu + chown fallback.  
**Refs:** `app/door_deploy.py`

### OS-ISSUE-6 — Door/Console SoftStop skips world backup
**Status:** Open — **MVP deferred** (2026-08-11 / Step 2.4 operator OK)  
**Summary:** World zip → Object Storage runs on idle-agent SoftStop and Manager `graceful_stop.sh` (**cold** after Minecraft stop). **Live** backups (`save-off` → `save-all flush` → zip → `save-on`) are implemented for CLI / future schedules (`world_backup.py live`). Door or Console SoftStop alone still skips backup. Manager top-bar **Stop** uses door `idle-empty` → same skip.  
**MVP rationale:** Success criterion is the ~9.5 GiB soft-cap *policy* plus a working idle SoftStop backup path — not backup on every stop path. Idle SoftStop still backs up.  
**Workaround:** Prefer idle empty SoftStop or Manager Force Stop (graceful path); or run `world_backup.py live` while up.  
**Future (post-MVP):** optional minecraft `ExecStop` hook or door pre-stop SSH (keep Always Free / Micro load in mind).  
**Refs:** `vm_agent/world_backup.py`, `vm_agent/idle_watch.py`

### OS-ISSUE-7 — Temporary idle disable undone by Minecraft boot
**Status:** Open (by design / product safety — document for testers)  
**Summary:** Manager / config can set `idle_agent_enabled=false` and stop `mc-idle-watch.timer`, but the next Minecraft start runs `record_boot.py`, which **force-enables** the timer and rewrites local + OS budget config to `idle_agent_enabled=true`.  
**Cause:** PRODUCT-IDEAS MVP rule — disabling idle is testing-only and must not survive reboot/start.  
**Workaround for tests:** keep Minecraft from restarting, or re-disable after each start; accept that SoftStop may return after boot. Pack-corpus harness stops `minecraft` and disables idle **before** `ReplacePackAsync`, **re-disables every ~15s during replace** (boot oneshot turns idle back on), then again in the ready-gate.  
**Refs:** `vm_agent/record_boot.py` `force_enable_idle_agent`, `src/McManager.PackTestHarness/IdleHold.cs`

### OS-ISSUE-8 — Door wake may trust stale OS budget/ledger cache
**Status:** Fixed (Step 2.4)  
**Summary:** `pull_os_budget.sh` skipped GETs when caches existed and dirty bits were clear; wake did not pass `--force`, so a lost flags PUT could leave a stale wake gate.  
**Fix:** `do_wake` and `control_os_refresh` invoke `pull_os_budget.sh --force` so every wake/refresh re-validates Object Storage before the gate (fail-closed unchanged).  
**Refs:** `door_vm/src/control.c`, `door_vm/oci/pull_os_budget.sh`

### FN-ISSUE-1 — $1 budget Function SoftStops the door, so reconcile cannot hand back the play IP
**Status:** **Gone on TESTING** (2026-08-19) and **gone on the live Forge lab** (2026-08-27). Product **v1** image **0.0.12** SoftStops **VM1 only** and PUTs the spend-brake lock.  
**Summary:** The old Forge lab `shutdown_vm` **0.0.11** SoftStopped **VM1 and VM2**. Door `mccontrol-reconcile.timer` only runs while VM2 is up, so it **could not** move the reserved play IP after that path.  
**Cause:** Live Forge image was updated from VM1-only to both instances; no IP-move or Object Storage lock flag.  
**TESTING (2026-08-19):** `mcmgr-fn-softstop` runs `mcmgr-fn/softstop:setup` **0.0.12**. Synthetic ACTUAL SoftStops VM1 only, PUTs `meta/spend-brake-triggered.json` (`source=budget_function`), door stays **RUNNING**, play IP stayed on the door secondary. RESET is `SKIPPED` and does not DELETE the lock.  
**Forge lab (2026-08-27):** `budget-repo/shutdown_vm:0.0.12`, Function config `INSTANCE_OCIDS` = VM1 only + Object Storage lock keys. Invoke SoftStopped **VM1 only**; door stayed **RUNNING**; reconcile parked the play IP on the door; Manager/OCI DELETE of the lock restored `DOOR_IDLE`.  
**Product:** v1 Function leaves the door running (Always Free AMD Micro ≠ Ampere hours) and PUTs `meta/spend-brake-triggered.json`. Door honor of that flag is Step **2.3** (TESTING P2). Official installer image copy is V1 Step **8.6.1** (do not treat TESTING `fn`/Docker as that path).  
**Refs:** `functions/shutdown_vm/`, `Infrastructure-Information.md` Budget emergency stop, `archive/V1-Bug-Fix-Plan-Pass-1.md` P3

---

## Setup (product Manager)

### SETUP-ISSUE-1 — First deploy skipped `meta/infra.json`; door wake degraded; reserved IP timed out
**Status:** Fixed (2026-08-14)  
**Summary:** OpenTofu + SSH bootstrap succeeded, but Setup stopped at `apply_stage=vm1`. Bucket had `budget/config.json` + `meta/flags.json` only. Door MOTD on the **ephemeral** IP showed idle then **Control plane degraded**; reserved play IP timed out; Vanilla rejected the admin (`not white-listed`).  
**Causes:**  
1. `PublishFromLocalAsync` GETs `meta/infra.json` first; a greenfield 404 aborted the PUT. Seed errors were not copied into the deploy log.  
2. Door `/etc/mccontrol/oci.env` omitted `OBJECT_STORAGE_NAMESPACE` / `BUCKET`; `object_storage_enabled` was already true → `pull_os_budget.sh` failed closed. `--force` also 404'd missing `ledger/usage.json`.  
3. Guest OS never had the secondary play IP (netplan). Reserved public IP maps to that secondary, not the primary/ephemeral.  
4. Vanilla `white-list=true` with empty `whitelist.json`.  
**Fix:** Treat missing infra meta as create; seed empty ledger; log seed failures; persist OS vars in door `oci.env`; optional ledger 404 in `pull_os_budget.sh`; Setup writes `/etc/netplan/99-mcmgr-play.yaml` on both VMs; wizard collects admin Minecraft username; Re-Deploy at `vm1` re-runs guest repair (and starts VM1 if idle-stopped). **Do not** `apt upgrade` / `do-release-upgrade` (22.04 baseline).  
**Follow-up (2026-08-15):** product intent is **Minecraft `white-list` off** so only OCI Security List matters. Automated bootstrap now writes `white-list=false` / `enforce-whitelist=false` (MVP **Step 4.3** / SETUP-ISSUE-3 **fixed**). Admin Minecraft username remains optional in the wizard (later MOTD/ops), not a join gate.  
**Verify:** Re-Deploy from Setup (fill Minecraft username). Expect log lines `Published budget/config.json, ledger/usage.json, and meta/infra.json` and `Setup finished`. Bucket has `meta/infra.json`. Reserved play IP shows door MOTD; second connect starts VM1.  
**Refs:** `InfraMetaStore.PublishFromLocalAsync`, `SetupBootstrapService.EnsureGuestRuntimeAsync`, `door_vm/install.sh`, `door_vm/oci/pull_os_budget.sh`

### SETUP-ISSUE-2 — Door instance principal cannot move reserved IP (compartment IAM + tag DG)
**Status:** Fixed (2026-08-14)  
**Summary:** After `wait_forge` succeeded, wake went **DEGRADED** with `ip_to_vm1.sh failed`. `UpdatePublicIp` / `GetPublicIp` / `GetPrivateIp` returned `NotAuthorizedOrNotFound` from the door instance principal. `start_vm1` still worked (`mcmgr-dg-instances` + `use instance-family`).  
**Causes:**  
1. Compartment-only `manage public-ips` / `use private-ips` is not enough for `UpdatePublicIp` — those verbs must be **in tenancy** (`mcmgr-door-ip` at the root).  
2. Door DG matching `instance.freeform-tag.mcmgr-role = 'door'` did not enroll the door (hyphenated tag / identity domain). Classic `iam dynamic-group list` may show `matching-rule: null` even when tofu set a rule.  
**Fix:** Product HCL: `oci_identity_policy.door_ip` at tenancy + `mcmgr-dg-door` match `instance.id = <door OCID>`. If the policy was created out of band, import it into Setup LocalAppData state from repo `infra/` with quoted `-state="$state"` (see product `infra/README.md`).  
**Verify:** From the door as root, `ip_to_vm1.sh` succeeds; wake → **PLAYABLE**; reserved play IP answers Minecraft.  
**Refs:** `infra/modules/iam/main.tf`, `docs/Automated-Infrastructure-Deployment.md` §11.4, [`Infrastructure-Information.md`](Infrastructure-Information.md) door IAM

### SETUP-ISSUE-3 — Automated bootstrap still enables Vanilla in-game whitelist
**Status:** Fixed (2026-08-15) — product bootstrap + Re-Deploy guest repair  
**Summary:** SETUP-ISSUE-1 joined with `white-list=true` + empty `whitelist.json`. The 3.3 fix seeded `whitelist.json` from the wizard admin username. Operator wanted **in-game whitelist off** so only OCI Security List `/32`s matter (already applied **manually** on the test VM — not enough).  
**Fix (product path):** `onbox/mcmgr/common/server_properties.sh` managed defaults `white-list=false` / `enforce-whitelist=false` (never `online-mode=false`). Driver always re-applies managed properties on resume (does not skip with completed `rcon_ready`). `repair-server-properties.sh` + Setup `EnsureGuestRuntime` Re-Deploy the same writer. Admin Minecraft username is optional (not required to join). Blueprint §7.3 already listed false; code now matches.  
**Verify:** dry-run asserts the three keys; test VM1 `sudo grep` after the repair script (not a one-off sed). Next greenfield/Re-Deploy writes false.  
**Refs:** `onbox/mcmgr/common/server_properties.sh`, `repair-server-properties.sh`, `Minecraft-Server-Deployment-Blueprint.md` §7.3

### SETUP-ISSUE-4 — `minecraft.service` crash-loops: systemd `CHDIR` Permission denied
**Status:** Fixed (2026-08-15) — product bootstrap + test VM1  
**Summary:** Operator started VM1 from the Manager; Minecraft never came up. `journalctl -u minecraft` repeated every ~10s with `status=200/CHDIR` (`User=mcmgr` could not `chdir` `WorkingDirectory=/opt/mcmgr/server`). Live tree was `ubuntu:ubuntu` `0750` on `/opt/mcmgr` and `server/` (mcmgr gid 1000 / group `mcmgr`; ubuntu gid 1001).  
**Cause:** layout applied once (or skipped) then later `mkdir` as `ubuntu`; cloud-init used `chown -R mcmgr:mcmgr` + `0755` + homedir `/opt/mcmgr`; no fail-closed verify. StartLimit did not stop the storm (CHDIR spawn loop).  
**Fix (product path):** `onbox/mcmgr/common/layout.sh` §5 contract — `layout_ensure_accounts` (usermod wrong home/gid), idempotent per-path `layout_apply` (never `chown -R /opt/mcmgr`), fail-closed `layout_verify` (`mcmgr` can `cd` + exec Temurin). Driver always apply+verify. `repair-permissions.sh` for existing VMs. Unit `ExecStop=+` (root reads `rcon.secret`) + `RestartPreventExitStatus=200`. Cloud-init per-path owners. Setup whitelist seed and Manager world-replace re-apply the contract.  
**Verify (2026-08-15, test VM1):** `sudo bash /tmp/mcmgr-onbox/repair-permissions.sh` → `root:mcmgr` `0750` / `mcmgr:mcmgr` `0750`; `systemctl start minecraft` **active** `NRestarts=0`; journal `Starting net.minecraft.server.Main` (no new CHDIR); `ss` `0.0.0.0:25565`; door `diagnose_wait_forge.sh` TCP **OK**. Idle timer left disabled for continued work.  
**Refs:** `onbox/mcmgr/common/layout.sh`, `onbox/mcmgr/repair-permissions.sh`, `templates/minecraft.service.in`, blueprint §5

### SETUP-ISSUE-5 — Setup cloud-init wait times out with `Last: WAIT` (marker hidden from `ubuntu`)
**Status:** Fixed (2026-08-17) — product Setup waiter  
**Summary:** Greenfield Setup `tofu apply` succeeded; both VMs RUNNING. Wizard timed out after ~20 min: `Timed out waiting for /etc/mcmgr/cloud-init-done on <vm1-ssh>. Last: WAIT`. SSH as `ubuntu` worked. Operator dump: `cloud-init status` **done** (~50s after boot), packages/Adoptium/firewalld complete, runcmd succeeded. `namei -l` showed the marker path exists; `ls /etc/mcmgr` as `ubuntu` was **Permission denied**.  
**Cause:** Cloud-init writes `/etc/mcmgr/cloud-init-done` **after** `chmod 0750` `root:mcmgr` on `/etc/mcmgr` (blueprint §5 / SETUP-ISSUE-4). Setup probed `test -f` as `ubuntu`, who is not in group `mcmgr`. `test -f` cannot traverse `0750` → false → `WAIT` forever. Not a hung apt/cloud-init; waiting longer does not help. Door marker `/etc/mcmgr-door/` is default `0755`, so the door wait would not have hit this.  
**Fix (product path):** `SetupBootstrapService.WaitCloudInitAsync` probes `sudo -n test -f <marker>` (same pitfall as `/etc/mccontrol/oci.env`). Keep `/etc/mcmgr` `0750` — do not `chmod 0755`/`0777`. Template comment in `infra/cloud-init/vm1.yaml.tftpl`. Instance `metadata` is `ignore_changes`; this waiter change unblocks an already-booted VM without recreate.  
**Verify:** After rebuilding Manager, **Advanced → Deploy / repair** (resume at `apply_stage=tofu_applied`). Expect `cloud-init ready: /etc/mcmgr/cloud-init-done` within seconds, then door/VM1 bootstrap. On-box check: `sudo test -f /etc/mcmgr/cloud-init-done && echo OK`.  
**Refs:** `src/McManager.Core/Setup/SetupBootstrapService.cs`, `infra/cloud-init/vm1.yaml.tftpl`, `Agent-Deploy-Pitfalls.md`

### SETUP-ISSUE-6 — Setup leaves reserved play IP on the door while VM1 is already playable
**Status:** Fixed (2026-08-17) — product Setup guest repair + door scripts  
**Summary:** After greenfield Deploy, VM1 was RUNNING with Minecraft up. Minecraft to the **reserved play IP** timed out (`Connection timed out: getsockopt`). VM1 **ephemeral** worked. Door **ephemeral** `:25565` also timed out. A later connect to the reserved IP then worked.  
**Causes (stacked):**  
1. OpenTofu parks the reserved public IP on the **door** secondary. Setup `EnsureDoorRuntime` then forced `door_state=DOOR_IDLE` and **did not** call `ip_to_vm1.sh`, so the play address still hit mcdoor while the game was already on VM1.  
2. First client login woke the door → `start_vm1.sh` `INSTANCE_ACTION START` on an already-RUNNING VM1 → OCI **409 Conflict** → wake **DEGRADED**, **never** `ip_to_vm1` (DOOR-ISSUE-6).  
3. mcdoor’s single accept loop blocked in `recv` with **no socket timeout**; listen backlog 16 filled (`Recv-Q` = backlog) → further TCP to reserved IP **and** door ephemeral timed out. VM1 Java has a large backlog, so the ephemeral game IP still worked.  
**Fix (product path):** After VM1 netplan/Minecraft repair, Setup runs `door_vm/scripts/promote_playable.sh` (`ip_to_vm1` **then** persist `PLAYABLE`, restart mccontrol). Do not force `DOOR_IDLE` at the end of a successful deploy. `start_vm1.sh` no-ops when VM1 is already RUNNING/STARTING. mcdoor sets 8s recv/send timeouts and listen backlog 128.  
**Verify:** VM1 RUNNING, game up, **no** prior VM1-ephemeral connect: Minecraft to the reserved play IP joins Vanilla. Door `/api/status` is `PLAYABLE`. Door ephemeral `:25565` may show MOTD (mcdoor still binds `0.0.0.0`); it must not TCP-timeout. Idle timer left **disabled**.  
**Refs:** `src/McManager.Core/Setup/SetupBootstrapService.cs`, `door_vm/scripts/promote_playable.sh`, `door_vm/oci/start_vm1.sh`, `door_vm/src/mcdoor.c`, E2E **F7**

### SETUP-ISSUE-7 — After SoftStop reboot, Oracle `netfilter-persistent` kills firewalld (25565 closed)
**Status:** Fixed (2026-08-18) — cloud-init + Setup guest repair  
**Summary:** Second greenfield E2E play path worked, then a later Start-after-Stop left door **DEGRADED** (`wait_forge.sh timed out`). VM1 Minecraft was `active` and listening on `0.0.0.0:25565`. Door probe to VM1 private `:25565` was **No route to host**.  
**Cause:** Canonical OCI Ubuntu images persist SSH-only `INPUT REJECT` via **`netfilter-persistent`**. Cloud-init enables **firewalld** (SSH + 25565 tcp/udp). `firewalld.service` **Conflicts** with `iptables.service` (alias of netfilter-persistent). After the first reboot, netfilter-persistent starts and firewalld stays **inactive (dead)** — no journal, no 25565. Public play and door wait_forge both fail.  
**Fix (product path):** Cloud-init **disable + mask** `netfilter-persistent` **before** `systemctl enable --now firewalld`. Setup `EnsureVm1Runtime` applies the same mask + firewalld ports on Re-Deploy / guest repair (existing stacks).  
**Verify (2026-08-18, test VM1):** Before: firewalld inactive, INPUT REJECT, door TCP **FAIL**. After mask + firewalld: ports `25565/tcp 25565/udp`, door `diagnose_wait_forge` **OK**.  
**See also:** **OS-ISSUE-9** — a later TESTING boot still had firewalld/dbus dead with netfilter already masked. Cause was a **firewalld/cloud-init/dbus ordering cycle** (systemd deleted dbus), not UFW nft fight. UFW is still masked as firewalld-only SoT. Do not assume every dead-firewalld boot is this issue.  
**Refs:** `infra/cloud-init/vm1.yaml.tftpl`, `SetupBootstrapService.EnsureVm1HostFirewall`

### SETUP-ISSUE-8 — Installed `repair-permissions.sh` fails: `cp` env.sh onto itself
**Status:** Fixed (2026-08-17) — `layout.sh` skip-same-file copy; wipe no longer calls this script  
**Summary:** Manager **Wipe world** (V1 Step 1.3) stopped Minecraft, deleted `/opt/mcmgr/server/world`, then ran `/opt/mcmgr/bin/repair-permissions.sh`. That script reported `[mcmgr] layout: applying §5 permission contract` and died: `cp: '/opt/mcmgr/lib/env.sh' and '/opt/mcmgr/lib/env.sh' are the same file`. Wipe reported failure and started Minecraft again even though the live save was already gone. **Replace world** and Troubleshooting **Repair game permissions** (when using the installed `/opt/mcmgr/bin/` copy) hit the same `cp`.  
**Cause:** `layout_apply` always `cp -f` `common/env.sh` (and siblings) into `/opt/mcmgr/lib/`. When repair is run from the **installed** tree, source and dest are the same inode. GNU `cp -f` of a file onto itself is an error (`set -e`). Staging-tree runs (`/tmp/mcmgr-onbox/…`) were fine.  
**Fix (product path):** `_layout_cp_unless_same` in `onbox/mcmgr/common/layout.sh` (`[[ src -ef dest ]]` → skip). Wipe recreates the world dir with `mcmgr:mcmgr` `0750` and does **not** invoke `repair-permissions.sh` (blueprint §11.3 — world folder only).  
**Refs:** `onbox/mcmgr/common/layout.sh`, `McManager.Core/Services/WorldWipe.cs`

### SETUP-ISSUE-9 — Greenfield `tofu apply` fails: budget description >200 chars + OCIR 404-DENIED
**Status:** Fixed (2026-08-20) — product `infra/modules/budget_brake`  
**Summary:** Pass 2 Phase A greenfield Setup on TESTING created the compartment, VCN, both VMs, Functions app, and IAM, then `tofu apply` failed. Two errors in the same apply:  
1. `oci_budget_budget.one_usd` → `400-InvalidParameter, description size must be between 0 and 200` (HCL description was **208** characters).  
2. `oci_artifacts_container_repository.softstop` → `404-DENIED` on the **new** stack compartment (~1 min after Identity create). Functions Application and Object Storage in that same compartment succeeded. No leftover `mcmgr-fn/softstop` repo (tenancy list empty).  
**Cause:** OCI Budgets cap `description` at 200. Artifacts/OCIR lags Identity on a brand-new compartment; the provider treated 404-DENIED as fatal after ~1 min of create.  
**Fix (product path):** Shorten the budget description (plan-time precondition ≤200). When the module **creates** the compartment, wait **2 min** before `CreateContainerRepository` and use a **10 min** create timeout so Artifacts can catch up.  
**Retry:** Do **not** destroy the partial stack (A1 already exists). Close Manager (Deploy stays locked after the first click), reopen the same TESTING config dir, Setup resume → **Deploy**. First retry may wait ~2 min for the new `time_sleep` before OCIR.  
**Refs:** `infra/modules/budget_brake/main.tf`, `infra/main.tf`, `infra/versions.tf` (`hashicorp/time`)

### SETUP-ISSUE-10 — VM1 cloud-init YAML invalid: `indent()` left `[Unit]` unindented; no marker
**Status:** Fixed (2026-08-20) — product `infra/cloud-init/vm1.yaml.tftpl` + Setup guest repair  
**Summary:** Pass 2 Phase A retry: tofu apply succeeded, then Setup timed out 20 min on `/etc/mcmgr/cloud-init-done` (`Last: WAIT`). SSH: `cloud-init status` was **degraded done** in ~14 s; `/etc/mcmgr` did not exist; no `mcmgr` user; **firewalld not installed**. User-data failed YAML parse: `expected <block end>, but found '['` at `[Unit]` inside `write_files` `content: |`.  
**Cause:** `vm1.yaml.tftpl` used `${indent(6, firewalld_unit)}` on its own line. OpenTofu `indent()` does **not** indent the first line, so `[Unit]` sat at column 1 and YAML treated it as a flow sequence. Cloud-init dropped the entire `#cloud-config` (`Failed at merging in cloud config part from part-001: empty cloud config`) — no `packages:`, no `runcmd`, no marker. This is not SETUP-ISSUE-5 (the waiter already uses `sudo -n test -f`; the file was never written). Instance `metadata` is `ignore_changes`; fixing the template does not rewrite this VM’s user_data.  
**Fix (product path):** Embed the firewalld unit as `encoding: b64` + `base64encode(...)` (no YAML literal). Guest repair `EnsureVm1HostFirewall` `apt-get install`s firewalld/unzip if missing, writes the marker, and runs at the **start** of VM1 bootstrap. Waiter continues when `cloud-init status` is **done** without a marker so it does not wait 20 min.  
**Retry:** Do **not** destroy. Restart Hybrid (C# waiter/repair), same TESTING config dir, Setup resume → **Deploy**. Marker may already exist if an agent wrote it on the live VM.  
**Refs:** `infra/cloud-init/vm1.yaml.tftpl`, `infra/main.tf`, `SetupBootstrapService.WaitCloudInitAsync` / `EnsureVm1HostFirewall`

### SETUP-ISSUE-11 — Setup / Change pack health treated crash-loops as an RCON timeout
**Status:** Fixed (2026-08-21) — product Manager health check  
**Summary:** Informal Change pack tests (cluster A) failed with `Minecraft unit started but RCON list did not succeed in time` while the unit was crash-looping (`status=1`, journal `FATAL` / loader abort). Slow first world gen used the same copy, so operators could not tell the difference and the waiter burned the full RCON budget.  
**Cause:** `WaitRcon` only polled `systemctl is-active` + localhost RCON `list`. Joinable still correctly requires RCON (blueprint §12.1 step 9); the gap was no fail-fast on crash-loop / FATAL.  
**Fix (product path):** Combined SSH probe (localhost RCON + `NRestarts`/`ActiveState` + `journalctl -u minecraft`). Crash-loop / FATAL / loader “caused the server to crash” / `NoClassDefFoundError` abort / `UnsupportedClassVersionError` fail immediately, stop the unit so it does not keep restarting, and show a capped journal excerpt plus the implicated mod when the loader printed one. RCON success still wins. Timeout without a crash says so.  
**Verify:** `dotnet test` filter `MinecraftReadinessTests`. Optional live Change pack that is known to crash should no longer sit on the generic RCON message.  
**Refs:** `src/McManager.Core/Setup/MinecraftReadiness.cs`, `SetupBootstrapService.WaitRcon`, `docs/V1-Modpack-Test-Follow-On-Plan.md` P1

### SETUP-ISSUE-12 — Fabric `.mrpack` leftover client GUI mods still installed
**Status:** Fixed (2026-08-21) — product Layer 2 overlay + leftover in-jar peek  
**Summary:** Informal Change pack Test 4 (cluster C) installed a loading-screen / FlatLaf-class Fabric GUI mod. Analyze showed **Pack-declared: 0** / override-list 22: the pack tagged every file `env.server=required` (env was not ignored; authors mis-declared). itzg Layer 1 caught Sodium/Iris-class names; leftover GUI/loading-screen jars were not a list class and were not peeked.  
**Cause:** `.mrpack` filter trusted `env.server` then Layer 1–2 only. Product overlay was empty. In-jar Fabric `environment` / client-only entrypoints (P2) ran on unstructured zips, not leftover `.mrpack` files.  
**Fix (product path):** Overlay classes `loading-screen`, `konkrete`, `titlebar`, `flatlaf` (not a single Test 4 filename). After env + list, leftover jars with in-jar client metadata are skipped (missing `env.server` + client entrypoints only counts as client). Analyze peeks jars embedded in the archive; install also peeks downloaded jars. Force-include still wins.  
**Verify:** `dotnet test` filter `MrpackAnalyzerTests|MrpackInstallerTests|ExcludeIncludeMatcherTests`. Optional: Change pack OptiFine-for-Fabric should skip loading-screen class in the confirmable summary.  
**Refs:** `src/McManager.Core/Setup/pack-lists/mcmgr-exclude-include.json`, `MrpackFileFilter`, `MrpackAnalyzer` / `MrpackInstaller`, `docs/V1-Modpack-Test-Follow-On-Plan.md` P3

### SETUP-ISSUE-13 — Existing VM1 lacked Layer 3 `quarantine_mod` until layout_apply
**Status:** Fixed (2026-08-23) — product `layout.sh` + TESTING one-shot copy  
**Summary:** Step 8.8 P10 installs `quarantine_mod.py` / `quarantine_mod.sh` via `layout_apply`. Stacks created before that change have no `/opt/mcmgr/lib/quarantine_mod.py`, so Setup/Change pack Layer 3 retry and Manager **Keep excluded** / **Put back** fail until the next driver/`layout_apply` or a one-shot copy.  
**Cause:** Existing TESTING VM1 predates P10; door deploy does not push VM1 on-box helpers.  
**Fix (product path):** `onbox/mcmgr/common/layout.sh` copies the helper to `/opt/mcmgr/lib` + `/opt/mcmgr/bin`. Greenfield Setup / Change pack / `repair-permissions.sh` pick it up. TESTING VM1 received a one-shot copy 2026-08-23 (`root:mcmgr` 0640 / 0755; on-box self-test OK). Other existing VMs: copy-paste in [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md).  
**Verify:** `sudo test -f /opt/mcmgr/lib/quarantine_mod.py`; `sudo python3 /opt/mcmgr/lib/quarantine_mod.py self-test --self-test-root /tmp/mcmgr-p10-selftest`.  
**Refs:** `onbox/mcmgr/common/quarantine_mod.py`, `onbox/mcmgr/common/quarantine_mod.sh`, `docs/V1-Operator-Notes-Follow-On-Plan.md` P10

### DOOR-ISSUE-4 — `wait_forge.sh` / `ip_to_vm1.sh` abort wake (`set -u` CR-strip, missing `--force`)
**Status:** Fixed (2026-08-14)  
**Summary:** After Minecraft was reachable on VM1 private `:25565`, door wake still went **DEGRADED**. First `POLL_INTERVAL_SEC: unbound variable` (CR-strip under `set -u` before `:-10`). After a live patch, `ip_to_vm1.sh failed` until IAM (SETUP-ISSUE-2) plus `--force` (reserved IP parked on the door secondary). Sourcing `/etc/mccontrol/oci.env` as `ubuntu` is **Permission denied** (mode 600 root) — expected; not a misdeploy.  
**Fix:** Default optional env vars before CR-strip; `ip_to_vm1.sh` / `ip_to_vm2.sh` `--force` and exit 0 if already on the target private IP.  
**Verify:** `diagnose_wait_forge.sh` OK (as root), then wake → PLAYABLE after IAM is in place.  
**Refs:** `door_vm/oci/wait_forge.sh`, `door_vm/oci/ip_to_vm1.sh`, `door_vm/oci/ip_to_vm2.sh`

### DOOR-ISSUE-5 — `wait_forge.sh` timed out; door cannot TCP to VM1 `:25565` (blank-tenancy test)
**Status:** Fixed (2026-08-15) — leading cause was SETUP-ISSUE-4  
**Summary:** After Manager Start on the product Setup test stack, door status is **Degraded — wait_forge.sh timed out**. `diagnose_wait_forge.sh` showed matching `VM1_PRIVATE_IP` (`10.0.0.37`) then **FAIL** TCP to `:25565`.  
**Cause:** Minecraft never listened because `minecraft.service` died on `CHDIR`. Private IP / VCN SL were not the blocker.  
**Verify (2026-08-15, after Step 4.2 repair + `systemctl start minecraft`):** VM1 `ss` `0.0.0.0:25565`; door `diagnose_wait_forge.sh` **OK: 10.0.0.37:25565 accepts TCP**. Sticky DEGRADED leftover: Manager **Troubleshooting → Diagnose wait_forge / Reset door state / Unstick after game is up** (MVP Step **4.4**).  
**Refs:** `door_vm/oci/wait_forge.sh`, `door_vm/scripts/diagnose_wait_forge.sh`

### DOOR-ISSUE-6 — Wake START 409 when VM1 is already up; mcdoor accept queue stalls
**Status:** Fixed (2026-08-17) — `start_vm1.sh` + mcdoor I/O timeouts  
**Summary:** With VM1 already RUNNING, a Minecraft login on the door (reserved IP still parked there after Setup) started wake. `start_vm1.sh` issued `START` → **409** `instance is currently being modified` / already running. `do_wake` treated that as failure → **DEGRADED**, skipped `ip_to_vm1`. Meanwhile mcdoor handled client sockets with **blocking `recv`** and backlog 16; hung status/login connections filled the accept queue so later reserved-IP and door-ephemeral connects TCP-timed out.  
**Fix:** `start_vm1.sh` GetInstance first; skip START when `RUNNING`/`STARTING`; if START fails, succeed when lifecycle is already up. mcdoor `SO_RCVTIMEO`/`SO_SNDTIMEO` 8s on accepted clients; listen backlog 128. Setup no longer relies on this wake to place the play IP (SETUP-ISSUE-6).  
**Verify:** `sudo bash /opt/mccontrol/oci/start_vm1.sh` while VM1 is RUNNING prints `already RUNNING` and exit 0. `ss -lntp | grep 25565` Recv-Q stays near 0 under client pings.  
**Refs:** `door_vm/oci/start_vm1.sh`, `door_vm/src/mcdoor.c`, `door_vm/src/control.c` `do_wake`

### DOOR-ISSUE-7 — After VM1 idle SoftStop, doorbell timed out (IP / STOP 409 / listen)
**Status:** Fixed (2026-08-17) — reconcile handback + `stop_vm1.sh` already-down no-op; original timeouts also DOOR-ISSUE-6  
**Summary:** After idle SoftStop, Minecraft to the **reserved play IP** and the **door ephemeral** both TCP-timed out. Manager showed door **`DOOR_IDLE`**. Wake did not start VM1.  
**Causes (stacked):**  
1. First E2E: Setup had left the reserved IP on the **door** already (`DOOR_IDLE`) while mcdoor’s accept queue was stalled (**DOOR-ISSUE-6**) — nothing to hand back; TCP timeout was listen, not “wrong VM.” F7 rebuilt mcdoor.  
2. After F7 parked the IP on **VM1** (`PLAYABLE`), idle/equivalent SoftStop must move it back. Reconcile **did** POST idle-empty on PLAYABLE+STOPPING, but `stop_vm1.sh` issued another SOFTSTOP → OCI **409** `currently being modified`. Handback still continued (`control_stop` ignores stop failure), yet the 409 is the same class of bug as wake START 409.  
3. If `door_state` is already **`DOOR_IDLE`** (reset, or persist-idle before `ip_to_vm2` finishes), reconcile used to **skip** IP move — reserved address can stay on STOPPED VM1 (black hole) while Advanced still says idle.  
**Fix (product path):** `stop_vm1.sh` skips SOFTSTOP when VM1 is already STOPPED/STOPPING (409 on already-down = success). Reconcile still runs **`ip_to_vm2.sh`** when idle/exhausted and VM1 is down. `install.sh` installs `scripts/` + enables `mccontrol-reconcile.timer`. Setup guest repair also deploys `stop_vm1.sh` / `ip_to_vm2.sh` / `reconcile_vm1.sh`.  
**Verify (2026-08-17, equivalent SoftStop, idle left off):** VM1 STOPPED → reserved IP on door secondary; MOTD TCP ~0.03s on reserved IP **and** door ephemeral (`Server offline…`). Reconcile log `already STOPPING — skip SOFTSTOP` then `DOOR_IDLE`. `POST /api/wake` → VM1 RUNNING (wait_forge / joinable is **F9**). Door ephemeral `:25565` is MOTD, not the Vanilla world. Java is TCP-only (no UDP doorbell).  
**Refs:** `door_vm/oci/stop_vm1.sh`, `door_vm/scripts/reconcile_vm1.sh`, `door_vm/install.sh`, `src/McManager.Core/Setup/SetupBootstrapService.cs`, E2E **F8**

### DOOR-ISSUE-8 — Manager Start stuck “starting”; Minecraft not joinable after first idle SoftStop
**Status:** Fixed (2026-08-17) — wait-for-RUNNING + TCP probe cap + DEGRADED recovery  
**Summary:** After the first idle SoftStop of a greenfield VM1, Manager Start brought VM1 **RUNNING** but Status stayed **Starting…** for **> 10 minutes** and the reserved play IP never became joinable. A later Stop + Start (third try) worked.  
**Causes (stacked):**  
1. `start_vm1.sh` returned as soon as OCI accepted **START** (or saw **STARTING**). `wait_forge` then spent its default **600s** TCP budget while the instance was still coming up. A slow first A1 start after the first-ever SoftStop could expire that clock before Java listened → **DEGRADED**, reserved IP still on the door.  
2. Each `/dev/tcp` probe had **no timeout**. A DROP (no RST) can hang a probe for minutes and push wait_forge well past 10 minutes, which matches Manager staying on Starting….  
3. After wait_forge timeout, VM1 was often still **RUNNING**. F5 disabled **Start** whenever VM1 is on, so the operator could not retry wake without Stop first — the “third Start worked” pattern. Sticky **DEGRADED** also needed Troubleshooting unstick (`promote_playable` / reset).  
Minecraft **was** `systemctl enable`d (CHDIR did not regress). After F7/F8 listen/handback, a later Start already reached PLAYABLE in ~40s; F9 hardens the remaining race.  
**Fix (product path):** `start_vm1.sh` waits until **RUNNING** (exponential poll, few seconds → 30s, ~20 min) before returning. `wait_forge.sh` caps each TCP probe at **5s**. Reconcile runs **`promote_playable.sh`** when door is STARTING/DEGRADED, VM1 is RUNNING, wake is not in progress, and private `:25565` accepts TCP. Manager **Start** stays enabled on **DEGRADED** even if VM1 is RUNNING; Start wait is **30 min**. Setup guest repair also deploys `wait_forge.sh`.  
**Verify (2026-08-17, idle off):** Two Start-after-SoftStop cycles on the test stack: door PLAYABLE in **~109s** then **~76s**; `start_vm1` RUNNING after ~14s; wait_forge TCP ~30s; reserved play IP TCP connect OK. Left VM1 **STOPPED**, IP on door, `DOOR_IDLE`, idle **off**. Typical joinable time **~1–2 minutes** after Start; **>10 min with no game** is still failure.  
**Refs:** `door_vm/oci/start_vm1.sh`, `door_vm/oci/wait_forge.sh`, `door_vm/scripts/reconcile_vm1.sh`, `src/McManager.Hybrid/ViewModels/MainViewModel.cs`, `SetupBootstrapService.cs`, E2E **F9**

### DOOR-ISSUE-9 — Manager Stop times out (`POST /api/idle-empty` 10s) while handback still succeeds
**Status:** Fixed (2026-08-18) — async idle-empty like wake  
**Summary:** Second greenfield E2E: Manager **Stop** SoftStopped VM1 and moved the reserved IP to the door, but the app showed `POST /api/idle-empty failed: … HttpClient.Timeout of 10 seconds`.  
**Cause:** `POST /api/idle-empty` ran `stop_vm1.sh` + `ip_to_vm2.sh` **on the HTTP thread** and only then returned 200. Those OCI calls routinely exceed 10s. Manager used the short door client (wake is 202 + background thread). On timeout, Stop skipped “wait until off” even though the door finished the work. Door HTTP is single-threaded, so status polls also queued behind the sync stop.  
**Fix (product path):** `control_stop(..., async)` — HTTP returns **202** immediately; SoftStop + IP handback run on a thread; `/api/status` includes `stop_in_progress`. Persist `DOOR_IDLE` **after** `ip_to_vm2`. Wake is rejected while a stop is in flight. Manager IdleEmpty uses the long client and waits until idle **and** `stop_in_progress` is false.  
**Verify (2026-08-18, test door, PLAYABLE):** `POST /api/idle-empty` **202** in **~0.5 ms**; status `DOOR_IDLE` / `stop_in_progress=false`; reserved IP on door secondary; VM1 **STOPPED**. Manager IdleEmpty uses the 2 min client as a fallback for old binaries.  
**Refs:** `door_vm/src/control.c`, `door_vm/src/httpmini.c`, `src/McManager.Core/Services/DoorClient.cs`, `MainViewModel.StopAsync`

### DOOR-ISSUE-10 — Spend-brake lock GET 404 treated as fail-closed (OCI CLI 3.90)
**Status:** Fixed (2026-08-19) — TESTING door redeployed  
**Summary:** Door wake GETs `meta/spend-brake-triggered.json` on every `pull_os_budget.sh` (Step 2.3). QA **S2-11** first failed because the live TESTING script was **pre-2.3** (no GET — wake STARTed VM1 while the lock object existed). After the product script landed, OCI CLI **3.90+** missing-object JSON is `"code": null`, message **The service returned error code 404**, `"status": 404` — not `ObjectNotFound`. The old grep (`status:[[:space:]]*404`) does not match quoted `"status": 404`, so **absent** lock failed closed (`ERROR: spend-brake lock GET failed (not 404)`) and wake would not START (seen in P1). `oci os object get --file` also leaves a **0-byte** cache that C treats as locked. Setup repair copies scripts only — live `mccontrol` needed a rebuild for `SPEND_BRAKE` refuse/MOTD.  
**Fix:** `pull_os_budget.sh` matches CLI 3.90 404 text; deletes the cache unless GET succeeded (fail-closed errors too). TESTING: installed that script and rebuilt `/opt/mccontrol/build/mccontrol` from product `door_vm/`.  
**Verify (TESTING 2026-08-19):** Absent lock → `SPEND_BRAKE_LOCK=0` (no cache file). PUT v1 lock → `POST /api/wake` → VM1 stayed **STOPPED**, door `SPEND_BRAKE`, `last_error=monthly spend brake fired`, journal `SPEND_BRAKE_LOCK=1` (no `start_vm1`). DELETE + `POST /api/os-refresh` → `SPEND_BRAKE_LOCK=0`, `DOOR_IDLE`. **Live Forge lab door may still be stale.**  
**Refs:** `door_vm/oci/pull_os_budget.sh`, `door_vm/src/control.c`, `Agent-Deploy-Pitfalls.md`

### DOOR-ISSUE-11 — Manager Start refused when daily budget exhausted (S5-05)
**Status:** Fixed (2026-08-20) — product `door_vm` + Hybrid Start wait  
**Summary:** Pass 1 **S5-05**: after daily cap was lowered below used hours, player connect correctly showed **daily** MOTD/kick (not spend-brake), but **Manager Start was also refused**. PRODUCT-IDEAS / Pass 1 bug-fix plan: door refuses **player** wake; **admin Start from Manager** must still work. Spend-brake overlay must still block Start.  
**Cause:** Manager `POST /api/wake` and Minecraft login both called `control_wake` → `do_wake`, which always refused daily exhaustion. Hybrid Start wait also treated `BUDGET_EXHAUSTED` as a terminal state, so a Start click returned immediately while the door was already exhausted.  
**Fix:** `control_wake(..., admin_override)`. HTTP `/api/wake` (admin-CIDR) passes `1` and skips the daily OCPU gate; `control_on_login_wake` passes `0`. Spend-brake lock and soft monthly cap still refuse both. Hybrid Start waits for PLAYABLE / DEGRADED / SPEND_BRAKE, not BUDGET_EXHAUSTED.  
**Verify (TESTING 2026-08-20):** Lowered daily (monthly target 15 → ~0.48 OCPU-h) while used ~4.17. Minecraft login kick: `DAILY BUDGET FULFILLED…` (not spend-brake); door `last_error=daily budget exhausted`. `POST /api/wake` **202** → **PLAYABLE** ~1 min, VM1 RUNNING. After idle-empty: PUT v1 lock → admin wake → VM1 **STOPPED**, door `SPEND_BRAKE`, `last_error=monthly spend brake fired`. DELETE lock + restored original budget; door `DOOR_IDLE`; lock **404**; play IP on door secondary.  
**Refs:** `door_vm/src/control.c`, `door_vm/src/httpmini.c`, `src/McManager.Hybrid/ViewModels/MainViewModel.cs`, `archive/V1-Bug-Fix-Plan-Pass-1.md` P6

---

## Idle agent (VM1)

### IDLE-ISSUE-1 — Idle timeout did not SoftStop; oneshot logs `Minecraft inactive; nothing to do.`
**Status:** Fixed in agent (2026-08-15); play path unblocked by Step 4.2 (game can start). Empty-`active` SoftStop not re-proven this session (idle left disabled).  
**Summary:** On the blank-tenancy test VM1, operator set idle timeout to 15 minutes and left the VM up. After 15+ minutes it did **not** SoftStop. `mc-idle-watch.timer` was **enabled** and **active (waiting)**. The oneshot `mc-idle-watch.service` last ran successfully (`status=0`) with log **`Minecraft inactive; nothing to do.`**  
**Cause:** `idle_watch.py` returned immediately when `systemctl is-active <minecraft_unit>` was false. Idle timeout only applied to an **empty running Minecraft**. Minecraft was down because of SETUP-ISSUE-4 (`200/CHDIR`).  
**Fix (MVP Step 4.1):** `vm_agent/idle_watch.py` now starts the same idle clock when the unit is not `active`; first tick does not SoftStop; skip RCON/`systemctl stop` if already down; cold backup if `world_path` exists; then ledger/lease + OCI SoftStop. Redeployed to test VM1 `/opt/mc-manager`.  
**Verify (2026-08-15, timeout=2 min, Minecraft stopped):** journal `Minecraft not active; idle timer started.` → … → `Stopped instance after: Minecraft not running for 2 minutes.` VM1 lifecycle **STOPPED**. Empty-server SoftStop while `active` still needs a short idle-enabled proof after 4.2 (operator left idle disabled during permission work).  
**Refs:** `vm_agent/idle_watch.py`

---

## How to add issues

1. New id: `AREA-ISSUE-N`  
2. Status + short summary + cause/fix/verify  
3. Link code/docs paths (no live OCIDs/secrets)  
4. **Setup / automated-deploy bugs:** if the failure came from OpenTofu, IAM matching rules, cloud-init, SSH bootstrap, `onbox/mcmgr/`, `door_vm/` install, or `vm_agent/` install — file it here **and** change that product path in the same effort. Do **not** only patch the live test VM. Example: SETUP-ISSUE-2 (door DG + tenancy `manage public-ips`) had to land in `infra/`.  
5. Operator copy-paste commands for a new failure mode: add a section to [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md).
