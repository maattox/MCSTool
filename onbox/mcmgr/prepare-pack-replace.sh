#!/usr/bin/env bash
# Full pack-replace prepare (blueprint §28.1 full path / §12.2–§12.3).
# Stops Minecraft, clears loader + pack files, keeps identity/RCON and optionally the world.
# Does not reinstall — Setup/Manager then runs driver.sh + pack copy.
# Usage (as root, or DRY_RUN=1):
#   KEEP_WORLD=1 sudo -E bash /path/to/onbox/mcmgr/prepare-pack-replace.sh
#   WIPE_WORLD=1 sudo -E bash /opt/mcmgr/bin/prepare-pack-replace.sh
# shellcheck shell=bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

_env_src=""
if [[ -f "${SCRIPT_DIR}/common/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/common/env.sh"
elif [[ -f "${SCRIPT_DIR}/../lib/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/../lib/env.sh"
elif [[ -f "${SCRIPT_DIR}/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/env.sh"
else
  echo "[mcmgr] ERROR: cannot find env.sh relative to ${SCRIPT_DIR}" >&2
  exit 1
fi

# shellcheck source=common/env.sh
source "${_env_src}"

KEEP_WORLD="${KEEP_WORLD:-1}"
WIPE_WORLD="${WIPE_WORLD:-0}"
if [[ "${WIPE_WORLD}" == "1" ]]; then
  KEEP_WORLD=0
fi

STASH="${VAR_MCMGR}/pack-replace-stash"

_IDENTITY_NAMES=(
  eula.txt
  server.properties
  whitelist.json
  ops.json
  banned-ips.json
  banned-players.json
  usercache.json
  usernamecache.json
)

_WORLD_NAMES=(
  world
  world_nether
  world_the_end
)

if [[ "${DRY_RUN}" != "1" && "$(id -u)" -ne 0 ]]; then
  mcmgr_die "prepare-pack-replace must run as root (or use DRY_RUN=1)"
fi

_restore_leftover_stash() {
  [[ -d "${STASH}" ]] || return 0
  mcmgr_log "prepare-pack-replace: restoring leftover stash from a prior run"
  mkdir -p "${SERVER_DIR}"
  local item
  for item in "${STASH}"/* "${STASH}"/.[!.]* "${STASH}"/..?*; do
    [[ -e "${item}" ]] || continue
    mv -- "${item}" "${SERVER_DIR}/"
  done
  rm -rf "${STASH}"
}

_stash_named() {
  local dir="$1"
  shift
  local name
  for name in "$@"; do
    local src="${SERVER_DIR}/${name}"
    if [[ -e "${src}" ]]; then
      mkdir -p "${dir}"
      mv -- "${src}" "${dir}/${name}"
    fi
  done
}

_restore_stash() {
  [[ -d "${STASH}" ]] || return 0
  mkdir -p "${SERVER_DIR}"
  local item
  for item in "${STASH}"/* "${STASH}"/.[!.]* "${STASH}"/..?*; do
    [[ -e "${item}" ]] || continue
    mv -- "${item}" "${SERVER_DIR}/"
  done
  rm -rf "${STASH}"
}

_clear_server_dir() {
  mkdir -p "${SERVER_DIR}"
  if [[ -d "${SERVER_DIR}" ]]; then
    find "${SERVER_DIR}" -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +
  fi
}

main() {
  export HOME="${HOME:-/home/ubuntu}"
  mkdir -p "${SERVER_DIR}" "${ETC_MCMGR}" "${VAR_MCMGR}"

  _restore_leftover_stash

  if [[ "${DRY_RUN}" != "1" ]]; then
    systemctl stop "${MINECRAFT_UNIT}.service" || true
  fi

  if [[ -f "${GAME_MANIFEST}" ]]; then
    cp -f "${GAME_MANIFEST}" "${ETC_MCMGR}/game-manifest.previous.json"
    mcmgr_log "prepare-pack-replace: snapshot ${ETC_MCMGR}/game-manifest.previous.json"
  fi

  rm -rf "${STASH}"
  mkdir -p "${STASH}"
  _stash_named "${STASH}" "${_IDENTITY_NAMES[@]}"
  if [[ "${KEEP_WORLD}" == "1" ]]; then
    _stash_named "${STASH}" "${_WORLD_NAMES[@]}"
    mcmgr_log "prepare-pack-replace: keeping world under ${WORLD_PATH}"
  else
    mcmgr_log "prepare-pack-replace: wiping world folders (WIPE_WORLD=1)"
  fi

  _clear_server_dir
  _restore_stash

  rm -f "${BOOTSTRAP_STATE}"
  # Leave rcon.secret, /opt/mcmgr bin+lib, and idle-agent config alone.

  mcmgr_log "prepare-pack-replace: server dir cleared for bootstrap (identity kept, rcon untouched)"
  mcmgr_log "  server_dir=${SERVER_DIR}"
  mcmgr_log "  keep_world=${KEEP_WORLD}"
}

main "$@"
