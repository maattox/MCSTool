# OCI Minecraft Server — Infrastructure Information

Generalized reference for the cloud and host setup behind a private, Always Free–oriented Minecraft deployment on Oracle Cloud Infrastructure (OCI).

**Audience:** operators and coding agents.  
**Cost rule:** keep spend at **$0** unless a paid change is explicitly accepted.

Deployment-specific OCIDs, IPs, usernames, and secrets live in a **gitignored** private file (see [Private deployment details](#private-deployment-details)). Never put those values in this document.

---

## Table of contents

1. [Purpose](#purpose)
2. [Design principles](#design-principles)
3. [Architecture overview](#architecture-overview)
4. [OCI account and cost model](#oci-account-and-cost-model)
5. [Networking (VCN)](#networking-vcn)
6. [Compute instances](#compute-instances)
7. [Reserved play IP and secondaries](#reserved-play-ip-and-secondaries)
8. [Door control plane (VM2 / mccontrol)](#door-control-plane-vm2--mccontrol)
9. [Host OS services](#host-os-services)
10. [IP allowlisting (dual firewall)](#ip-allowlisting-dual-firewall)
11. [Minecraft server (VM1)](#minecraft-server-vm1)
12. [OCI MC Server Manager (desktop tools)](#oci-mc-server-manager-desktop-tools)
13. [Idle agent and usage tracking](#idle-agent-and-usage-tracking)
14. [Stop / idle handback and reconcile](#stop--idle-handback-and-reconcile)
15. [Budget emergency stop (Functions)](#budget-emergency-stop-functions)
16. [Shared Object Storage](#shared-object-storage)
17. [Identity and access (IAM)](#identity-and-access-iam)
18. [Operational lifecycle](#operational-lifecycle)
19. [What this stack deliberately excludes](#what-this-stack-deliberately-excludes)
20. [Placeholder index](#placeholder-index)
21. [Private deployment details](#private-deployment-details)
22. [Maintaining this documentation](#maintaining-this-documentation)
23. [Related repo paths](#related-repo-paths)
24. [Known issues](#known-issues)

---

## Purpose

Run a **modded Minecraft server for a small friend group** on OCI while staying inside **Always Free–eligible** Ampere A1 usage as much as possible.

The account may be **Pay As You Go (PAYG)** so Ampere capacity can be obtained. PAYG does **not** mean spending is acceptable. Target monthly usage stays under the free Ampere envelope (commonly cited as **1500 OCPU-hours** and **9000 memory-hours**). Operational soft targets used by tooling are typically **1400 OCPU-h** and **8800 GB-h**, with slightly lower soft caps for auto-stop.

Day-to-day management uses the **Blazor Hybrid Manager** (`McManager.Hybrid`) in this repo, plus **SSH** (PuTTY / WinSCP) for break-glass. Friends use a **stable reserved public IP** (doorbell); admins use each VM’s **ephemeral** public IP for SSH and the door admin UI.

The cloud resources below are the source of truth; the desktop app is not required for the server to run.

---

## Design principles

| Principle | Meaning |
|-----------|---------|
| **$0 first** | Prefer Always Free–eligible shapes and networking. No paid LBs, WAF, extra block volumes, or surprise egress patterns unless approved. |
| **Private access** | No public `0.0.0.0/0` for Minecraft (or SSH at the OCI edge). |
| **Defense in depth** | OCI Security List **and** host firewall (`firewalld` on VM1; `iptables` on the door Micro). |
| **SSH break-glass** | SSH is restricted in the **Security List** by admin `/32`s. On VM1, `firewalld` keeps the `ssh` service open so an admin IP change does not lock operators out of fixing host rules over SSH (OCI still blocks strangers). |
| **Stop when idle** | Forge VM (VM1) is often **STOPPED** when nobody is playing to conserve free hours. |
| **Doorbell Micro (VM2)** | Always-on Always Free **Micro** holds the reserved play IP when idle, answers MOTD / wake, **reads** shared Object Storage budget/ledger for the wake gate, heals orphan open intervals when VM1 is **STOPPED** (lease-aware), and moves the reserved IP to/from VM1. |
| **Ephemeral vs reserved** | Each VM keeps an **ephemeral** public IP for SSH/admin. Friends always connect to the **reserved** play IP. |
| **Secondary private IPs** | Reserved public IP attaches only to a **secondary** private IP on each VNIC so the primary+ephemeral path is never displaced. |

---

## Architecture overview

```text
Friend PC                         Admin PC
    │                                  │
    │ Minecraft → reserved play IP     ├─ SSH → VM1 ephemeral
    │                                  ├─ SSH → door ephemeral
    │                                  └─ HTTP :8080 → door ephemeral (mccontrol UI)
    ▼
OCI Security List (ingress allowlist)
    │
    ├─ reserved public IP ──► secondary private IP on door (idle) OR VM1 (play)
    ├─ VM1 ephemeral ───────► VM1 primary private IP (SSH / outbound)
    └─ door ephemeral ──────► door primary private IP (SSH / admin UI)

Idle path:
  door (mccontrol) answers MOTD on reserved IP; Wake → START VM1 → wait Forge
  → move reserved IP to VM1 secondary → PLAYABLE

Play path:
  friends hit reserved IP → VM1 Forge :25565

Stop / idle handback:
  POST /api/idle-empty (or reconciler if VM1 stopped externally)
  → SOFTSTOP VM1 → move reserved IP back to door secondary → DOOR_IDLE
```

Logical OCI layout:

```text
[OCI Tenancy / <HOME_REGION>]
  └─ Compartment <COMPARTMENT_OCID>
       ├─ IAM (users, API keys, dynamic groups, policies)
       ├─ Object Storage bucket <OBJECT_STORAGE_BUCKET> (shared ledger / meta / backups)
       ├─ Budgets + Events + Functions (optional emergency SoftStop)
       └─ VCN <VCN_OCID>
            ├─ Internet Gateway + route table
            ├─ Public subnet <SUBNET_OCID>
            │    ├─ VM1 A1 Flex (Forge) — primary + secondary private IPs
            │    │    ephemeral public on primary; reserved play IP when playing
            │    ├─ VM2 Always Free Micro (door) — primary + secondary private IPs
            │    │    ephemeral public on primary; reserved play IP when idle
            │    └─ Security List <SECURITY_LIST_OCID>
            └─ Reserved public IP <RESERVED_PUBLIC_IP> (play address for friends)
```

Naming used in ops / repo:

| Name | Role |
|------|------|
| **VM1** / `minecraft-vm3` | Forge primary (A1 Flex 4/24) |
| **VM2** / door / `minecraft-vm-door` | Always Free Micro control plane (`mccontrol`) |

---

## OCI account and cost model

| Topic | Guidance |
|-------|----------|
| **Region** | Home / deployment region, e.g. `<HOME_REGION>` (placeholder; see private file). |
| **Always Free Ampere** | Shape `VM.Standard.A1.Flex`. Common free monthly envelope: **1500 OCPU-h** and **9000 GB-h** (confirm against current OCI docs for your tenancy). |
| **Always Free Micro** | Door VM2 — keep always running for MOTD / wake / IP parking. Subject to Always Free Micro reclaim rules (keepalive may be added later). |
| **Operational targets** | Soft targets **1400 / 8800**; idle-agent soft caps often **1375 / 8600** so SoftStop happens before hard free limits. |
| **Door daily budget** | With Object Storage Phase 3 enabled, door wake gate **reads** shared `budget/config.json` + ledger: daily limit = `monthly_ocpu_target / days-in-LA-month` (published also as `daily_ocpu_limit_phase_a`), plus refuse when month-to-date ≥ `soft_ocpu_cap`. Also GETs `meta/spend-brake-triggered.json` and **never START VM1** while that object exists. Legacy offline door default remains ~45 OCPU-h/day. |
| **PAYG** | May be required for A1 capacity; still treat every chargeable resource as out of scope unless approved. |
| **Break-glass spend control** | Optional **$1 compartment budget** → Events → Function SoftStop (see [Budget emergency stop](#budget-emergency-stop-functions)). |

One **reserved public IP** for the play address is intentional (doorbell). One **private Object Storage** bucket for shared control-plane data (ledger, meta, backups) is intentional—stay within Always Free Object Storage caps (see [Shared Object Storage](#shared-object-storage)). Avoid casually adding: paid shapes, extra block volumes, load balancers, WAF, or extra always-on paid instances.

---

## Networking (VCN)

Typical layout (names/CIDRs are deployment-specific):

| Resource | Role |
|----------|------|
| **VCN** `<VCN_OCID>` | Network for both instances |
| **Public subnet** `<SUBNET_OCID>` | Instance VNICs; route `0.0.0.0/0` → Internet Gateway |
| **Internet Gateway** | Public ingress/egress |
| **Security List** `<SECURITY_LIST_OCID>` | Cloud-edge ingress allowlist (primary filter) |
| **Ephemeral public IPs** | Per-VM SSH / door admin UI |
| **Reserved public IP** | Stable play address friends use |

The live lab VCN was created with the Console wizard, so it also has an **unused** private subnet (`10.0.1.0/24`), NAT gateway, and service gateway. Both VMs sit on the public subnet. Product OpenTofu must **not** recreate those leftovers (see [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md)). IPv6 on the public subnet is likewise unused for the product.

### Security List ingress (intent)

| Traffic | Source | Dest | Notes |
|---------|--------|------|-------|
| Minecraft | Friend `/32` **or CIDR prefix** (`/9`–`/32`; `/0`–`/8` rejected) | **25565/tcp** and **25565/udp** | Description often = player name (for tooling) |
| SSH | Each admin `/32` (CIDR only if editing **own** admin entry) | **22/tcp** | Description often = `{name} SSH access` |
| Door admin UI | Each admin `/32` (same own-admin CIDR rule as SSH) | **8080/tcp** | `mccontrol` HTTP UI / API |
| VCN → Forge | `10.0.0.0/24` | **25565/tcp** | Door `wait_forge` probes VM1 private IP |
| ICMP / other | As needed | — | Preserve non-owned rules when updating the list |

**Do not** open Minecraft, SSH, RCON, or the door UI to `0.0.0.0/0`.

**Egress:** often default allow-all; any lockdown is deployment-specific.

> **Agent note:** OCI `UpdateSecurityList` replaces the **entire** ingress set. Always preserve ICMP and rules not owned by the whitelist tooling.

---

## Compute instances

### VM1 — Forge primary

| Item | Typical value |
|------|----------------|
| Shape | `VM.Standard.A1.Flex` (Ampere ARM) |
| Size | **4 OCPU / 24 GB** RAM |
| OS | Canonical **Ubuntu 22.04 aarch64** |
| SSH user | `ubuntu` |
| Instance OCID | `<VM1_INSTANCE_OCID>` |
| Lifecycle | **STOPPED** when idle; **RUNNING** when playing |
| Boot | Minecraft unit should be `systemctl enable`d so it starts after Force Start / door wake |
| Host firewall | **firewalld** (allow VCN + friend `/32`s to 25565; keep SSH service open on host) |

### VM2 — Door Micro

| Item | Typical value |
|------|----------------|
| Shape | Always Free **Micro** (E2.1.Micro or current free Micro) |
| OS | Ubuntu (door image) |
| SSH user | `ubuntu` |
| Instance OCID | `<VM2_INSTANCE_OCID>` |
| Lifecycle | **Always RUNNING** (control plane) |
| Host firewall | **iptables** (install script opens 22 / 25565 / 8080); not firewalld |

Management: Console, OCI CLI/API, desktop tool, or door UI wake. SoftStop is preferred for graceful shutdown paths on VM1.

---

## Reserved play IP and secondaries

OCI will not assign a second public IP to a private IP that already has an ephemeral. Therefore:

| Address role | VM1 | VM2 (door) |
|--------------|-----|------------|
| Primary private + **ephemeral** public | SSH / outbound (mods, Mojang) | SSH / admin UI `:8080` |
| Secondary private (**play**) | Target for reserved IP while **PLAYABLE** | Target for reserved IP while **IDLE** |
| Reserved public IP | Attached to VM1 secondary when playing | Attached to door secondary when idle |

Guest OS must configure the secondary address (netplan). **Setup bootstrap** writes `/etc/netplan/99-mcmgr-play.yaml` (mode `600`) on both VMs. Manual rebuilds can use `99-vm1-play.yaml` / `99-door-play.yaml` instead.

`wait_forge` always probes **VM1 primary private IPv4** (`VM1_PRIVATE_IP`), not the secondary.  
`ip_to_vm1` / `ip_to_vm2` use the **private IP OCIDs** of the secondaries (`VM1_PRIVATE_IP_ID` / `VM2_PRIVATE_IP_ID`) and `--force` when moving the reserved IP.

---

## Door control plane (VM2 / mccontrol)

**Tracked source tree:** [`../door_vm/`](../door_vm/README.md) — full redeploy SoT for door software.  
**Deep dive:** [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md).  
**VM overview:** [`VM-Software.md`](VM-Software.md).

**Authority:** Product intent lives in [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md). Door control plane is **C**-based (`mccontrol`) for Micro performance (~1/8 OCPU)—prefer keeping heavy Python off VM2. Object Storage I/O uses OCI CLI shell scripts (`pull_os_budget.sh`, `heal_os_ledger.sh`).

### On-box layout

| Path | Purpose |
|------|---------|
| `/opt/mccontrol/build/mccontrol` | Control-plane binary |
| `/opt/mccontrol/oci/*.sh` | OCI wrappers + `pull_os_budget.sh` + `heal_os_ledger.sh` |
| `/opt/mccontrol/scripts/` | `reconcile_vm1.sh`, reset/diagnose helpers |
| `/opt/mccontrol/web/`, `assets/` | Admin UI + MOTD icons |
| `/etc/mccontrol/config.json` | Ports, paths, `object_storage_enabled`, cache paths |
| `/etc/mccontrol/oci.env` | OCIDs + OS namespace/bucket + `OCI_CLI_AUTH=instance_principal` (**mode 600**) |
| `/var/lib/mccontrol/` | Door state; `os-cache/` for pulled ledger/budget |

### Systemd

| Unit | Role |
|------|------|
| `mccontrol.service` | Door daemon (MOTD :25565 when IP local, HTTP :8080) |
| `mccontrol-reconcile.timer` | Every ~1 min: external-stop handback; OS ledger heal **once** per down episode when VM1 is **STOPPED** (`ledger_heal_verified`; lease-aware close); no routine budget pull |

### Auth for OCI from the door

- Unit `EnvironmentFile=-/etc/mccontrol/oci.env`
- Wrappers / reconcile use **`OCI_CLI_AUTH=instance_principal`** (no `~/.oci/config` as root)
- Dynamic group + policies for the **door** instance (start/stop VM1, move reserved / private IPs)

### Required `oci.env` keys

| Key | Meaning |
|-----|---------|
| `INSTANCE_ID` | VM1 compute OCID |
| `RESERVED_PUBLIC_IP_ID` | Reserved public IP **OCID** (`ocid1.publicip...`), not the dotted address |
| `VM1_PRIVATE_IP_ID` | VM1 **secondary** (play) private IP OCID |
| `VM2_PRIVATE_IP_ID` | Door **secondary** (play) private IP OCID |
| `VM1_PRIVATE_IP` | VM1 primary private IPv4 for `wait_forge` |
| `OCI_CLI_AUTH` | `instance_principal` |
| `PATH` | Include `/home/ubuntu/bin` for Oracle `oci` CLI |
| `WAIT_TIMEOUT_SEC` | Optional; default 600 |

### Door states (conceptual)

| State | Meaning |
|-------|---------|
| `DOOR_IDLE` | Reserved IP on door; MOTD / wake |
| `STARTING` | Wake in flight |
| `PLAYABLE` | Reserved IP on VM1; friends play Forge |
| `BUDGET_EXHAUSTED` | Daily OCPU cap hit |
| `SPEND_BRAKE` | `$1` monthly lock present; door will not START VM1 |
| `DEGRADED` | Lost track (e.g. IP move failed); manual fix / reset |

### Operator APIs (on door ephemeral `:8080`)

| Method | Path | Role |
|--------|------|------|
| GET | `/api/status` | Door + budget snapshot |
| POST | `/api/wake` | Start VM1 → wait Forge → `ip_to_vm1` |
| POST | `/api/idle-empty` | SoftStop VM1 → ledger stop → `ip_to_vm2` → idle |
| POST | `/api/budget-exhausted` | Same stop path with exhausted state |

WinSCP tip: shell scripts must be **LF**. Install strips CR under `/opt`; still prefer LF uploads.

---

## Host OS services

### VM1

| Service | Role |
|---------|------|
| **sshd** | Admin SSH (key-based) |
| **firewalld** | Host firewall; zone often `public` |
| **minecraft** (systemd) | Game process |
| **mc-idle-watch.timer** | Legacy idle / budget watchdog (desktop tool) — see coexistence note below |
| **mc-boot-ledger.service** | Boot: force-pull OS ledger + lease → reconcile → detect live shape → open interval + lease → publish |
| **Time sync** | UTC |

Ensure `firewalld` is **enabled** on VM1. Product SoT is **firewalld-only**: mask Oracle `netfilter-persistent` (SETUP-ISSUE-7) and Canonical **UFW**. Override `/etc/systemd/system/firewalld.service` (no `network-pre`) so boot does not delete dbus (OS-ISSUE-9 ACPI STOPPING hang).

Also ensure firewalld allows **VCN** (`10.0.0.0/24`) to **25565/tcp** so the door can probe Forge before moving the public IP.

### VM2 (door)

| Service | Role |
|---------|------|
| **sshd** | Admin SSH |
| **mccontrol** | Doorbell + budget + IP moves |
| **mccontrol-reconcile.timer** | External-stop handback |
| **iptables** / netfilter-persistent | Host ACCEPT for 22 / 25565 / 8080 |

---

## IP allowlisting (dual firewall)

### Layer 1 — OCI Security List

First filter at the VCN edge. Friend `/32`s or CIDR prefixes → Minecraft; admin `/32`s → SSH + door `:8080` (own-admin CIDR may widen those); VCN → VM1 `:25565` for door probes.

### Layer 2 — host

- **VM1 firewalld:** per-IP rich rules for 25565; SSH service open on host; VCN → 25565 for door.  
- **Door iptables:** install script inserts ACCEPT for 22 / 25565 / 8080 (Security List still gates strangers).

Original wide-open `25565` to the world (from some setup guides) must be removed.

---

## Minecraft server (VM1)

**How the game itself gets installed/upgraded (Vanilla today; Paper/Fabric/NeoForge/Forge/Quilt/modpacks researched for v1+):** [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md). This section stays a snapshot of the current live values; the blueprint is authoritative for mechanism (game manifest schema, directory/systemd/RCON design, per-platform artifact acquisition, upgrade/rollback).

| Item | Value |
|------|--------|
| Game port | **25565** TCP **and** UDP (mods may use UDP) |
| RCON | **25575**, bind **127.0.0.1** only — never open on Security List or firewalld |
| Process | systemd unit `minecraft` |
| JVM tip | Prefer IPv4 for mod update checks: `-Djava.net.preferIPv4Stack=true` |
| Label / DNS | None required; friends use reserved play IP |
| World folder (current) | `/home/ubuntu/minecraft/server/world` — idle-agent SoftStop backups use config `world_path` (may change under Setup / Vanilla vs modded) |

World files and Java/modpack paths are deployment-specific (see private file). SoftStop world backups: see [World backups (MVP)](#world-backups-mvp).

---

## OCI MC Server Manager (desktop)

The product UI is **`McManager.Hybrid`** in this repo (WPF + BlazorWebView WinExe). It does not replace OCI, systemd, or the door.

### What it talks to

| Channel | Credential | Used for |
|---------|------------|----------|
| **OCI API** | `~/.oci/config` + API key PEM | Security List; instance start/stop; Object Storage — follow [`OCI-API-Usage.md`](OCI-API-Usage.md) (429 backoff, waiters, pagination; Always Free request thrift) |
| **SSH (VM1)** | OpenSSH private key | firewalld; idle agent deploy; ledger helpers; Minecraft bootstrap |
| **SSH (door)** | Same key | Door deploy, pull/heal, journals |

Runtime config (gitignored): `data/config.local.json`, `data/friends.local.json`. See [`Local-Config.md`](Local-Config.md).

### Capabilities (conceptual)

1. **Desired List** of friends → sync to Security List (Minecraft 25565; product lean is Security List–only)  
2. **Start / Stop / Restart** via the door play-IP path (prefer **Wake**; Force Stop uses graceful stop + **cold world backup** when the agent is installed)  
3. **Deploy / update** the on-VM idle agent (`vm_agent/`) and push/pull agent config (incl. `world_path`, backup soft cap)  
4. **Usage / budget** via Object Storage  
5. **Troubleshooting** one-shots (park play IP, heal ledger, repair permissions, …)

**Important:** Console SoftStop does **not** notify the door. The door reconciler detects STOPPED and runs handback (and ledger heal) within about a minute. Prefer door **Wake** / **idle-empty** when exercising the doorbell path. Console SoftStop alone does **not** run a world backup.

### Whitelist rule ownership (for sync tools)

| Surface | How a rule is identified |
|---------|---------------------------|
| OCI Minecraft | Description = player **name** |
| OCI SSH | Description = `{name} SSH access` |
| firewalld | No reliable comments; map IP → name in the UI |

---

## Idle agent and usage tracking

### Production on VM1 (`vm_agent/`)

Tracked tree: [`../vm_agent/README.md`](../vm_agent/README.md). Installed under `/opt/mc-manager`. SoftStops an **empty**, **not-running**, or over-budget server via **instance principal**. Publishes/pulls the shared Object Storage ledger + session **lease** when configured. Does **not** move the reserved IP by itself — the **door reconciler** covers IP/state (and orphan heal) after an external stop.

**Usage metering (Phases 2 + 5):** intervals in `ledger/usage.json` always carry per-row **`ocpus`** and **`memory_gb`** (totals = hours × those fields). While VM1 is up, `idle_watch` refreshes `ledger/lease.json` about every 5 minutes (`last_heartbeat_at`) so a failed SoftStop publish cannot leave usage open forever. On Minecraft boot, the agent force-pulls OS ledger + lease, reconciles against `journalctl --list-boots`, repairs uncertain stops (never later than the prior estimate), **detects live shape from `/proc`**, opens a new interval + lease, and syncs `shape_ocpus` / `shape_memory_gb` into local agent config and Object Storage `budget/config.json` when they differ. Config shape values are fallback only if `/proc` probes fail. A rare mid-session shape change closes the open interval and opens a new one.

**Idle force-enable on boot (product safety):** every Minecraft boot (`mc-boot-ledger.service` → `record_boot.py`) **enables and starts** `mc-idle-watch.timer`, rewrites local `/etc/mc-manager/config.json` `idle_agent_enabled=true` if it was false, and patches Object Storage `budget/config.json` the same way. Disabling the idle agent (Manager Danger Zone / testing) is temporary only and **does not survive** a Minecraft restart. See also world backup on SoftStop under [World backups (MVP)](#world-backups-mvp).

**SoftStop backup:** idle SoftStop and Manager `graceful_stop.sh` stop Minecraft, then run a **cold** world zip → Object Storage upload (9.5 GiB soft cap) before OCI SoftStop / return. Live (`save-off` / `flush` / `save-on`) backup is implemented for scheduled/CLI use while players are online.

Optional future path: notify door `POST /api/idle-empty` before SoftStop. Until then: idle agent SoftStop + door reconcile is the supported pattern (**do not** run a second conflicting stop agent as a peer).

### OCI / IAM for VM1 idle SoftStop

1. Dynamic group matching `<VM1_INSTANCE_OCID>`  
2. Policy: use/read instance-family for SoftStop  
3. Instance principal — no user API keys on the VM  

### On-box layout

| Path | Purpose |
|------|---------|
| `/opt/mc-manager/` | Agent code, venv (`oci` SDK), scripts |
| `/etc/mc-manager/config.json` | Instance OCID, RCON, shape, budgets, idle timeout, OS + lease, **`world_path`**, backup soft cap |
| `/var/lib/mc-manager/usage.json` | Usage ledger (intervals + `revision`) |
| `/var/lib/mc-manager/lease.json` | Active-session lease / heartbeat |
| `/var/lib/mc-manager/idle_state.json` | Idle / budget-warn state |
| `/var/tmp/mc-manager-backup/` | Temporary world zip workspace (not retained) |
| `mc-boot-ledger.service` | Boot: force-enable idle → OS pull + lease → reconcile → detect shape → open interval → publish |
| `mc-idle-watch.timer` | Idle/budget SoftStop + lease heartbeat (~1 min tick; heartbeat ~5 min) |

### Watchdog behavior (`idle_watch.py`)

Each tick, if `idle_agent_enabled`: SoftStop after the idle timeout when Minecraft is **empty** (RCON `list`) **or** the unit is **not** `active`. Do not no-op merely because the game is down. Soft monthly caps still apply while VM1 is up. When the unit is `active`: idle via RCON, SoftStop path (save flush → stop unit → **cold world backup** → ledger/lease publish → OCI SoftStop), lease heartbeat, mid-session reshape check. When already down: skip RCON/`systemctl stop`, still cold-backup if the world exists. RCON stays on localhost.

Door wake gate (OS mode) uses shared monthly/soft budget SoT; offline/local door still has a Phase A ~45 OCPU-h/day default.

---

## Stop / idle handback and reconcile

| Trigger | What happens |
|---------|----------------|
| Door `POST /api/idle-empty` | `stop_vm1.sh` (SOFTSTOP) → record session end → `ip_to_vm2.sh --force` → `DOOR_IDLE` (**no** world backup on this path yet — see OS-ISSUE-6) |
| Idle agent SoftStop | If the game is up: RCON flush → `systemctl stop minecraft`. If already down (Step 4.1): skip RCON/stop. Then **cold world backup** if the world exists → close ledger/lease + OS publish → OCI SoftStop; reserved IP may remain on VM1 until reconcile |
| Desktop Force Stop (graceful) | `graceful_stop.sh` (flush → stop → **cold backup**) then OCI SoftStop; door reconcile for IP |
| Console SoftStop alone | No world backup; door reconcile for IP / heal |
| `mccontrol-reconcile.timer` | If door is PLAYABLE/STARTING/DEGRADED **and** VM1 is STOPPED/STOPPING → POST idle-empty; if already DOOR_IDLE/BUDGET_EXHAUSTED → still `ip_to_vm2` (idempotent). **heal** OS ledger at most once per down episode **only when VM1 is STOPPED** (skip STOPPING / RUNNING / after `ledger_heal_verified`); close opens at lease heartbeat when present |

Manual sticky-state reset: `reset_door_state.sh` (stops mccontrol, sets `DOOR_IDLE`, restarts).

---

## Budget emergency stop (Functions)

Belt-and-suspenders if agents fail or usage is exceeded enough to incur charge:

```text
Budget ($1, compartment, monthly)
  → Alert (actual spend threshold) + email recipients
    → Events rule AutoShutdownOnBudgetAlert
      (event: com.oraclecloud.budgets.createtriggeredalert)
      → Functions action → BudgetControlApp / shutdown_vm
```

Resource Manager discovery omitted the Events **action** (`#action = <<Optional value not found in discovery>>`). The Console Actions tab and the private notes both show Functions → `shutdown_vm`. That is the live path.

An ONS topic `Budget-Alerts` also exists with a Function subscription to `shutdown_vm`, but the budget is **not** linked to it (email only). It is an abandoned leftover; product OpenTofu should not create it. Optional: delete the topic in Console.

Same components as before (budget, Events, Fn app, OCIR image, Functions dynamic group). **Live deployed lab image (0.0.11)** still SoftStops **both VM1 and the door Micro** and does not write the lock. **Tracked product source (0.0.12, V1 Step 2.2)** SoftStops **VM1 only**, **PUTs** `meta/spend-brake-triggered.json`, and **leaves the door running**. **Product path for users (before official release):** pre-built ARM tarball copied into their OCIR — **users** do not need Docker Desktop / Cloud Shell (V1 Step **8.6.1**). The developer may produce that tar with Docker Desktop. Lab Cloud Shell / TESTING `fn push` stay operator/agent fill-in. Do not `fn push` the live Forge lab until the operator authorizes it (prefer after Step 2.3 so the door will refuse wake). The Function does **not** move the reserved IP. If a still-deployed stop-both image takes the door down, `mccontrol-reconcile` cannot run until VM2 is started again — reserved IP may be left on whichever VNIC held it (see `Issues.md` FN-ISSUE-1).

### Function behavior (live lab image vs tracked source)

Source of truth: [`../functions/shutdown_vm/func.py`](../functions/shutdown_vm/func.py) (placeholders; live OCIDs only in Function config / gitignored local config / a deployed image).

**Tracked product v1 (this tree, not live-pushed in Step 2.2):**

- Ignore budget **RESET** alerts (no SoftStop, no lock PUT, no lock DELETE).  
- On real threshold alerts (or unparseable events): SoftStop **VM1 only** via resource principals. Already STOPPED/STOPPING is treated as success.  
- **PUT** `meta/spend-brake-triggered.json` (`source=budget_function`). Manager is the only clearer.  
- **Do not SoftStop the door Micro.** Oracle Always Free AMD `VM.Standard.E2.1.Micro` is a separate allowance (up to two instances), not Ampere A1 OCPU-hours; PAYG upgrade still does not charge Always Free resources. Leaving the door up keeps MOTD / reconcile / IP parking.  
- No graceful Minecraft stop, no world backup (OS-ISSUE-6).

**Live lab deployed image (0.0.11, until an authorized `fn push`):** SoftStop **VM1 and VM2**; no Object Storage lock.

**Product Manager (v1 overlay — Step 2.4):** full-window warning and typed confirmation before Start. Door honor of the lock is Step **2.3**.

### Usage API 48h ledger reconcile (Functions, not deployed)

Tracked product source: [`../functions/reconcile_usage/`](../functions/reconcile_usage/README.md) (V1 Step **7.7**). A **second** Function (does not modify `shutdown_vm`) may, for UTC ledger days older than ~48 hours, read OCI Usage API Ampere A1 OCPU-h / GB-h, write `ledger/usage.json` `daily_overrides`, bump `revision`, and dirty ledger consumers. Placeholders only. **Not live.** Do not `fn push` / OCIR / scheduled invoke unless the operator authorizes it. Do not run Usage API against the live Forge lab from an agent session.

---

## Shared Object Storage

Central bucket for **shared control-plane data** (not game world serving). Product intent (`PRODUCT-IDEAS.md`): ledger, budget config, infra meta, world **backups**, dirty/version helpers. VMs access via **instance principal**; admin PC via API key. No bucket Private Endpoint required for the current design (use regional Object Storage + Service Gateway / normal routing).

| Item | Typical value |
|------|----------------|
| Bucket name | `<OBJECT_STORAGE_BUCKET>` (e.g. shared-data style name) |
| Namespace | `<OCIR_NAMESPACE>` / Object Storage namespace (same tenancy namespace string) |
| Visibility | **Private** |
| Default tier | **Standard** (backups + JSON); do not rely on Infrequent Access / Archive for rotating backups |
| Versioning | Off for initial deploy (optional later; consumes free space) |
| Emit object events | Off (polling-first) |
| Multipart upload cleanup | On (delete stale incomplete multipart uploads) |
| Encryption | Oracle-managed keys |
| Resource logging | Off unless debugging |
| Private Endpoint | Not used |

**Always Free (paid/PAYG tenancy) reminder:** stay under Standard capacity (commonly **10 GB** Standard + **~50,000 Object Storage API requests/month**—confirm current OCI docs). Soft-cap **total bucket** usage ~**9.5 GiB**; delete oldest `backups/*.zip` before upload when near limit. Keep Get/List/Put chatter modest (Manager + door + VM1); see [`OCI-API-Usage.md`](OCI-API-Usage.md) for 429 backoff and polling intervals.

Manager (Phase 1) + VM1 publish (Phase 2): VM1 uploads `ledger/usage.json` on boot/stop (idle path retries publish after local close) and dirties manager/door flags. See [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md).

**Object prefixes in use:** `meta/` (incl. `flags.json`, `spend-brake-triggered.json`), `ledger/usage.json`, `ledger/lease.json`, `budget/config.json`, `ip/`, `messages/`, `backups/world-*.zip` (world archives).

### World backups (MVP)

Before SoftStop (idle/budget path) and after Manager **graceful Minecraft stop**, VM1 zips the configured world folder and uploads `backups/world-<UTC>.zip` using **cold** mode (Minecraft already stopped).

The same module supports **live** backups while players are online (for future scheduled backups / CLI):

1. RCON `save-off`  
2. RCON `save-all flush` (long timeout; brief settle)  
3. Zip world to a **temporary** local file under `backup_work_dir`  
4. RCON `save-on` (always, via `try`/`finally` — even if zip fails)  
5. Evict oldest Object Storage `backups/*.zip` if needed → multipart upload → **delete local zip**

| Item | Detail |
|------|--------|
| **Config key** | `world_path` in `/etc/mc-manager/config.json` (desktop Push Agent Config) |
| **Current operator path** | `/home/ubuntu/minecraft/server/world` |
| **May change later** | Automated Setup / Vanilla vs modded may relocate the world — keep path in config; do not hard-code in new call sites |
| **Modes** | `auto` (live if unit active, else cold), `live`, `cold` — CLI: `world_backup.py [auto\|live\|cold]` |
| **Soft cap** | Total bucket Standard usage ≤ **~9.5 GiB**; evict **oldest** backup zips before upload (never delete ledger/budget/meta for space) |
| **Local disk** | No retained local backup copies; work-dir leftovers cleaned; free-space check before zip |
| **Failure policy** | Best-effort on SoftStop; SoftStop continues if backup fails. Live mode always attempts `save-on`. |
| **Gap** | Door/Console SoftStop alone (no idle-agent / `graceful_stop.sh`) does not run this backup yet |
| **Code** | `vm_agent/world_backup.py` |

**IAM:** a dynamic group covering **both** VM1 and VM2 instance OCIDs (`<MC_INSTANCES_DYNAMIC_GROUP>`) with policy allowing object (and currently bucket) management as documented in the private file. Prefer tightening to a single bucket / least privilege when productizing.

---

## Identity and access (IAM)

| Principal | Purpose |
|-----------|---------|
| **Human / API user** | Console, API keys for desktop tool (`~/.oci/config`) |
| **SSH key** | `ubuntu@` ephemeral IPs — not the same as the API key |
| **VM1 instance dynamic group** | Legacy idle agent SoftStop |
| **Door instance dynamic group** | `mccontrol` start/stop VM1 + move reserved/public/private IPs |
| **Both-VMs dynamic group** | Instance principal access to **Object Storage** shared bucket |
| **Functions dynamic group** | Budget Function SoftStop (lab matching rule is all `fnfunc` in the tenancy — too broad) |
| **Auth Token** | `docker login` to OCIR only — private |

Lab matching rules pin **instance OCIDs**. Product greenfield: `mcmgr-dg-instances` / `mcmgr-dg-fn` match compartment; **`mcmgr-dg-door` matches the door instance OCID** (tag `mcmgr-role` did not enroll on the identity-domain 3.3 test). Reserved-IP verbs are **in tenancy** (`mcmgr-door-ip`). See [`Lab-Reference-Stack-Notes.md`](Lab-Reference-Stack-Notes.md) and `infra/README.md`. Do not copy tenancy-wide `manage objects` or tenancy-wide `fnfunc`.

Door policies (conceptual; exact text in private file):

```text
Allow dynamic-group <DOOR_DYNAMIC_GROUP> to use instance-family in tenancy
Allow dynamic-group <DOOR_DYNAMIC_GROUP> to manage public-ips in tenancy
Allow dynamic-group <DOOR_DYNAMIC_GROUP> to use private-ips in tenancy
Allow dynamic-group <DOOR_DYNAMIC_GROUP> to use virtual-network-family in tenancy
```

Object Storage for both Minecraft VMs (conceptual; exact text in private file):

```text
Allow dynamic-group <MC_INSTANCES_DYNAMIC_GROUP> to manage buckets in tenancy
Allow dynamic-group <MC_INSTANCES_DYNAMIC_GROUP> to manage objects in tenancy
```

(Initial test policies may be tenancy-wide `manage`; product IaC should scope to the shared bucket / compartment when practical.)

Least privilege is preferred; document exact statements in the private file.

---

## Operational lifecycle

1. **Idle:** VM1 STOPPED; reserved IP on door secondary; MOTD on reserved play IP; door UI on door ephemeral `:8080`.  
2. Friend connects / admin hits **Wake** → door STARTING → OCI START VM1 (no-op if already RUNNING) → `wait_forge` on VM1 primary private `:25565` → move reserved IP to VM1 secondary → PLAYABLE. **Setup** parks the reserved IP on VM1 and sets PLAYABLE when bootstrap finishes with the game already up (do not leave `DOOR_IDLE` + IP on the door).  
3. Players join reserved IP → Forge. On Minecraft start, boot ledger **force-enables** the idle agent and opens a usage interval + lease.  
4. Empty / budget SoftStop → flush → stop Minecraft → **cold world backup to Object Storage** → SoftStop VM1; reserved IP returns to door (door API or reconciler). Manager Force Stop uses the same graceful + cold backup path when the agent is installed.  
5. If VM1 is **STOPPED** with an **open** Object Storage ledger interval, door heal closes it using the lease heartbeat when present (else wall clock) as `stop_uncertain` (reconcile ~1 min or Testing2 heal; skips while STOPPING). VM1 boot may refine the stop **earlier** from journals / list-boots / lease (never later than the prior estimate), detects live OCPU/memory, and opens a new interval + lease.  
6. Optional: $1 budget Function — **tracked v1 source** SoftStops **VM1** and PUTs the spend-brake lock (door stays up). **Live 0.0.11 image** still SoftStops **VM1 and VM2**; door cannot reconcile while STOPPED (**no** world backup on Function SoftStop today — OS-ISSUE-6; IP may be stranded — FN-ISSUE-1).  
7. Whitelist changes = Security List + VM1 firewalld (manually or via desktop tool); keep admin `:8080` and VCN→25565 rules.  
8. Optional **live** world backup while playing: `world_backup.py live` (or future scheduled job) uses `save-off` / `save-all flush` / zip / `save-on` / upload.

**Wake-on-connect** is supported via the door (MOTD / join while idle can trigger wake, depending on mcdoor config). Desktop Force Start of VM1 alone does **not** move the reserved IP — use door Wake for the full path.

---

## What this stack deliberately excludes

- Custom DNS / domain for Minecraft (friends use reserved IP)  
- Pterodactyl or other panels (door web UI is the Phase A control UI)  
- Load balancer, WAF, API Gateway in the game path  
- Public RCON or public Minecraft without allowlists  
- Object Storage / DB for **live gameplay** (Object Storage **is** used for shared control-plane data and backups)  
- Chunky / map UI (deferred Phase B+)  
- Replacing the $1 budget Function path (kept as belt-and-suspenders)
- Bucket Private Endpoint (not required for current design)

---

## Placeholder index

Use these in commands and docs; resolve from the private deployment file.

| Placeholder | Meaning |
|-------------|---------|
| `<HOME_REGION>` | e.g. `us-sanjose-1` |
| `<OCIR_REGION_KEY>` | OCIR endpoint prefix, e.g. `sjc` |
| `<TENANCY_OCID>` | Tenancy OCID |
| `<COMPARTMENT_OCID>` | Compartment (may equal tenancy in simple setups) |
| `<OCIR_NAMESPACE>` | Tenancy Object Storage / OCIR namespace |
| `<OCI_USERNAME>` | Username for OCIR login path |
| `<VCN_OCID>` | VCN |
| `<SUBNET_OCID>` | Public subnet |
| `<SECURITY_LIST_OCID>` | Ingress Security List |
| `<VM1_INSTANCE_OCID>` | Forge compute instance |
| `<VM2_INSTANCE_OCID>` | Door Micro instance |
| `<VM1_EPHEMERAL_IP>` | VM1 SSH public IP |
| `<VM2_EPHEMERAL_IP>` | Door SSH / admin UI public IP |
| `<RESERVED_PUBLIC_IP>` | Dotted play address friends use |
| `<RESERVED_PUBLIC_IP_ID>` | Reserved public IP OCID |
| `<VM1_PRIMARY_PRIVATE_IP>` | e.g. `10.0.0.167` — `wait_forge` |
| `<VM1_PLAY_PRIVATE_IP>` | e.g. `10.0.0.168` — reserved IP target when playing |
| `<VM2_PRIMARY_PRIVATE_IP>` | Door primary private |
| `<VM2_PLAY_PRIVATE_IP>` | Door secondary — reserved IP target when idle |
| `<VM1_PRIVATE_IP_ID>` | OCID of VM1 **play** secondary |
| `<VM2_PRIVATE_IP_ID>` | OCID of door **play** secondary |
| `<DOOR_DYNAMIC_GROUP>` | Dynamic group for door instance principal |
| `<IDLE_AGENT_DYNAMIC_GROUP>` | Dynamic group for VM1 idle SoftStop |
| `<MC_INSTANCES_DYNAMIC_GROUP>` | Dynamic group matching VM1 **and** VM2 (Object Storage access) |
| `<OBJECT_STORAGE_BUCKET>` | Shared private Standard bucket name |
| `<OBJECT_STORAGE_BUCKET_OCID>` | Bucket OCID |
| `<OBJECT_STORAGE_NAMESPACE>` | Tenancy Object Storage namespace (often same string as OCIR namespace) |
| `<BUDGET_FUNCTIONS_DYNAMIC_GROUP>` | Dynamic group name for Functions |
| `<FN_APPLICATION_NAME>` | Functions application name |
| `<FN_FUNCTION_NAME>` | SoftStop function name |
| `<OCIR_REPOSITORY>` | e.g. `budget-repo/shutdown_vm` |
| `<BUDGET_NAME>` | e.g. dollar-limit budget |

---

## Private deployment details

**Do not commit secrets or tenancy-specific IDs into git.**

| File | Role |
|------|------|
| [`data/Infrastructure-Deployment-Private.md`](data/Infrastructure-Deployment-Private.md) | Live OCIDs, IPs, names, tokens, policy text as deployed (**gitignored**; readable by local agents in this workspace) |

Agents and operators should **read** that file when they need real values. Update it when OCI resources change. Update **this** public doc when architecture or behavior changes.

---

## Maintaining this documentation

### When to update **this** file (`Infrastructure-Information.md`)

- New OCI resource types (second instance, reserved IP model, Object Storage, DNS, etc.)  
- Changes to allowlist model, door/idle/budget behavior, shared storage, or Functions emergency path  
- Shape / free-tier policy changes that affect design  
- New on-box paths or systemd units  

### When to update the **private** file

- Any OCID, IP, username, image tag, Auth Token, or exact IAM statement change  

### Agent expectations

1. Prefer **placeholders** in public docs.  
2. Keep secrets and live OCIDs in gitignored `data/config.local.json` (and Setup tofu outputs).  
3. After infrastructure or door/idle behavior changes, update these docs in the same effort when practical.  
4. If product scope/UX/budget-ownership decisions change, update **`PRODUCT-IDEAS.md`**. Door software SoT is **`door_vm/`**.

---

## Related repo paths

| Path | Role |
|------|------|
| [`Minecraft-Server-Deployment-Blueprint.md`](Minecraft-Server-Deployment-Blueprint.md) | Minecraft server install/upgrade **mechanism** |
| [`../door_vm/`](../door_vm/README.md) | Door Micro software SoT |
| [`../functions/shutdown_vm/`](../functions/shutdown_vm/README.md) | $1 budget Function (SoftStop VM1 + lock PUT) |
| [`../functions/reconcile_usage/`](../functions/reconcile_usage/README.md) | Usage API 48h ledger reconcile Function |
| [`../vm_agent/`](../vm_agent/README.md) | VM1 idle agent + OS publish + world backup |
| [`VM-Software.md`](VM-Software.md) | What runs on VM1 vs VM2 + **current build status** |
| [`Door-VM-Control-Plane.md`](Door-VM-Control-Plane.md) | Door/mccontrol behavior deep dive |
| [`Issues.md`](Issues.md) | Known infrastructure / app issues |
| [`Contracts-Object-Storage.md`](Contracts-Object-Storage.md) | Object Storage object names / writers |
| [`V1-Implementation-Plan.md`](V1-Implementation-Plan.md) | Living v1 execution checklist |
| [`OCI-API-Usage.md`](OCI-API-Usage.md) | 429 backoff, waiters, pagination, Always Free request thrift |
| [`PRODUCT-IDEAS.md`](PRODUCT-IDEAS.md) | Product vision + MVP / v1 / later |

## Known issues

See [`Issues.md`](Issues.md) for MOTD first-kick race, heal/reconcile history, Force Start dual-write quirks, door SoftStop skipping world backup (OS-ISSUE-6), budget Function stopping the door (FN-ISSUE-1), and related items.
