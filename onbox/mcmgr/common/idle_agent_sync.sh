#!/usr/bin/env bash
# Sync idle-agent /etc/mc-manager/config.json from game-manifest + rcon.secret (§10.2).
# Call after manifest_write. Live: RMW only if config already exists (agent installed).
# Dry-run: create a minimal stub so sync is testable.
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

idle_agent_sync_from_manifest() {
  [[ -f "${GAME_MANIFEST}" ]] || mcmgr_die "idle_agent_sync: missing ${GAME_MANIFEST}"
  [[ -f "${RCON_SECRET}" ]] || mcmgr_die "idle_agent_sync: missing ${RCON_SECRET}"

  if [[ ! -f "${MC_MANAGER_CONFIG}" ]]; then
    if [[ "${DRY_RUN}" == "1" ]]; then
      mkdir -p "$(dirname "${MC_MANAGER_CONFIG}")"
      printf '%s\n' '{"version":1}' >"${MC_MANAGER_CONFIG}"
      mcmgr_log "idle_agent_sync: dry-run created stub ${MC_MANAGER_CONFIG}"
    else
      mcmgr_log "idle_agent_sync: skip (no ${MC_MANAGER_CONFIG}; idle agent not installed)"
      return 0
    fi
  fi

  local py
  py="$(mcmgr_python)"
  "${py}" - "${GAME_MANIFEST}" "${RCON_SECRET}" "${MC_MANAGER_CONFIG}" <<'PY'
import json, sys

manifest_path, secret_path, config_path = sys.argv[1:4]
with open(manifest_path, encoding="utf-8") as f:
    man = json.load(f)
password = open(secret_path, encoding="utf-8").read().strip()
if not password:
    raise SystemExit("empty rcon secret")

with open(config_path, encoding="utf-8") as f:
    cfg = json.load(f)
if not isinstance(cfg, dict):
    cfg = {}

world = man.get("world_path")
unit = man.get("minecraft_unit")
rcon = man.get("rcon") if isinstance(man.get("rcon"), dict) else {}
port = rcon.get("port", 25575)

if not world:
    raise SystemExit("manifest missing world_path")
if not unit:
    raise SystemExit("manifest missing minecraft_unit")

cfg["world_path"] = world
cfg["minecraft_unit"] = unit
cfg["rcon_port"] = int(port)
cfg["rcon_password"] = password
cfg.setdefault("rcon_host", "127.0.0.1")

with open(config_path, "w", encoding="utf-8", newline="\n") as f:
    json.dump(cfg, f, indent=2)
    f.write("\n")
PY

  if [[ "${DRY_RUN}" != "1" ]]; then
    chown root:root "${MC_MANAGER_CONFIG}" 2>/dev/null || true
    chmod 0640 "${MC_MANAGER_CONFIG}" 2>/dev/null || true
  fi
  mcmgr_log "idle_agent_sync: patched ${MC_MANAGER_CONFIG} from game-manifest"
}
