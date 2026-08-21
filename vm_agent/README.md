# VM1 idle agent (`vm_agent/`)

Tracked source for software that runs on the **Ampere game host (VM1)** under `/opt/mc-manager` (plus systemd units). Deploy via product Setup or Manager **Deploy / Update Idle Agent**.

| Doc | Role |
|-----|------|
| [`docs/VM-Software.md`](../docs/VM-Software.md) | VM1 + VM2 overview + **current build status** |
| [`docs/Infrastructure-Information.md`](../docs/Infrastructure-Information.md) | Full OCI architecture |
| [`docs/Contracts-Object-Storage.md`](../docs/Contracts-Object-Storage.md) | Ledger publish, uncertain-stop repair, lease heartbeat |
| [`docs/Issues.md`](../docs/Issues.md) | SoftStop hang, dual-write, repair rules |

## What it does

SoftStop VM1 after `idle_timeout_minutes` if Minecraft is **empty** (RCON `list`) **or** the `minecraft` unit is **not** `active` (stopped, failed, crash-loop). Same timeout; do not SoftStop on the first tick of a normal start. When the game is already down, skip RCON; still cold-backup if the world exists, then ledger/lease + OCI SoftStop. Budget soft cap still stops a VM that is up with the game down.

Redeploy to the VM (`/opt/mc-manager`) after changing this tree. Door Phase 4 deploy does **not** push it.

1. **Idle / budget SoftStop** — `mc-idle-watch.timer` → unit not active **or** RCON empty → `timeout 120 systemctl stop` when the game is up → **cold world zip → Object Storage** (9.5 GiB soft cap; evict oldest `backups/*.zip` first; delete local zip) → **close+save local ledger** → **clear lease** → **retry OS publish** → OCI SoftStop  
2. **Lease heartbeat (Phase 5)** — while Minecraft is active, refresh `ledger/lease.json` about every 5 minutes (does not dirty ledger flags)  
3. **Boot ledger** — `mc-boot-ledger.service` (**Before=** `minecraft.service`) → **force-enable idle agent** (timer + local/OS `idle_agent_enabled=true`) → **force-pull** `messages/chat.json` (identity MOTD/icon + idle chat templates) so this Java start loads them → **force-pull** OS ledger + lease → merge → close prior opens (lease / list-boots) → repair `stop_uncertain` → fill missing boots → **detect live shape** → open boot interval + lease → publish; sync shape to local config + OS `budget/config.json`  
4. **Object Storage** — publish `ledger/usage.json` (with `revision`); dirty manager + door flags; publish `ledger/lease.json`; upload `backups/world-*.zip`  
5. **Live world backup (ready for scheduled use)** — `world_backup.py live` / `mode=auto` while unit active: RCON `save-off` → `save-all flush` → zip → `save-on` (always) → upload → delete local. SoftStop uses **cold** after stop. **`--stream-stdout`** zips the world to stdout (no Object Storage PUT) for Manager oversized-world SSH download.  

Intervals always include **`ocpus`** / **`memory_gb`** from **live guest detection** (config is fallback only), so totals stay correct after Console/Manager resize. Mid-session shape change (rare) closes the open interval and opens a new one.

### World path (`world_path`)

Default / current operator layout: **`/home/ubuntu/minecraft/server/world`**.

This is **config-driven** (`world_path` in `/etc/mc-manager/config.json`). Automated Setup and Vanilla vs modded installs may place the world elsewhere later — agents and code should read the config key, not assume the path forever.

RCON stays **localhost only** (`25575`).

## Layout (typical on VM1)

| Path | Purpose |
|------|---------|
| `/opt/mc-manager/` | Agent install (venv + scripts from this tree) |
| `/etc/mc-manager/config.json` | RCON, budgets, Object Storage, lease heartbeat, **world_path**, backup soft cap |
| `/var/lib/mc-manager/usage.json` | Local usage ledger |
| `/var/lib/mc-manager/lease.json` | Local active-session lease |
| `/var/tmp/mc-manager-backup/` | Temporary zip workspace (cleaned after upload; not a retained archive set) |
| `mc-idle-watch.timer` / `.service` | Periodic idle/budget check + heartbeat |
| `mc-boot-ledger.service` | Oneshot on boot |

## Key files in repo

| Path | Role |
|------|------|
| `idle_watch.py` | Idle / soft-cap stop; lease heartbeat; cold world backup; publish retries before SoftStop |
| `record_boot.py` | Boot force-enable idle + force-pull ledger/lease/messages → merge → reconcile → repair → start + lease → publish |
| `world_backup.py` | Cold + **live** zip → OS upload; **`--stream-stdout`** zip to stdout (no OS PUT) |
| `ledger.py` | Interval math, list-boots reconcile, repair, merge |
| `lease.py` | Lease open / heartbeat / clear helpers |
| `shape_detect.py` | Live OCPU / memory detection from `/proc` |
| `os_publish.py` | Object Storage put/get + dirty flags; lease + budget shape / idle_enabled sync; messages identity pull |
| `install.sh` | On-box install from uploaded tree |
| systemd unit templates | Idle timer + boot ledger |

Door / doorbell software lives in **`door_vm/`**, not here.

## Deploy note

After pulling agent changes from git, **Redeploy idle agent** while VM1 is RUNNING so `/opt/mc-manager` matches this tree (SoftStop/boot/lease fixes are not applied by door Phase 4). Also **Push agent config** so `lease_heartbeat_minutes` lands in `/etc/mc-manager/config.json`.
