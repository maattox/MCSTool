# VM software — game VM (VM1) and door (VM2)

What runs on each Always Free host. The desktop Manager is **`McManager.Hybrid`** in this repo. Neither the Manager nor SSH is required for friends to play once the cloud side is up.

| Host | Shape (intent) | Role |
|------|----------------|------|
| **VM1** | `VM.Standard.A1.Flex` (target 4 OCPU / 24 GB) | Minecraft + idle/budget agent + ledger publish |
| **VM2** | Always Free **E2.1.Micro** (~1/8 OCPU) | Door / doorbell: MOTD, wake, IP parking, OS wake-pull, one-shot orphan heal |

**Cost:** stay on Always Free–eligible resources; keep spend **$0** unless explicitly accepted.

| Host | Shape (intent) | Role |
|------|----------------|------|
| **VM1** | `VM.Standard.A1.Flex` (target 4 OCPU / 24 GB) | Modded Minecraft (Forge) + idle/budget agent + ledger publish |
| **VM2** | Always Free **E2.1.Micro** (~1/8 OCPU) | Door / doorbell: MOTD, wake, IP parking, OS wake-pull, one-shot orphan heal |

**Cost:** stay on Always Free–eligible resources; keep spend **$0** unless explicitly accepted.

---

## Current build status (agents — read this)

**Live / functional today** (operator Always Free stack + this repo):

| Area | Status |
|------|--------|
| Dual-VM doorbell (reserved play IP on VM2 idle / VM1 playable) | **Built & live** |
| Door `mccontrol` (C): MOTD, wake, HTTP `:8080`, IP move | **Built & live** — SoT `door_vm/` |
| IP allowlist (Security List + VM1 firewalld) via Manager | **Built & live** — product Manager CIDR prefixes for Minecraft 25565 (`/9`–`/32`; SSH/door stay `/32` except own admin) |
| VM1 idle/budget SoftStop + local ledger | **Built & live** (empty **or** Minecraft not running, same timeout) — SoT `vm_agent/` + TESTING VM1 redeployed (MVP Step 4.1). Game can start after Step 4.2 (CHDIR fixed); empty-`active` SoftStop not re-proven while idle is left disabled |
| Object Storage Phases 1–5 (ledger/budget/flags, wake gate, orphan heal, lease heartbeat, live shape detect) | **Built & live** (redeploy agent + door Phase 4 scripts) |
| Door reconcile: handback + **one heal per down episode** (`ledger_heal_verified`); heal only when **STOPPED** | **Built & live** (minimize OS chatter) |
| VM1 boot: force-pull OS + lease, list-boots reconcile, repair `stop_uncertain`, **detect live shape**, **force-enable idle agent** | **Built & live**; redeploy idle agent after pulls to ensure VM1 matches |
| VM1 world backup to Object Storage on SoftStop / graceful stop (9.5 GiB soft cap + oldest zip eviction) | **Built & live** — cold after stop; **live** `save-off`/`flush`/`save-on` path ready (`world_backup.py live`) |
| VM1 boot force-enables idle agent + rewrites OS `budget/config.json` `idle_agent_enabled=true` | **Built & live** — testing disable does not survive Minecraft restart (OS-ISSUE-7) |
| Manager Testing2 door/OS helpers | **Historical** (old Python Manager). Product path is Hybrid Troubleshooting + Setup deploy |
| Product Manager | **Manage + Setup usable** on `McManager.Hybrid` (WPF + WebView2 WinExe). **Phase B DONE.** Happy-path guide **DONE**. **Delete infrastructure** is on **Danger Zone**. MVP Phases 0–7 **DONE**. Living execution: **V1 plan NEXT = Step 8.8 P11** (operator-notes follow-on; P1–P10 **DONE**; do not start Pass 3 until 8.8 exits **and** the operator says so; Step **8.4** P1–P13 DONE; do not start 9.1 until QA exits **and** Step **8.6.1** is DONE). **Paid / spend mode skipped** (later / far future). Wipe world is in Hybrid Server Management. Spend-brake full-window lock UX is in Hybrid (Step 2.4). Packaging is V1 Phase 9. **Function image product path** (CI-built ARM, copy into OCIR, no Docker on the admin PC) is V1 Phase **8.6**. Novice status = **Running / Stopped**; door/VM technical status on Advanced; pinned usage hours. **Connect-existing usable** (Phase 5). Step **4.4 Troubleshooting one-shots DONE**. On-box SoT: `door_vm/`, `vm_agent/`, `functions/shutdown_vm/`, `functions/reconcile_usage/`. Layout/visual choices are **not locked**. |
| `$1` budget Function `shutdown_vm` | **Source updated (v1 Step 2.2)** — `functions/shutdown_vm/` SoftStops **VM1 only** + PUTs `meta/spend-brake-triggered.json`; door left running. **TESTING:** `mcmgr-fn-softstop` / `mcmgr-fn/softstop:setup` **0.0.12**. **Forge lab (`DEFAULT`):** `budget-repo/shutdown_vm:0.0.12` (2026-08-27; VM1 only + lock PUT). **Product path before release (V1 Step 8.6.1):** CI-built ARM image copied into the user’s OCIR — not Docker Desktop / Cloud Shell on the admin PC. |
| Usage API 48h ledger reconcile Function | **Source (v1 Step 7.7)** — `functions/reconcile_usage/`. Writes `daily_overrides` for days older than ~48h. **TESTING** agents may deploy/invoke (prefer dry-run). |
| Door honors spend-brake lock | **Source updated (v1 Step 2.3)** — product `door_vm/` GETs the lock on wake pull and refuses START (`SPEND_BRAKE` MOTD). **TESTING redeployed 2026-08-19** (S2-11 / DOOR-ISSUE-10: CLI 3.90 404 = unlocked). **Forge lab door redeployed 2026-08-27** from current `door_vm/`. |

**Known open quirks:** [`Issues.md`](Issues.md) (MOTD first-kick, Force Start dual-write, FN-ISSUE-1 **gone on TESTING and Forge lab** after 0.0.12). **OS-ISSUE-9** ACPI STOPPING hang **fixed** (firewalld/cloud-init/dbus drop-in + mask UFW). **DOOR-ISSUE-10** spend-brake 404 fail-closed **fixed** on TESTING. **SETUP-ISSUE-3** in-game whitelist off, **SETUP-ISSUE-4** CHDIR, **SETUP-ISSUE-5** cloud-init marker `WAIT` (ubuntu vs `/etc/mcmgr` 0750), **SETUP-ISSUE-6** Setup parks reserved IP on VM1 when the game is already up, **SETUP-ISSUE-7** firewalld vs Oracle netfilter-persistent after reboot, **DOOR-ISSUE-5** wait_forge TCP, **DOOR-ISSUE-6** wake START-on-RUNNING 409 + mcdoor accept stall, **DOOR-ISSUE-7** idle handback, **DOOR-ISSUE-8** Start-after-idle wait_forge, and **DOOR-ISSUE-9** Manager Stop idle-empty 10s timeout **fixed**. **IDLE-ISSUE-1** fixed in agent. Operator commands: [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md).

---

## Document map

| Doc | Use |
|-----|-----|
| [`Infrastructure-Information.md`](Infrastructure-Information.md) | Full OCI architecture (placeholders) |
| [`../door_vm/README.md`](../door_vm/README.md) | **Tracked** door tree + redeploy outline |
| [`../vm_agent/README.md`](../vm_agent/README.md) | **Tracked** VM1 agent tree (idle, ledger, world backup) |
| [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md) | Door state machine / scripts deep dive |
| [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) | Shared ledger/budget object names and writers |
| [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) | Living v1 execution checklist |
| [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) | **Agents only:** SSH/sudo/SFTP deploy failure modes |
| [`Issues.md`](Issues.md) | Known bugs / quirks |
| [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md) | Operator copy-paste commands (VM1 + door) |
| [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) | Product intent (**UI sketches not locked**) |
| [`../functions/shutdown_vm/`](../functions/shutdown_vm/README.md) | $1 budget Function (SoftStop VM1 + lock PUT; door left running) |
| [`../functions/reconcile_usage/`](../functions/reconcile_usage/README.md) | Usage API 48h ledger reconcile Function |
| `data/config.local.json` | **Gitignored** live TESTING OCIDs / secrets |

---

## Repo structure (software SoT)

```text
OCI-mc-server/
  src/                   ← Manager (McManager.Hybrid WinExe + Core)
  door_vm/               ← door Micro SoT
  vm_agent/              ← VM1 idle/boot/OS publish SoT
  functions/shutdown_vm/ ← $1 budget Function SoT
  functions/reconcile_usage/ ← Usage API 48h ledger reconcile Function
  onbox/mcmgr/           ← Minecraft bootstrap SoT
  infra/                 ← OpenTofu greenfield
  docs/                  ← living product + infra docs
  data/                  ← gitignored local config / sample packs
```

Greenfield game install targets `/opt/mcmgr/` + `/etc/mcmgr/game-manifest.json` (see product blueprint). Live operator lab may still use `/home/ubuntu/minecraft` until a fresh Setup deploy.

---

## VM1 — Minecraft + idle agent

### On-box paths (typical)

| Path | Purpose |
|------|---------|
| systemd `minecraft` | Forge server |
| `/opt/mc-manager/` | Idle agent install (Python venv + scripts) |
| `/etc/mc-manager/config.json` | Agent config (RCON localhost, budgets, OS bucket) |
| `/var/lib/mc-manager/usage.json` | Local usage ledger |
| `/var/lib/mc-manager/lease.json` | Active-session lease / heartbeat |
| `/var/lib/mc-manager/idle_state.json` | Idle / budget warn state |
| `mc-idle-watch.timer` | Periodic idle/budget check + lease heartbeat |
| `mc-boot-ledger.service` | Oneshot: force-pull OS+lease → reconcile → detect shape → boot+lease → publish |

### Repo sources

| Path | Role |
|------|------|
| `vm_agent/` | Tracked agent: see [`../vm_agent/README.md`](../vm_agent/README.md) |
| Manager **Deploy / Update Idle Agent** | Push + install over SSH |

### Object Storage (Phase 2 + 4 + 5)

- **Publish** `ledger/usage.json` on boot / SoftStop (retries on idle path); dirty `ledger.manager` + `ledger.door`  
- **Lease** `ledger/lease.json` heartbeat ~every 5 min while up; cleared on stop (no dirty flags)  
- **Boot:** force-pull ledger+lease; merge; list-boots close/fill; repair `stop_uncertain` (never later than estimate); **detect live OCPU/memory** and sync shape to local config + OS budget  
- **Idle SoftStop:** after idle timeout if empty **or** Minecraft not running; `timeout` on `systemctl stop` when the game is up (skip RCON/stop if already down); close+save local **before** SoftStop; clear lease; retry publish  

RCON stays **localhost only** (`25575` never public).

### Firewall

- **firewalld** per-IP rich rules for Minecraft `25565` TCP/UDP (Manager sync)  
- SSH service left open on host for break-glass; OCI Security List still restricts `:22`

---

## VM2 — door Micro (`mccontrol`)

### Tracked source

**`door_vm/`** is the redeploy SoT for door software (C daemon, OCI scripts, web UI, systemd). See [`../door_vm/README.md`](../door_vm/README.md).

### Responsibilities

1. Hold **reserved play IP** on secondary private IP while idle  
2. Answer Minecraft **status MOTD** / login kick; wake VM1 when allowed  
3. Move reserved IP to VM1 after Forge accepts connections; hand back on stop  
4. **Object Storage wake:** `pull_os_budget.sh` before spend-brake lock + budget gate (`do_wake`); `pull_os_icons.sh` loads Manager-composed MOTD favicons (`idle` / `starting` / `exhausted`)  
5. **Reconcile** (~1 min): external SoftStop → idle-empty; **no** routine budget pull; heal OS ledger **at most once** per VM1-down episode when **STOPPED** (`ledger_heal_verified`); clear latch when VM1 is up / `ip_to_vm1.sh`  

Door stays **C-first**; heavy Python is not the control plane. OCI CLI + small python3 snippets are OK for OS I/O. Scripts must default `HOME` (systemd oneshots omit it — see Issues OS-ISSUE-1).

### Key units

- `mccontrol.service` — daemon (HTTP `:8080`, mcdoor `:25565` when IP on door)  
- `mccontrol-reconcile.timer` → `mccontrol-reconcile.service` → `scripts/reconcile_vm1.sh`

### Manager helpers

Testing2: Deploy Phase 3 / Phase 4 (heal + reconcile + pull + `ip_to_vm1`; Phase 5 heal/lease), Show lease.json, Door pull OS, Door heal, Door reconcile journal, Door OS cache summary.

---

## Shared Object Storage

Bucket (private Standard): prefixes `meta/`, `ledger/`, `budget/`, `ip/`, `messages/`, `backups/world-*.zip`.

Dirty-flag protocol: writer sets consumer bits; consumer clears **only its own** after pull.

| Writer | Objects |
|--------|---------|
| Manager | budget, flags; optional ledger push |
| VM1 | ledger on start/stop; lease heartbeats; **world backup zips** on SoftStop / graceful stop (cold) or live CLI |
| Door | ledger (+ clear lease) only for Phase 4/5 uncertain heal |

World backup soft-caps **total bucket** usage ~9.5 GiB (evict oldest `backups/*.zip` before upload). Details: Phase 1–5 docs + [`Infrastructure-Information.md`](../Infrastructure-Information.md) § World backups.

---

## Preferred player path

1. Idle: reserved IP on door → MOTD  
2. Friend connects / admin Wake → door pulls OS budget → starts VM1 → wait Forge → IP to VM1 (clears heal latch)  
3. Play (boot ledger force-enables idle agent)  
4. Idle SoftStop (empty **or** Minecraft not running — Step 4.1) or budget → stop Minecraft if it was up → **cold world backup** → SoftStop VM1 → IP back to door (API or reconcile) → door may heal open OS interval **once**  

Desktop **Force Start** of VM1 alone does **not** move the reserved IP — use door Wake for the full doorbell path.
