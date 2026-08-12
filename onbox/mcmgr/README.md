# On-box Minecraft bootstrap (VM1) — product SoT

**Authority:** mechanism details live in [`docs/Minecraft-Server-Deployment-Blueprint.md`](../../docs/Minecraft-Server-Deployment-Blueprint.md). This tree is the **executable** Vanilla (MVP) bootstrap Phase 3 will upload and run over SSH.

**Not** the idle agent (`/opt/mc-manager` stays in the lab `vm_agent/` tree). **Not** a copy of the operator’s live Forge lab under `/home/ubuntu/minecraft`.

## Layout

```text
onbox/mcmgr/
  common/driver.sh              shared stages (layout → java → module → eula/rcon → unit → manifest)
  common/*.sh                   helpers
  modules/bootstrap-vanilla.sh  piston-meta Vanilla installer module only
  templates/minecraft.service.in
  dry-run/run-dry-run.sh        offline proof (fixtures + temp root)
```

Greenfield paths (live install):

| Path | Role |
|------|------|
| `/opt/mcmgr/server` | `server_dir` / world |
| `/etc/mcmgr/game-manifest.json` | Authoritative game manifest |
| `/etc/mcmgr/rcon.secret` | RCON password (never in Object Storage / manifest body) |
| `/var/lib/mcmgr/bootstrap-state.json` | Resumable stages |
| `/etc/systemd/system/minecraft.service` | Generated from `launch_command` |

## Offline dry-run (Windows / CI)

From Git Bash (or any bash with `python` + `curl`):

```bash
cd onbox/mcmgr
MCMGR_DRY_KEEP=1 MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
```

Uses [`tests/fixtures/game-metadata/`](../../tests/fixtures/game-metadata/) — no apt, no systemctl, no real jar download. Asserts §4.1-shaped manifest + generic unit (`User=mcmgr`, `nogui`, `rcon-graceful-stop.sh`).

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
