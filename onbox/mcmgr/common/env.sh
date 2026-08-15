#!/usr/bin/env bash
# Shared path / env defaults for mcmgr bootstrap.
# shellcheck shell=bash
set -euo pipefail

# Tool version stamped into game-manifest.json install.bootstrap_tool_version
export MCMGR_BOOTSTRAP_VERSION="${MCMGR_BOOTSTRAP_VERSION:-mcmgr-bootstrap/0.1.0}"

# When set (e.g. dry-run), all absolute roots are prefixed: $MCMGR_ROOT/opt/mcmgr/...
MCMGR_ROOT="${MCMGR_ROOT:-}"

_mcmgr_prefix() {
  local p="$1"
  if [[ -n "${MCMGR_ROOT}" ]]; then
    printf '%s%s' "${MCMGR_ROOT}" "${p}"
  else
    printf '%s' "${p}"
  fi
}

export OPT_MCMGR
export ETC_MCMGR
export VAR_MCMGR
export SERVER_DIR
export WORLD_PATH
export BIN_DIR
export BACKUPS_WORK
export GAME_MANIFEST
export RCON_SECRET
export BOOTSTRAP_STATE
export SYSTEMD_UNIT_PATH
export MC_MANAGER_CONFIG
export OPT_MC_MANAGER
export ETC_MC_MANAGER

OPT_MCMGR="$(_mcmgr_prefix /opt/mcmgr)"
ETC_MCMGR="$(_mcmgr_prefix /etc/mcmgr)"
VAR_MCMGR="$(_mcmgr_prefix /var/lib/mcmgr)"
SERVER_DIR="${OPT_MCMGR}/server"
WORLD_PATH="${SERVER_DIR}/world"
BIN_DIR="${OPT_MCMGR}/bin"
BACKUPS_WORK="${OPT_MCMGR}/backups-work"
GAME_MANIFEST="${ETC_MCMGR}/game-manifest.json"
RCON_SECRET="${ETC_MCMGR}/rcon.secret"
BOOTSTRAP_STATE="${VAR_MCMGR}/bootstrap-state.json"
SYSTEMD_UNIT_PATH="$(_mcmgr_prefix /etc/systemd/system/minecraft.service)"
MC_MANAGER_CONFIG="$(_mcmgr_prefix /etc/mc-manager/config.json)"
OPT_MC_MANAGER="$(_mcmgr_prefix /opt/mc-manager)"
ETC_MC_MANAGER="$(_mcmgr_prefix /etc/mc-manager)"

export DRY_RUN="${DRY_RUN:-0}"
export EULA_ACCEPTED="${EULA_ACCEPTED:-}"
export MINECRAFT_VERSION="${MINECRAFT_VERSION:-latest.release}"
export DISTRIBUTION="${DISTRIBUTION:-vanilla}"
export MCMGR_FIXTURES_DIR="${MCMGR_FIXTURES_DIR:-}"
export JVM_XMS="${JVM_XMS:-2G}"
export JVM_XMX="${JVM_XMX:-4G}"
export MINECRAFT_UNIT="${MINECRAFT_UNIT:-minecraft}"

# Directory containing this file's parent (onbox/mcmgr)
_MCMGR_COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export MCMGR_HOME
MCMGR_HOME="$(cd "${_MCMGR_COMMON_DIR}/.." && pwd)"

mcmgr_log() { printf '[mcmgr] %s\n' "$*" >&2; }
mcmgr_die() { mcmgr_log "ERROR: $*"; exit 1; }

mcmgr_need_cmd() {
  command -v "$1" >/dev/null 2>&1 || mcmgr_die "required command not found: $1"
}

# Prefer python3, fall back to python (Windows Git Bash / some images).
mcmgr_python() {
  if command -v python3 >/dev/null 2>&1; then
    command -v python3
  elif command -v python >/dev/null 2>&1; then
    command -v python
  else
    mcmgr_die "python3/python required for JSON helpers"
  fi
}
