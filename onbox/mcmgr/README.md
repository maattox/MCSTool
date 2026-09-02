# On-box Minecraft bootstrap (VM1) — product SoT

This tree is the **executable** Vanilla + Paper + Fabric + NeoForge + Forge bootstrap Setup uploads and runs over SSH. The on-box Fabric/NeoForge/Forge modules are invoked with `DISTRIBUTION=fabric`, `DISTRIBUTION=neoforge`, or `DISTRIBUTION=forge`. Forge is **not** a Setup radio next to NeoForge; it exists for packs that declare Forge (1.12.2-era and 1.20.1).

**Not** the idle agent (`/opt/mc-manager` stays in this repo’s `vm_agent/` tree). **Not** a copy of the operator’s live Forge lab under `/home/ubuntu/minecraft`.

## Layout

```text
onbox/mcmgr/
  repair-permissions.sh         root wrapper: layout_ensure_accounts + apply + verify
  repair-server-properties.sh   root wrapper: re-apply managed server.properties (§7.3)
  prepare-pack-replace.sh       day-2 full pack replace: stop + clear loader/pack, keep world/RCON
  common/driver.sh              shared stages (… → manifest → idle_agent_sync)
  common/layout.sh              §5 accounts / apply / fail-closed verify
  common/*.sh                   helpers (incl. idle_agent_sync.sh §10.2)
  common/paper_fill_v3.py       Fill v3 STABLE resolve (SHA-256, no v2 URLs)
  common/fabric_meta.py         Fabric meta v2 resolve (game+loader+installer, none_published)
  common/neoforge_meta.py       NeoForge Maven XML resolve (argfile_tree, none_published)
  common/forge_meta.py          Forge promotions_slim resolve (single_jar / argfile_tree)
  common/quarantine_mod.py      Layer 3 crash quarantine (move one blamed jar; never delete)
  common/quarantine_mod.sh      Wrapper installed to `/opt/mcmgr/bin/quarantine_mod.sh`
  modules/bootstrap-vanilla.sh  piston-meta Vanilla installer module
  modules/bootstrap-paper.sh    Fill v3 Paper installer module
  modules/bootstrap-fabric.sh   Fabric launcher-jar installer module
  modules/bootstrap-neoforge.sh NeoForge installer-jar + --installServer module
  modules/bootstrap-forge.sh    Forge installer (Vanilla jar first; 1.12.2 / 1.20.1)
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

Managed `server.properties` (in-game whitelist off — SETUP-ISSUE-3). Repair always rewrites RCON, `white-list=false`, `enforce-whitelist=false`, and `online-mode=true`. `difficulty` / `max-players` / `motd` are seeded only when missing so Manager Settings and Identity are not clobbered.

```bash
sudo bash /path/to/onbox/mcmgr/repair-server-properties.sh
# or, once installed:
sudo bash /opt/mcmgr/bin/repair-server-properties.sh
```

Then re-apply permissions if you wrote under `/opt/mcmgr`.

Day-2 **full pack replace** (keep the world unless wiping). Manager Server Management **Change pack** calls `SetupBootstrapService.ReplacePackAsync`. On-box prepare, then the same `driver.sh` + pack copy Setup uses:

```bash
# Stop Minecraft, clear loader/mods/config, keep world + rcon.secret + eula/properties:
KEEP_WORLD=1 sudo -E bash /path/to/onbox/mcmgr/prepare-pack-replace.sh
# or, once installed:
KEEP_WORLD=1 sudo -E bash /opt/mcmgr/bin/prepare-pack-replace.sh
# Then the same driver.sh as Setup (DISTRIBUTION + MINECRAFT_VERSION from the new pack).
# WIPE_WORLD=1 also deletes world/ (identity/RCON still kept).
```

Do **not** delete `/opt/mcmgr` or `/etc/mcmgr/rcon.secret`. Light swap (converge `mods/` only) is parked.

## Offline dry-run (Windows / CI)

From Git Bash (or any bash with `python` + `curl`):

```bash
cd onbox/mcmgr
MCMGR_DRY_KEEP=1 MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
DISTRIBUTION=paper MINECRAFT_VERSION=1.21.10 bash dry-run/run-dry-run.sh
DISTRIBUTION=fabric MINECRAFT_VERSION=1.21.8 bash dry-run/run-dry-run.sh
DISTRIBUTION=neoforge MINECRAFT_VERSION=1.21.1 bash dry-run/run-dry-run.sh
DISTRIBUTION=forge MINECRAFT_VERSION=1.12.2 bash dry-run/run-dry-run.sh
bash dry-run/test-resume.sh           # second driver pass / SETUP-ISSUE-16
bash dry-run/run-pack-replace-dry.sh   # KEEP_WORLD vs WIPE_WORLD (no driver download)
```

Uses [`tests/fixtures/game-metadata/`](../../tests/fixtures/game-metadata/) — no apt, no systemctl, no real jar download. Asserts a §4.1 (Vanilla), §4.2 (Paper), Fabric loader (§18 / §4.4 artifact shape, `modpack` still null), NeoForge argfile tree (§19 / §4.3 artifact shape without a pack), or Forge legacy single jar (§20 / 1.12.2 recommended pin) manifest + the **generic** unit (`User=mcmgr`, Vanilla/Fabric/Forge-legacy `nogui` / Paper `--nogui` / NeoForge `@user_jvm_args.txt @unix_args --nogui`, `ExecStop=+`, `RestartPreventExitStatus=200`) + §7.3 `white-list=false` / `enforce-whitelist=false` / `online-mode=true`.

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

# Forge loader only (no pack import; not a Setup radio) — promotions_slim + Vanilla jar first:
export EULA_ACCEPTED=true
export DISTRIBUTION=forge
export MINECRAFT_VERSION=1.12.2
bash /path/to/onbox/mcmgr/common/driver.sh
```

Requires: root, `curl`, `sha1sum` (Vanilla) / `sha256sum` (Paper), `python3`, `apt-get` (Adoptium) or network for Adoptium API fallback, aarch64 Ubuntu. Paper/Fabric/NeoForge/Forge HTTP calls send a descriptive User-Agent (`mcmgr-bootstrap/…` + GitHub URL). Fabric, NeoForge, and Forge have no published installer/launcher checksum (`none_published`). NeoForge GETs use a 45s timeout and retry; failures name `maven.neoforged.net`. Forge GETs `promotions_slim.json` (not the ad HTML page); installer jars come from `maven.minecraftforge.net`. Minecraft **1.20.1 and older are refused for NeoForge** (Forge is the 1.20.1 / 1.12.2 path). Forge refuses Minecraft **older than 1.7**.

## SSH upload notes

When uploading from Windows:

1. SFTP as `ubuntu` into a **ubuntu-writable** staging dir under `/tmp/...` (do not `sudo mkdir` then SFTP into it).
2. Strip **CRLF** on scripts and `*.py` helpers authored on Windows before `bash`/`python3`.
3. Privileged multi-step work: `sudo bash -c '…'` (a bare `sudo a && b` only elevates `a`).
4. Manager must **not** re-implement authoritative piston-meta / Fill v3 URL/hash resolution in C# for the install itself — wizard may fetch version lists read-only for display; on-box modules re-resolve at install time.

## Out of scope here

- Setup wizard Modded / Fabric radio (later V1 step)
- Pack-import installer modules
- Quilt as a Setup entry point
