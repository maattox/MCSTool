# On-box Minecraft bootstrap (VM1) — product SoT

**Authority:** mechanism details live in [`docs/Minecraft-Server-Deployment-Blueprint.md`](../../docs/Minecraft-Server-Deployment-Blueprint.md). This tree is the **executable** Vanilla + Paper + Fabric + NeoForge bootstrap Setup uploads and runs over SSH. Wizard UI for Fabric/Modded is a later V1 step — the on-box Fabric/NeoForge modules are invoked with `DISTRIBUTION=fabric` or `DISTRIBUTION=neoforge`.

**Not** the idle agent (`/opt/mc-manager` stays in this repo’s `vm_agent/` tree). **Not** a copy of the operator’s live Forge lab under `/home/ubuntu/minecraft`.

## Layout

```text
onbox/mcmgr/
  repair-permissions.sh         root wrapper: layout_ensure_accounts + apply + verify
  repair-server-properties.sh   root wrapper: re-apply managed server.properties (§7.3)
  common/driver.sh              shared stages (… → manifest → idle_agent_sync)
  common/layout.sh              §5 accounts / apply / fail-closed verify
  common/*.sh                   helpers (incl. idle_agent_sync.sh §10.2)
  common/paper_fill_v3.py       Fill v3 STABLE resolve (SHA-256, no v2 URLs)
  common/fabric_meta.py         Fabric meta v2 resolve (game+loader+installer, none_published)
  common/neoforge_meta.py       NeoForge Maven XML resolve (argfile_tree, none_published)
  modules/bootstrap-vanilla.sh  piston-meta Vanilla installer module
  modules/bootstrap-paper.sh    Fill v3 Paper installer module
  modules/bootstrap-fabric.sh   Fabric launcher-jar installer module
  modules/bootstrap-neoforge.sh NeoForge installer-jar + --installServer module
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
DISTRIBUTION=paper MINECRAFT_VERSION=1.21.10 bash dry-run/run-dry-run.sh
DISTRIBUTION=fabric MINECRAFT_VERSION=1.21.8 bash dry-run/run-dry-run.sh
DISTRIBUTION=neoforge MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
```

Uses [`tests/fixtures/game-metadata/`](../../tests/fixtures/game-metadata/) — no apt, no systemctl, no real jar download. Asserts a §4.1 (Vanilla), §4.2 (Paper), Fabric loader (§18 / §4.4 artifact shape, `modpack` still null), or NeoForge argfile tree (§19 / §4.3 artifact shape without a pack) manifest + the **generic** unit (`User=mcmgr`, Vanilla/Fabric `nogui` / Paper `--nogui` / NeoForge `@user_jvm_args.txt @unix_args --nogui`, `ExecStop=+`, `RestartPreventExitStatus=200`) + §7.3 `white-list=false` / `enforce-whitelist=false` / `online-mode=true`.

## Live install (Phase 3 / operator VM)

```bash
# On VM1 as root after uploading this tree (see SSH notes below):
export EULA_ACCEPTED=true
export MINECRAFT_VERSION=1.21.1   # or latest.release
bash /path/to/onbox/mcmgr/common/driver.sh

# Paper (Optimized Vanilla) — Fill v3 STABLE, same generic unit:
export EULA_ACCEPTED=true
export DISTRIBUTION=paper
export MINECRAFT_VERSION=1.21.10
bash /path/to/onbox/mcmgr/common/driver.sh

# Fabric loader only (no pack import) — meta v2 three-axis launcher jar:
export EULA_ACCEPTED=true
export DISTRIBUTION=fabric
export MINECRAFT_VERSION=1.21.8
bash /path/to/onbox/mcmgr/common/driver.sh

# NeoForge loader only (no pack import) — Maven XML + --installServer argfile tree:
export EULA_ACCEPTED=true
export DISTRIBUTION=neoforge
export MINECRAFT_VERSION=1.21.1
bash /path/to/onbox/mcmgr/common/driver.sh
```

Requires: root, `curl`, `sha1sum` (Vanilla) / `sha256sum` (Paper), `python3`, `apt-get` (Adoptium) or network for Adoptium API fallback, aarch64 Ubuntu. Paper/Fabric/NeoForge HTTP calls send a descriptive User-Agent (`mcmgr-bootstrap/…` + GitHub URL). Fabric and NeoForge have no published installer/launcher checksum (`none_published`). NeoForge GETs use a 45s timeout and retry; failures name `maven.neoforged.net`. Minecraft **1.20.1 and older are refused** (Forge is the 1.20.1 path — later step).

## Phase 3 SSH upload notes

Follow [`Agent-Deploy-Pitfalls.md`](../../docs/Agent-Deploy-Pitfalls.md):

1. SFTP as `ubuntu` into a **ubuntu-writable** staging dir under `/tmp/...` (do not `sudo mkdir` then SFTP into it).
2. Strip **CRLF** on scripts and `*.py` helpers authored on Windows before `bash`/`python3`.
3. Privileged multi-step work: `sudo bash -c '…'` (a bare `sudo a && b` only elevates `a`).
4. Manager must **not** re-implement authoritative piston-meta / Fill v3 URL/hash resolution in C# for the install itself — wizard may fetch version lists read-only for display; on-box modules re-resolve at install time.

## Out of scope here

- Setup wizard Modded / Fabric radio (later V1 step)
- Forge / pack-import installer modules
- Quilt as a Setup entry point
