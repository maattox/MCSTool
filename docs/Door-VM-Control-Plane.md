# Door VM (VM2) control plane — how it works

**Audience:** operators and coding agents.  
**Purpose:** Explain the Always Free Micro “doorbell” end-to-end: architecture, state machine, C daemon, shell OCI wrappers, systemd, networking, and budgets.

**Tracked door software SoT:** [`../door_vm/`](../door_vm/README.md) — rebuild/redeploy from that tree if VM2 is deleted.  
**Live generalized infra:** [`Infrastructure-Information.md`](Infrastructure-Information.md)  
**VM overview:** [`VM-Software.md`](VM-Software.md)  
**Known issues:** [`Issues.md`](Issues.md)  
**Operator copy-paste commands:** [`Operator-Troubleshooting.md`](Operator-Troubleshooting.md)  
**Product roadmap / design intent:** [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) — living vision, **not infallible**. Operator will and living plans can override.

### Authority note (read this)

- **`door_vm/`** is the maintained, tracked copy of what should run on VM2 (C `mccontrol`, OCI scripts including Object Storage pull/heal, web UI, systemd).
- **Current stack (Object Storage contract):** when `object_storage_enabled` is true, the door **reads** shared `budget/config.json` + `ledger/usage.json` for the wake gate, and may **heal** orphan open intervals while VM1 is **STOPPED** (prefer `ledger/lease.json` heartbeat). VM1 remains the primary start/stop ledger writer and lease heartbeat publisher; Manager publishes budgets (VM1 may sync detected shape into `budget/config.json`). See [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md).
- Door workload should stay **primarily C** (Micro is ~1/8 OCPU). Object Storage I/O is OCI CLI shell (`pull_os_budget.sh`, `heal_os_ledger.sh`), not a Python control plane on VM2.

---

## 1. What the door is for

Friends always connect to one **reserved public IP** (the “play address”).

| When | Where that IP lives | What answers `:25565` |
|------|---------------------|------------------------|
| **Idle** | Attached to VM2’s **secondary** private IP | **mcdoor** (MOTD + kick / wake-on-join) |
| **Playable** | Attached to VM1’s **secondary** private IP | Real Minecraft/Forge on VM1 |

**VM2** (Always Free Micro, e.g. `minecraft-vm-door`) stays **RUNNING** and runs **`mccontrol`**: wake/stop orchestration, MOTD, small HTTP admin API/UI, Object Storage pull/heal for wake gating and orphan intervals, plus a **legacy local** Phase A ledger used only when Object Storage mode is off.

**VM1** (A1 Flex Forge host) is often **STOPPED** to save Ampere hours.

Each VM also keeps a **primary private IP + ephemeral public IP** for SSH/admin (and door UI on VM2 `:8080`). The reserved IP is *only* ever bound to the **secondary** “play” private IP on whichever VM currently holds it—OCI will not stack a second public IP on a private IP that already has an ephemeral.

```text
Friend → reserved play IP :25565
            │
            ├─ IDLE:      door Micro (mcdoor MOTD / wake)
            └─ PLAYABLE:  VM1 Forge

Admin → door ephemeral :8080  (HTTP UI / API)
Admin → each VM ephemeral :22 (SSH)
```

---

## 2. On-box layout (installed)

Installed from **`door_vm/`** (`install.sh` and/or Manager Testing2 deploy helpers):

| Path | Role |
|------|------|
| `/opt/mccontrol/build/mccontrol` | C daemon binary |
| `/opt/mccontrol/oci/*.sh` | OCI wrappers + `pull_os_budget.sh` + `heal_os_ledger.sh` |
| `/opt/mccontrol/scripts/` | `reconcile_vm1.sh`, `reset_door_state.sh`, diagnostics, … |
| `/opt/mccontrol/web/static/` | Admin SPA (HTML/JS/CSS) |
| `/opt/mccontrol/assets/icons/` | MOTD favicons (`idle.png`, `starting.png`, `exhausted.png` — user/default icon + overlays) |
| `/etc/mccontrol/config.json` | Ports, paths, `object_storage_enabled`, OS cache paths |
| `/etc/mccontrol/oci.env` | OCIDs + OS namespace/bucket + `OCI_CLI_AUTH=instance_principal` (**secrets; mode 600**) |
| `/var/lib/mccontrol/state.json` | Persisted door/play state |
| `/var/lib/mccontrol/ledger.json` | Legacy local Phase A intervals (skipped dual-write in OS mode) |
| `/var/lib/mccontrol/os-cache/` | Pulled `usage.json` / `budget.json` / optional `lease.json` / heal latch |
| systemd `mccontrol.service` | Runs the daemon |
| systemd `mccontrol-reconcile.timer` / `.service` | ~1 min: handback + OS pull + heal |

**Netplan** (written by product Setup bootstrap as `/etc/netplan/99-mcmgr-play.yaml`): secondary play address on the VNIC so the reserved public IP has a private target when parked on the door. Not part of the `door_vm/` C tree.

---

## 3. Source tree (`door_vm/`)

Tracked under sibling [`OCI-mc-server/door_vm/`](../../door_vm/):

| Path | Role |
|------|------|
| `src/main.c` | Daemon entry: threads for mcdoor, HTTP, keepalive |
| `src/control.c` + `include/control.h` | Wake/stop orchestration, OS pull before budget gate |
| `src/mcdoor.c` + `include/mcdoor.h` | Minecraft protocol MOTD / login kick / wake callback |
| `src/mc_proto.c` | Low-level MC packet helpers |
| `src/budget.c` + `include/budget.h` | Ledger load/save, LA-day OCPU math (local + OS cache) |
| `src/state.c` + `include/state.h` | `DoorState` enum + JSON persistence |
| `src/httpmini.c` | Tiny HTTP server: static UI + `/api/*` (incl. `/api/os-refresh`) |
| `src/jsonmin.c` | Minimal JSON (no heavy deps) |
| `src/keepalive.c` | Optional low-priority CPU bursts (Always Free Micro reclaim mitigation) |
| `oci/*.sh` | start/stop/wait/IP move + OS pull/heal |
| `scripts/reconcile_vm1.sh` | Handback (idle-empty or `ip_to_vm2` if already idle) + one-shot OS heal |
| `scripts/promote_playable.sh` | Setup/repair: `ip_to_vm1` then persist `PLAYABLE` (game already up) |
| `install.sh` | Build, install files, iptables, systemd, env prompts |
| `Makefile` | `make mccontrol`, `make test`, … |
| `config.example.json`, `oci/config.example.env` | Templates (no live secrets) |
| `systemd/` | `mccontrol.service` + reconcile timer/service |
| `assets/icons/` | MOTD PNGs (Manager-composed greyscale+overlay defaults; `gen_icons.py` is solid-color fallback) |

Language: **C11**, linked with **pthread**. No Python in the hot path (Python is used in install CRLF strip, reconcile JSON sniff, heal snippets, icon gen).

---

## 4. Process model (`mccontrol`)

`ExecStart=/opt/mccontrol/build/mccontrol /etc/mccontrol/config.json`

On start (`main.c`):

1. Load config → `control_init` (load `state.json` + `ledger.json`).
2. If `enable_mcdoor`: background thread → `mcdoor_serve` on `bind_host`:`mc_port` (default **0.0.0.0:25565**).
3. If `enable_http`: background thread → `httpmini_serve` on **`:8080`** (static UI + API).
4. Start **keepalive** thread (if enabled in config/state).
5. Main thread sleeps forever (`pause`).

Unit environment:

- `EnvironmentFile=-/etc/mccontrol/oci.env`
- `TZ=America/Los_Angeles` (budget day boundary semantics; see §7)

**Important:** mcdoor **always binds** `:25565` when enabled (TCP only; Java Edition). That only “wins” for friends when the **reserved public IP** is currently on the door. When the reserved IP is on VM1, friends hit Forge; the door’s listener is still local to VM2 (including the door **ephemeral** `:25565`) and should answer MOTD, not TCP-timeout. Accepted client sockets use an **8s** recv/send timeout so a hung handshake cannot fill the listen backlog (DOOR-ISSUE-6).

---

## 5. Door state machine

Defined in `state.h` / persisted as `door_state` in `state.json`:

| State | Meaning |
|-------|---------|
| `DOOR_IDLE` | Reserved IP on door; MOTD idle; VM1 expected stopped |
| `STARTING` / `DOOR_STARTING` | Wake in progress (START → wait Forge → move IP) |
| `PLAYABLE` / `DOOR_PLAYABLE` | Reserved IP on VM1; players play Forge |
| `BUDGET_EXHAUSTED` | Daily OCPU cap hit; wake rejected until LA day rolls |
| `SPEND_BRAKE` | `$1` monthly lock object present; **never START VM1** until Manager deletes it |
| `DEGRADED` | Lost track (script failure); needs manual fix / `reset_door_state.sh` |

API/UI may show names with or without the `DOOR_` prefix; reconcile accepts both.

`state.json` also caches: `daily_limit_ocpu_hours`, `ocpus`, `la_day`, `used_ocpu_hours`, `session_started_at`, `hard_stop_deadline`, keepalive fields, `last_error`.

---

## 6. Wake path (heart of the doorbell)

Triggered by:

- Player **login attempt** while IP is on door → `mcdoor` kick + `control_on_login_wake` → async `control_wake(ctx, 1, 0)` (may wake from `DOOR_IDLE`, or from `BUDGET_EXHAUSTED` / `SPEND_BRAKE` after limits rise or Manager deletes the lock). **Daily exhaustion still refuses** this path.
- Admin **POST `/api/wake`** (Manager Start, UI, or curl on the admin-CIDR HTTP port) → async `control_wake(ctx, 1, 1)`. **Skips the daily OCPU gate** so the admin can Start after daily exhaustion. Soft monthly cap and the `$1` spend-brake lock still refuse. Player Minecraft login never uses this endpoint.
- (Not: desktop Force Start alone—that does not move the reserved IP)

### `do_wake` sequence (`control.c`)

When **`object_storage_enabled`**:

1. Run **`pull_os_budget.sh`** (flag-aware or as configured); reload OS caches. Fail closed on pull failure (including a non-404 spend-brake GET).
2. If `os-cache/spend-brake-triggered.json` is present after the pull → `SPEND_BRAKE`, abort (**never** `start_vm1.sh`). MOTD/kick is distinct from daily exhaustion.
3. Budget gate from OS caches: prefer `monthly_ocpu_target / days-in-LA-month` and refuse when month-to-date ≥ `soft_ocpu_cap`. If exhausted → `BUDGET_EXHAUSTED`, abort.
4. **Daily OCPU gate:** player wake (`admin_override=0`) refuses when today’s used ≥ daily limit → `BUDGET_EXHAUSTED`, abort. **Admin HTTP wake skips this step** (S5-05 / DOOR-ISSUE-11). Spend-brake (step 2) still aborts both.
5. If already `STARTING` or `PLAYABLE` → no-op success.
6. Set **`STARTING`**, persist.
7. **`start_vm1.sh`** → START, or skip START if already `RUNNING`/`STARTING` (OCI `START` on a running instance 409s). Then **wait until `RUNNING`** (exponential poll, few seconds → 30s, ~20 min) so `wait_forge` does not spend its TCP budget during compute start. On failure → **`DEGRADED`**.
8. **Skip** local `budget_record_start` dual-write (VM1 owns OS ledger intervals).
9. **`wait_forge.sh`** on VM1 primary private `:25565` (default **600s**, 5s per TCP probe). Timeout → **`DEGRADED`**. Reconcile can later `promote_playable` if the game is already listening.
10. **`ip_to_vm1.sh`** → reserved IP to VM1 secondary. Failure → **`DEGRADED`**.
11. Set **`PLAYABLE`**, clear error, persist.

When Object Storage is **off**, step 1–4 use the local Phase A ledger instead (no spend-brake GET), and step 8 records a local session start + `hard_stop_deadline`.

Wake runs on a **detached thread** when `async=1` so HTTP can return `202` immediately.

### OCI scripts (env from `oci.env`)

| Script | Action |
|--------|--------|
| `start_vm1.sh` | `INSTANCE_ACTION` **START** on `INSTANCE_ID`; skip START if already RUNNING/STARTING; wait until **RUNNING** |
| `stop_vm1.sh` | **SOFTSTOP** on `INSTANCE_ID`; skip if already STOPPED/STOPPING |
| `wait_forge.sh` | TCP wait on `VM1_PRIVATE_IP:25565` (5s probe cap) |
| `ip_to_vm1.sh` | Move reserved IP → `VM1_PRIVATE_IP_ID` (`--force`; no-op if already there) |
| `ip_to_vm2.sh` | Move reserved IP → `VM2_PRIVATE_IP_ID` (`--force`; no-op if already there) |
| `pull_os_budget.sh` | Flag-aware (or `--force`) get ledger/budget; **always** GET `meta/spend-brake-triggered.json` (404 = unlocked); clear door dirty bits |
| `heal_os_ledger.sh` | Close orphan open OS intervals when VM1 is **STOPPED** (prefer lease heartbeat) |

Auth: **`OCI_CLI_AUTH=instance_principal`** (door dynamic group + policies). No `~/.oci/config` as root.

Required `oci.env` keys (see `door_vm/oci/config.example.env`):  
`INSTANCE_ID`, `RESERVED_PUBLIC_IP_ID`, `VM1_PRIVATE_IP_ID`, `VM2_PRIVATE_IP_ID`, `VM1_PRIVATE_IP`, Object Storage namespace/bucket/cache dir, plus optional `WAIT_TIMEOUT_SEC`, `PATH` including `/home/ubuntu/bin` for `oci`.

`control.c` `run_script` sources `oci.env` through `tr -d '\r'` (WinSCP CRLF safety) and prefers `bash -- script` so CRLF shebangs do not break.

---

## 7. Stop / handback path

### Normal stop — `control_stop(exhausted, async)`

1. **`stop_vm1.sh`** (SOFTSTOP, or no-op if already STOPPED/STOPPING). Failure is logged but handback **continues**.
2. **`ip_to_vm2.sh --force`** — park reserved IP on door again. Failure → **`DEGRADED`**.
3. **`budget_record_stop`**, clear session/deadline, refresh budget.
4. Door → `DOOR_IDLE` or **`BUDGET_EXHAUSTED`** if `exhausted` (persisted **after** the IP move).

HTTP (async, same shape as wake — SoftStop + IP move takes longer than the Manager’s 10s default client):

- `POST /api/idle-empty` → `control_stop(0, 1)` → **`202`** immediately; work runs on a background thread (`stop_in_progress` in `/api/status`)
- `POST /api/budget-exhausted` → `control_stop(1, 1)` → **`202`**
- A second POST while stop is in flight is a no-op success. Wake is **rejected** (`409`) while `stop_in_progress`.

### Reconcile (polling safety net)

`mccontrol-reconcile.timer` → every ~**1 minute** → `reconcile_vm1.sh`:

1. `GET http://127.0.0.1:8080/api/status` → read door state.
2. If door is **PLAYABLE / STARTING / DEGRADED** (any naming variant) **and**
   VM1 lifecycle is **STOPPED** or **STOPPING** → `POST /api/idle-empty` (handback).
3. If door is **STARTING / DEGRADED** (not PLAYABLE), VM1 is **RUNNING**, wake is
   **not** in progress, and private `:25565` accepts TCP → **`promote_playable.sh`**
   (reserved IP to VM1 + PLAYABLE). Recovers a `wait_forge` timeout without the
   Troubleshooting unstick one-shot. Does **not** run while a wake thread is live
   (promote restarts mccontrol).
4. If door is already **DOOR_IDLE / BUDGET_EXHAUSTED / SPEND_BRAKE** and VM1 is **STOPPED** or
   **STOPPING** → still run **`ip_to_vm2.sh`** (idempotent) so a reset or a crash
   between persist-idle and IP move cannot leave the reserved address on a
   dead VM1.
5. If VM1 is **RUNNING/STARTING** → clear `ledger_heal_verified` latch; **skip** OS pull/heal
   (budget pull stays on the wake path only).
6. If VM1 is **STOPPED** and latch is unset → run **`heal_os_ledger.sh` once**, then
   set latch (retry next tick only if heal failed or local cache still shows an open interval).
   **STOPPING** skips heal so SoftStop publish is not raced.
7. If latch is set → skip further Object Storage I/O until VM1 is up again (`ip_to_vm1.sh` also clears latch).

This covers Console Force Stop, VM1 idle SoftStop, and **budget Function SoftStop of VM1**. The live `$1` Function also SoftStops **VM2** (`functions/shutdown_vm/`), so this timer **does not run** until the door instance is started again — reserved IP may stay wherever it was (FN-ISSUE-1).

### Manual unstick

`reset_door_state.sh`: stop mccontrol → force `door_state=DOOR_IDLE` in `state.json` → start mccontrol. Does **not** by itself move the reserved IP—operator may still need `ip_to_vm2.sh` if IP is stuck on VM1.

Other helpers: `diagnose_wait_forge.sh`, `unstick_after_forge_ready.sh` (operational recovery).

---

## 8. Minecraft doorbell (`mcdoor`)

When friends hit the reserved IP while it is on VM2:

- **Status / server-list ping** → MOTD text from `mcdoor_build_motd` (state + budget figures) + state favicon (idle / starting / exhausted PNGs base64).
- **Login** → disconnect with `mcdoor_build_kick_reason` (e.g. waking / try again / budget exhausted) and, when appropriate, invoke **`control_on_login_wake`** → async wake.

Defaults in headers: protocol **763**, version name **1.20.1** (presentation for the fake server list—not the real Forge version on VM1).

**MOTD** = Message Of The Day (Minecraft multiplayer server-list description).

**Product later (after v1, not live today):** Manager Advanced may start VM1 **without** moving the reserved IP (admin joins via VM1 **ephemeral** public IP). While that mode is on, the door should **not** wake on connect; MOTD/kick should say the server is under **maintenance** (distinct from idle-wake and budget-exhausted). See `PRODUCT-IDEAS.md` → Maintenance / reserved-IP control.

---

## 9. HTTP admin API (`httpmini`)

Bound on door **ephemeral** IP (and localhost), port **8080**. Security List should allow admin `/32` → 8080 only.

| Method | Path | Behavior |
|--------|------|----------|
| GET | `/api/status` | JSON snapshot (door, budget, errors, …) |
| POST | `/api/wake` | Async **admin** wake; skips daily exhaustion; spend-brake / soft cap still refuse. `202` or `409` |
| POST | `/api/idle-empty` | Async stop + IP to door → idle; `202` (`stop_in_progress` until done) |
| POST | `/api/budget-exhausted` | Async stop + exhausted state; `202` |
| POST | `/api/os-refresh` | Reload OS ledger/budget caches from disk (after pull/heal) |
| POST | `/api/session-sync` | Merge catch-up intervals JSON into **local** ledger (legacy) |
| POST | `/api/config/idle` | Set `idle_timeout_minutes` (persisted) |
| GET | `/`, static | Web UI under `web/static` |

Static files served from `web_root` in config.

---

## 10. Budget behavior (Object Storage vs legacy local)

### Object Storage mode (operator stack — preferred)

When `object_storage_enabled` is true in `config.json`:

- Wake pulls shared **`budget/config.json`** + **`ledger/usage.json`** via `pull_os_budget.sh`, and **always** GETs **`meta/spend-brake-triggered.json`** (presence refuses START; 404 = unlocked).
- Daily limit derived from **`monthly_ocpu_target / days-in-LA-month`** (also published as `daily_ocpu_limit_phase_a`).
- Player Minecraft login still refuses wake when today’s used ≥ daily limit. **Manager `POST /api/wake` skips that daily gate** (admin Start after exhaustion). Soft monthly cap and the spend-brake lock still refuse both.
- Also refuse wake when month-to-date OCPU-h ≥ **`soft_ocpu_cap`**.
- Door skips local `budget_record_start/stop` dual-writes; VM1 publishes intervals + lease heartbeats; Phase 4/5 heal may close orphans as `stop_uncertain` (STOPPED-only; prefer lease heartbeat).
- See `docs/Object-Storage-Phase3.md` and `docs/Object-Storage-Phase4.md`.

### Legacy local Phase A (Object Storage off)

VM2 maintains `/var/lib/mccontrol/ledger.json` and gates wake on a local daily OCPU cap (default **45** OCPU-h / LA day). Intervals and `hard_stop_deadline` behave as in the original prototype. Prefer enabling Object Storage for the live stack.

### Product intent

Follow **operator will** and the living V1 / bug-fix plans. Object Storage + VM1 ledger + lease + Manager budget config are authorities; door **reads** for wake and may heal orphans when STOPPED. Do not treat old `MinecraftServerDeploy` README “door owns daily budget” as final design. If a living plan disagrees with `PRODUCT-IDEAS.md`, follow the living plan and note drift.

---

## 11. Keepalive

`keepalive.c`: low-priority thread periodically burns a short CPU **burst** (defaults ~ every 7200s, burst ~750s from config) and records `last_keepalive_at`. Intent: reduce risk of Always Free Micro **reclaim** due to idleness. Preempted when “activity” refs are held. Optional via `keepalive_enabled`.

---

## 12. Install script highlights (`install.sh`)

Typical stages:

1. apt packages (`build-essential`, `iptables-persistent`, …)
2. Install OCI CLI to `~/bin` if missing (instance principal later)
3. `make mccontrol`
4. Copy binary, `oci/`, `web/`, `assets/` → `/opt/mccontrol`; normalize shell scripts to **LF**
5. Install `mccontrol.service`
6. Configure `/etc/mccontrol/oci.env` and `config.json` (prompts or `--yes` with existing/env)
7. iptables ACCEPT for 22 / 25565 / 8080 unless `--skip-firewall`
8. enable/start unit unless `--no-start`

Reconcile timer/units ship under `door_vm/systemd/` and are installed by `install.sh` / Testing2 Phase 4 deploy.

---

## 13. Networking checklist (door-related)

| Layer | Rule |
|-------|------|
| Security List | Friend `/32` → 25565 tcp/udp; admin `/32` → 22 + **8080**; **VCN CIDR → VM1 :25565 tcp** so `wait_forge` private poll works |
| VM1 firewalld | Allow VCN → 25565; friend rules as designed |
| VM2 iptables | ACCEPT 22 / 25565 / 8080 (Security List still filters sources) |
| Netplan | Secondary play IP on door (and similarly on VM1) |

---

## 14. Interaction with VM1 / desktop Manager

| Actor | Door awareness |
|-------|----------------|
| Door wake / idle-empty | Full path: power + budget + IP move |
| Desktop Force Start VM1 | Starts compute **without** moving reserved IP — friends still hit door until a proper wake |
| Desktop / Console SoftStop | IP may linger on VM1 until **reconcile** (~1 min) runs idle-empty |
| Legacy `/opt/mc-manager` idle SoftStop | Same—reconcile repairs IP/state |
| Next-gen VM1 agent (design) | Should `POST` door idle-empty first; fallback local SoftStop + IP scripts |

Prefer exercising **door Wake / idle-empty** when testing the doorbell path.

---

## 15. How to study the code (reading order)

1. This doc + `Infrastructure-Information.md` § door / reserved IP + `VM-Software.md`  
2. Sibling `OCI-mc-server/door_vm/include/state.h`, `control.h`, `budget.h`, `mcdoor.h`  
3. Sibling `OCI-mc-server/door_vm/src/control.c` (`do_wake`, `control_stop`, OS pull)  
4. Sibling `OCI-mc-server/door_vm/oci/*.sh` (esp. `pull_os_budget.sh`, `heal_os_ledger.sh`)  
5. Sibling `OCI-mc-server/door_vm/src/main.c`, `httpmini.c` (API), `mcdoor.c` (MOTD/kick)  
6. Sibling `OCI-mc-server/door_vm/scripts/reconcile_vm1.sh`, `door_vm/systemd/`  
7. Sibling `OCI-mc-server/door_vm/install.sh`, `config.example.json`  
8. `Issues.md` for known quirks

Private scratch only: `development/vm2-door/` (gitignored).

---

## 16. Agent / operator pitfalls

**Deploy/SSH coding mistakes (agents):** see [`Agent-Deploy-Pitfalls.md`](Agent-Deploy-Pitfalls.md) before changing `door_deploy` / `ssh_ops`.

- **systemd + `set -u`:** door OCI scripts must default `HOME` (`HOME="${HOME:-/home/ubuntu}"`) or oneshots abort. Optional vars (`POLL_INTERVAL_SEC`) must get `:-default` **before** CR-strip.  
- **WinSCP CRLF** breaks shell scripts; install strips CR under `/opt/mccontrol/oci`—still prefer LF uploads.  
- Confusing **ephemeral** vs **reserved** IP: players use reserved; SSH/UI use ephemeral. Guest netplan must have the **secondary** play address.  
- `wait_forge` uses **primary private** IP, IP move uses **secondary private IP OCIDs**.  
- `oci.env` is **root-only** (600). `Permission denied` as `ubuntu` is expected. Live `state.json` / `ledger.json` contain tenancy data—keep out of git.  
- IAM: `UpdatePublicIp` needs **tenancy** `manage public-ips` / `use private-ips` / `use virtual-network-family`; door DG must match **instance.id** (tag `mcmgr-role` did not enroll on the identity-domain 3.3 test). See Issues **SETUP-ISSUE-2** / **DOOR-ISSUE-4**.  
- `DEGRADED` after partial wake: Forge may be up with IP still on door (or opposite)—use diagnose/unstick/reset + verify `oci network public-ip get`.  
- Do not run conflicting stop owners without a single authority (idle agent SoftStop + door reconcile is the supported pattern).  
- Reconcile journal should stay quiet while VM1 is RUNNING (`skip OS pull/heal`); noisy minute-by-minute OS pulls means an old reconcile script.

---

## Changelog

| Date | Note |
|------|------|
| 2026-08-17 | DOOR-ISSUE-8 / E2E F9: `start_vm1.sh` waits for RUNNING; `wait_forge` 5s TCP probe cap; reconcile `promote_playable` after wait_forge timeout. |
| 2026-08-14 | 3.3 test: `wait_forge` `set -u` defaults; `ip_to_vm1` `--force`; tenancy IP IAM + door DG by instance.id; `oci.env` root-only. |
| 2026-08-07 | Initial comprehensive door doc from `development/vm2-door` source + installed pulls. |
| 2026-08-08 | Authority banner: PRODUCT-IDEAS wins over MinecraftServerDeploy README; budget section split prototype vs product intent; door stays C-first. |
| 2026-08-08 | **`door_vm/`** becomes tracked SoT; OS wake/pull/heal + reconcile always-heal; `/api/os-refresh`; Issues/VM-Software links. |
| 2026-08-09 | `HOME` default for systemd; reconcile **low-chatter** (no routine pull; `ledger_heal_verified` one-shot heal); `ip_to_vm1` clears latch. |
| 2026-08-10 | Phase 5: heal **STOPPED-only** + lease heartbeat close; see `docs/Object-Storage-Phase5.md`. |
