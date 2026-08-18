# Agent deploy pitfalls (VM1 / door / Manager SSH)

**Audience:** coding agents only — not operator runbooks.  
**Why this exists:** During Object Storage Phases 1–5, deploy failures were fixed reactively after the operator pasted errors. Read this **before** writing or changing SSH/`sudo`/SFTP deploy code (product `SetupBootstrapService`, lab `app/door_deploy.py`, lab `app/ssh_ops.py`, ad-hoc SSH).

Related live quirks for operators: lab [`docs/Issues.md`](../../OCI-mc-server-manager/docs/Issues.md). Operator copy-paste commands: lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md). Build map: lab [`docs/VM-Software.md`](../../OCI-mc-server-manager/docs/VM-Software.md).

---

## Hard rules (copy these into new deploy code)

1. **`FirewallClient.sudo()` only prefixes `sudo` on the string you pass.**  
   `fw.sudo("cp A B && mv B C")` becomes `sudo cp A B && mv B C` → **`mv` runs as `ubuntu`**.  
   **Fix:** `fw.sudo("bash -c " + repr("cp A B && mv B C && chmod …"))` so the **entire** chain is root.

2. **SFTP uploads run as `ubuntu`.** Never `sudo mkdir` a staging dir you will SFTP into.  
   **Fix:** `fw.run("mkdir -p /tmp/…")` (user), optionally `fw.sudo("chown -R ubuntu:ubuntu /tmp/…")` if a prior root mkdir poisoned it. Copy into `/opt/…` only under `sudo bash -c '…'`.

3. **Do not overwrite a running binary in place** (`ETXTBSY` on `mccontrol`).  
   **Fix:** `systemctl stop mccontrol` → `cp …/mccontrol.new` → `mv …new …/mccontrol` → `chmod` → start (all under one `sudo bash -c`).

4. **Strip CRLF on every shell script you upload from Windows** (`sed -i 's/\r$//' …`) before `bash` runs it. Shebang/`set -o pipefail` break with `\r`. Same for on-box `*.py` helpers (`paper_fill_v3.py`, `fabric_meta.py`) — Setup's onbox `find` must include `-name '*.py'`.

5. **Door scripts under `set -u` must default `HOME`:**  
   `export HOME="${HOME:-/home/ubuntu}"`  
   Systemd oneshots often omit `HOME` → `HOME: unbound variable` (heal/pull looked “broken” while manual SSH worked).

6. **Prefer product `door_vm/` as SoT.** Lab `app/door_deploy.py` resolves **`OCI-mc-server/door_vm` first**. If gitignored lab `development/vm2-door/...` is used as a fallback and is stale, Phase 3/4 may deploy **old** sources. Do not edit a lab `door_vm/` copy — that tree is a pointer only.

7. **Manager Python on Windows:** bare `python` on PATH may lack `oci` (wrong install). Use `run.bat` / explicit Python 3.13 when running Manager or one-off scripts.

8. **Door `oci.env` must include Object Storage namespace/bucket** when `object_storage_enabled` is true. `install.sh` used to rewrite `/etc/mccontrol/oci.env` from compute OCIDs only; wake then failed `pull_os_budget.sh` and stuck **DEGRADED**. Also write guest netplan for the **secondary** play IP — the reserved public IP targets that address, not the primary/ephemeral.

9. **`set -u` CR-strip:** `${UNSET//$'\r'/}` aborts before `:-default`. Default optional vars first (`POLL_INTERVAL_SEC="${POLL_INTERVAL_SEC:-10}"` then strip CR). See `wait_forge.sh`.

10. **`ubuntu` is the SSH user and often cannot read/write what you need.** Recurring across many sessions: `/etc/mccontrol/oci.env` (600 root), `/etc/mc-manager/`, `/etc/mcmgr/` (**0750 `root:mcmgr`**), `/opt/mcmgr/`, systemd units, `/opt/mccontrol/` scripts. **Check `ls -l` and use `sudo` (or fix ownership/mode) before retrying as `ubuntu`.** Do not spend a session rediscovering `Permission denied`. Setup cloud-init wait must `sudo -n test -f` the marker (SETUP-ISSUE-5: `test -f /etc/mcmgr/cloud-init-done` as `ubuntu` is always WAIT). Operator runbook: lab [`docs/Operator-Troubleshooting.md`](../../OCI-mc-server-manager/docs/Operator-Troubleshooting.md).

11. **systemd `User=mcmgr` is a different user than SSH `ubuntu`.** `minecraft.service` `status=200/CHDIR` means **`mcmgr` cannot traverse `WorkingDirectory=`** (SETUP-ISSUE-4). Do **not** chmod a single directory; do **not** switch `User=` to `ubuntu` or `0777`. Re-apply the **whole** blueprint §5 contract via `onbox/mcmgr/repair-permissions.sh` (`layout_apply` + fail-closed `layout_verify`). Any later `mkdir` (Setup whitelist seed, vanilla/eula helpers, cloud-init) must call `layout_apply` again — skipped `layout_ready` on resume is not enough. ExecStop runs as **root** (`ExecStop=+…`) so it can read `rcon.secret` `0600`. Stop a restart storm with `systemctl stop minecraft` before diagnosing.

12. **Idle-agent SoT is product `vm_agent/`.** After changing it, **Redeploy idle agent** while VM1 is RUNNING so `/opt/mc-manager` matches. Door Phase 4 does **not** push VM1.

13. **Oracle Ubuntu ships `netfilter-persistent` (SSH-only INPUT REJECT) which Conflicts with firewalld.** After SoftStop reboot, netfilter-persistent can win and leave firewalld **inactive** — Minecraft listens but door `wait_forge` and public 25565 fail (SETUP-ISSUE-7). Cloud-init and guest repair must **disable + mask `netfilter-persistent` before** `systemctl enable --now firewalld`.

14. **GNU `cp -f src dest` fails when they are the same file** (`cp: '…' and '…' are the same file`). `layout_apply` used to copy `env.sh` / `layout.sh` into `/opt/mcmgr/lib/` even when repair was already running from that tree (SETUP-ISSUE-8). Skip with `[[ src -ef dest ]]`. Do not treat that error as a wipe/replace failure and restart Minecraft.

---

## Failure → cause → correct pattern

| Symptom | Cause | Do this instead |
|---------|--------|-----------------|
| `Permission denied` on `mv` after `sudo cp … && mv …` | Only first command got `sudo` | `sudo bash -c 'cp … && mv … && chmod …'` |
| SFTP `Permission denied` under `/tmp/door-p*` | Staging dir created with `sudo` (root-owned) | `mkdir` as ubuntu; `chown ubuntu:ubuntu` fallback |
| `Text file busy` / `ETXTBSY` replacing `mccontrol` | Overwrote running ELF | Stop unit → write `.new` → `mv` → start |
| `set: pipefail\r: invalid option` / weird bash errors | CRLF from Windows checkout | `sed -i 's/\r$//'` after upload |
| Reconcile journal: `HOME: unbound variable` every minute | `${HOME}` with `set -u` under systemd | `HOME="${HOME:-/home/ubuntu}"` before PATH |
| Heal works via Testing2, never via timer | Same HOME bug, or old reconcile `set -e` skipped heal | Fixed scripts + deploy Phase 4; verify journal |
| `ModuleNotFoundError: oci` launching Manager | Wrong `python` on PATH | `run.bat` |
| Phase 3 `make` succeeds, install fails | Elevations / ETXTBSY (above) | Stop + `bash -c` install chain |
| Door still running old heal/pull after “deploy” | Uploaded only some files, or deployed from stale `development/` tree | Deploy from current `door_vm/`; Phase 4 now ships heal+reconcile+pull+`ip_to_vm1` |
| `wait_forge.sh: POLL_INTERVAL_SEC: unbound variable` | `set -u` CR-strip on an unset optional var | Default `:-10` / `:-600` **before** `${var//$'\r'/}` |
| `ubuntu`: `/etc/mccontrol/oci.env: Permission denied` | File is root-only (600) | Source as root; not a broken install |
| Setup timeout `Last: WAIT` for `/etc/mcmgr/cloud-init-done` while `cloud-init status` is **done** | Marker is under `0750 root:mcmgr`; waiter used `test -f` as `ubuntu` (SETUP-ISSUE-5) | `sudo -n test -f` in `WaitCloudInitAsync`. Do not wait longer, reboot, or `chmod 0755 /etc/mcmgr` |
| `Permission denied` reading `/etc/mc-manager/`, `/opt/mcmgr/`, systemd drop-ins | Recurring: `ubuntu` is not root | `sudo` or `chown`/`chmod` the specific path **before** retrying |
| `minecraft.service` `status=200/CHDIR` / WorkingDirectory Permission denied | `User=mcmgr` cannot traverse `WorkingDirectory` (SETUP-ISSUE-4) | Stop the unit; `namei -l`; `sudo bash …/repair-permissions.sh` (whole §5 contract). Not `0777`, not `User=ubuntu` |
| Wake **DEGRADED** `ip_to_vm1.sh failed` after Forge TCP OK | Compartment IAM and/or door DG not matching; or `public-ip update` without `--force` | Tenancy `mcmgr-door-ip` + door DG by instance.id (product HCL); scripts `--force` + already-on-target no-op |
| Micro `make mccontrol` takes many minutes | Expected on E2.1.Micro | Set long SSH timeouts; don’t assume hung |

---

## Patterns already in-tree (follow them)

| Area | Reference |
|------|-----------|
| Product Setup door/VM1 upload | `src/McManager.Core/Setup/SetupBootstrapService.cs` |
| Door Phase 3 binary replace | lab `app/door_deploy.py` — stop service + `sudo bash -c` cp/mv/chmod |
| Door Phase 4 staging | lab `app/door_deploy.py` — ubuntu `/tmp/door-p4` + `chown` + `sudo bash -c` install |
| VM1 agent staging | lab `app/ssh_ops.py` — comment + `/tmp/mc-manager-deploy` without sudo mkdir |
| Config write with sudo | lab `ssh_ops._write_remote_file(..., use_sudo=True)` — SFTP to tmp then `sudo mv` |

---

## Checklist before claiming “deploy works”

- [ ] Multi-step root work is one `sudo bash -c '…'` (or equivalent), not `sudo cmd1 && cmd2`
- [ ] SFTP targets are ubuntu-writable
- [ ] Shell scripts LF-normalized on the VM
- [ ] Door OS scripts default `HOME`
- [ ] Optional `set -u` vars defaulted **before** CR-strip
- [ ] `oci.env` sourced as root when diagnosing
- [ ] Replacing `mccontrol`: service stopped first
- [ ] Sources came from the tree you intend (product `door_vm/` vs stale lab `development/`)
- [ ] After door script changes: Testing2 **Door reconcile journal** shows expected lines (no `HOME: unbound`)
- [ ] After product `vm_agent/` changes: **Redeploy idle agent** on a RUNNING VM1 (door Phase 4 does not push VM1).
- [ ] `minecraft.service` `User=mcmgr` can `chdir` `WorkingDirectory` (`namei -l`); after any `mkdir` under `/opt/mcmgr` re-run `layout_apply` / `repair-permissions.sh` — do not ship a one-path chmod
- [ ] Setup cloud-init wait uses `sudo -n test -f` on `/etc/mcmgr/cloud-init-done` (0750; `ubuntu` `test -f` is a false WAIT)

---

## Out of scope here

Operator product docs, Always Free cost policy, and Manager roadmap — see lab `PRODUCT-IDEAS.md` / `Infrastructure-Information.md`.
