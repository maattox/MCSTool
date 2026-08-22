# Door VM (`door_vm/`) — tracked source of truth

This folder is the **tracked** tree for **VM2 (door Micro)** software: `mccontrol`, OCI shell wrappers, Object Storage pull/heal, web UI, systemd units, and helper scripts. Setup uploads it over SSH.

| Authority | Role |
|-----------|------|
| **`door_vm/` (this tree)** | What to rebuild/redeploy onto a new Always Free Micro |
| [`docs/Door-VM-Control-Plane.md`](../docs/Door-VM-Control-Plane.md) | Behavior / state machine explanation |
| [`docs/PRODUCT-IDEAS.md`](../docs/PRODUCT-IDEAS.md) | Product intent (wins on design conflicts) |

Do **not** commit `oci.env`, API keys, or tenancy OCIDs. Use `oci/config.example.env` and `config.example.json` as templates; live values stay in `/etc/mccontrol/` on the VM and in gitignored `data/`.

---

## Layout

```text
door_vm/
  README.md                 ← this file
  Makefile                  ← builds build/mccontrol
  install.sh                ← full-ish install helper (prototype-era; prefer steps below)
  config.example.json
  include/  src/            ← C11 mccontrol sources
  oci/                      ← start/stop VM1, IP move, wait_forge, pull/heal OS
  scripts/                  ← reconcile, reset, diagnose helpers
  web/static/               ← door admin UI (served on :8080)
  assets/icons/             ← MOTD favicons (idle/starting/exhausted.png; Manager-composed defaults)
  systemd/                  ← mccontrol.service + reconcile timer/service
  tests/                    ← unit smoke (optional on Micro)
```

**Not in this tree (host-level):** live `/etc/mccontrol/oci.env` (Setup writes Object Storage namespace/bucket + OCIDs). Product Setup also writes guest netplan (`/etc/netplan/99-mcmgr-play.yaml`) for the secondary play IP. Forge/Vanilla itself is VM1.

Installed on the live door (typical):

| On VM2 | From |
|--------|------|
| `/opt/mccontrol/build/mccontrol` | `make` → install binary |
| `/opt/mccontrol/oci/*.sh` | `oci/` |
| `/opt/mccontrol/scripts/*.sh` | `scripts/` |
| `/opt/mccontrol/web/static/` | `web/static/` |
| `/etc/mccontrol/config.json` | from example + local edits |
| `/etc/mccontrol/oci.env` | secrets / OCIDs (not in git) |
| `/var/lib/mccontrol/` | state + `os-cache/` |
| `/etc/systemd/system/mccontrol*.service` / `.timer` | `systemd/` |

---

## Rebuild from bare Ubuntu Micro (outline)

Assumes: Ubuntu 22.04 aarch64/x86_64 Micro, `ubuntu` user, instance principal for door dynamic group, Security List allows your admin `/32` → `:22` and `:8080`, VCN can reach Object Storage / OCI APIs.

1. **Packages:** `build-essential`, `curl`, OCI CLI under `/home/ubuntu/bin` (same pattern as Testing docs).
2. **Copy this tree** to the VM (Setup uploads it over SSH).
3. **Config:** create `/etc/mccontrol/config.json` from `config.example.json`; set `object_storage_enabled`, cache paths, ports, `vm1_private_ip` as needed.
4. **Env:** create `/etc/mccontrol/oci.env` from `oci/config.example.env` — `INSTANCE_ID` (VM1), compartment, VNIC/IP OCIDs, `OBJECT_STORAGE_NAMESPACE` / `BUCKET`, `OS_CACHE_DIR`. **Never commit this file.**
5. **Build:** `make mccontrol` on the door (slow on Micro).
6. **Install binary + assets:** stop `mccontrol` if running; install binary under `/opt/mccontrol/build/`; copy `oci/`, `scripts/`, `web/`.
7. **Systemd:** install units from `systemd/`; `systemctl enable --now mccontrol.service mccontrol-reconcile.timer`.
8. **IAM:** door instance in dynamic group(s) for compute/IP move **and** Object Storage.
9. **Smoke:** `curl -sS http://127.0.0.1:8080/api/status`; then Manager Troubleshooting / door journals.

Manager **Troubleshooting** / Setup door deploy covers the same operations (park IP, OS refresh, heal, redeploy from this tree).

### Functional checklist (door)

- [x] mcdoor MOTD / wake-on-join / HTTP admin  
- [x] OS wake budget gate (`object_storage_enabled`)  
- [x] Reconcile IP handback when VM1 stopped under PLAYABLE  
- [x] One-shot orphan ledger heal + `ledger_heal_verified` latch (**STOPPED**-only; lease-aware close — Phase 5)  
- [x] `HOME` default for systemd oneshot scripts  
- [ ] First-join custom kick text always shown (parked — Issues DOOR-ISSUE-1)

---

## Object Storage scripts (Phases 3–5)

| Script | Role |
|--------|------|
| `oci/pull_os_budget.sh` | Flag-aware (or `--force`) get ledger/budget; clear door flags (**wake path**; skips flags PUT when nothing to pull) |
| `oci/heal_os_ledger.sh` | If VM1 is **STOPPED** (not STOPPING) and open interval → close at lease `last_heartbeat_at` when present (else wall clock) as `stop_uncertain`, put ledger + clear lease, dirty manager+vm1 |
| `scripts/reconcile_vm1.sh` | Idle-empty handback; **no** routine budget pull; heal **once** per down episode when STOPPED (`os-cache/ledger_heal_verified`) |
| `oci/ip_to_vm1.sh` | Move reserved IP to VM1; clears `ledger_heal_verified` |

Wake path (`do_wake`) also runs pull before the spend-brake lock check and the daily/soft budget gate when `object_storage_enabled` is true. See also `docs/Object-Storage-Phase5.md`.

---

## Budget gate (OS mode)

- Prefer `monthly_ocpu_target / days-in-LA-month` from OS `budget/config.json`  
- Also refuse wake when month-to-date OCPU-h ≥ `soft_ocpu_cap`  
- Also refuse **START VM1** when `meta/spend-brake-triggered.json` is present (distinct MOTD/kick from daily exhaustion)  
- Fail closed if OS pull fails (including a non-404 lock GET)  

See `docs/Object-Storage-Phase3.md`.

---

## Known issues

See [`docs/Issues.md`](../docs/Issues.md) (MOTD first-kick race, heal/reconcile history, etc.).
