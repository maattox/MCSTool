# On-box Minecraft bootstrap (VM1) — product SoT

**Authority:** mechanism details live in [`docs/Minecraft-Server-Deployment-Blueprint.md`](../../docs/Minecraft-Server-Deployment-Blueprint.md). This tree is the **executable** Vanilla (MVP) bootstrap Phase 3 will upload and run over SSH.

**Not** the idle agent (`/opt/mc-manager` stays in the lab `vm_agent/` tree). **Not** a copy of the operator’s live Forge lab under `/home/ubuntu/minecraft`.

## Layout

```text
onbox/mcmgr/
  repair-permissions.sh         root wrapper: layout_ensure_accounts + apply + verify
  repair-server-properties.sh   root wrapper: re-apply managed server.properties (§7.3)
  common/driver.sh              shared stages (… → manifest → idle_agent_sync)
  common/layout.sh              §5 accounts / apply / fail-closed verify
  common/*.sh                   helpers (incl. idle_agent_sync.sh §10.2)
  modules/bootstrap-vanilla.sh  piston-meta Vanilla installer module only
  templates/minecraft.service.in
  dry-run/run-dry-run.sh        offline proof (fixtures + temp root)
```

Greenfield paths (live install):

| Path | Owner:Group | Mode | Role |
|------|-------------|------|------|
| `/opt/mcmgr` | `root:mcmgr` | `0750` | Product tree root |
| `/opt/mcmgr/server` | `mcmgr:mcmgr` | `0750` | `server_dir` / world |
| `/opt/mcmgr/bin` | `root:mcmgr` | `0750` | ExecStop helper + `repair-permissions.sh` + `repair-server-properties.sh` |
| `/etc/mcmgr/game-manifest.json` | `root:mcmgr` | `0640` | Authoritative game manifest |
| `/etc/mcmgr/rcon.secret` | `root:root` | `0600` | RCON password (never in Object Storage / manifest body) |
| `/var/lib/mcmgr/bootstrap-state.json` | `root:root` | `0750` dir | Resumable stages |
| `/etc/systemd/system/minecraft.service` | root | `0644` | Generated from `launch_command` (`User=mcmgr`, `ExecStop=+`, `RestartPreventExitStatus=200`) |

Permission repair (existing VM, same contract as bootstrap — not an ad-hoc chmod):

```bash
sudo bash /path/to/onbox/mcmgr/repair-permissions.sh
# or, once installed:
sudo bash /opt/mcmgr/bin/repair-permissions.sh
```

Managed `server.properties` (in-game whitelist off — SETUP-ISSUE-3):

```bash
sudo bash /path/to/onbox/mcmgr/repair-server-properties.sh
# or, once installed:
sudo bash /opt/mcmgr/bin/repair-server-properties.sh
```

Then re-apply permissions if you wrote under `/opt/mcmgr`.

## Offline dry-run (Windows / CI)

From Git Bash (or any bash with `python` + `curl`):

```bash
cd onbox/mcmgr
MCMGR_DRY_KEEP=1 MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
```

Uses [`tests/fixtures/game-metadata/`](../../tests/fixtures/game-metadata/) — no apt, no systemctl, no real jar download. Asserts §4.1-shaped manifest + generic unit (`User=mcmgr`, `nogui`, `ExecStop=+`, `RestartPreventExitStatus=200`) + §7.3 `white-list=false` / `enforce-whitelist=false` / `online-mode=true`.

## Live install (Phase 3 / operator VM)

```bash
# On VM1 as root after uploading this tree (see SSH notes below):
export EULA_ACCEPTED=true
export MINECRAFT_VERSION=1.21.1   # or latest.release
bash /path/to/onbox/mcmgr/common/driver.sh
```

Requires: root, `curl`, `sha1sum`, `apt-get` (Adoptium) or network for Adoptium API fallback, aarch64 Ubuntu.

## Phase 3 SSH upload notes

Follow lab [`Agent-Deploy-Pitfalls.md`](../../../OCI-mc-server-manager/docs/Agent-Deploy-Pitfalls.md):

1. SFTP as `ubuntu` into a **ubuntu-writable** staging dir under `/tmp/...` (do not `sudo mkdir` then SFTP into it).
2. Strip **CRLF** on scripts authored on Windows before `bash`.
3. Privileged multi-step work: `sudo bash -c '…'` (a bare `sudo a && b` only elevates `a`).
4. Manager must **not** re-implement authoritative piston-meta URL/hash resolution in C# — wizard may fetch the version list read-only for display; on-box module re-resolves at install time.

## Out of scope here

- Paper / modded installer modules (v1)
- Full idle-agent `world_path` / `minecraft_unit` sync (MVP Step **2.4**)
- OpenTofu / Setup wizard orchestration (Phase 3)
- Migrating the live Forge lab path
