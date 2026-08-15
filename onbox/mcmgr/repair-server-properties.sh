#!/usr/bin/env bash
# Re-apply managed server.properties keys (SETUP-ISSUE-3).
# Same writer as bootstrap — not an ad-hoc sed.
# Usage (as root):
#   sudo bash /path/to/onbox/mcmgr/repair-server-properties.sh
#   sudo bash /opt/mcmgr/bin/repair-server-properties.sh
# shellcheck shell=bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

_props_src=""
if [[ -f "${SCRIPT_DIR}/common/server_properties.sh" ]]; then
  _props_src="${SCRIPT_DIR}/common/server_properties.sh"
elif [[ -f "${SCRIPT_DIR}/../lib/server_properties.sh" ]]; then
  _props_src="${SCRIPT_DIR}/../lib/server_properties.sh"
elif [[ -f "${SCRIPT_DIR}/server_properties.sh" ]]; then
  _props_src="${SCRIPT_DIR}/server_properties.sh"
else
  echo "[mcmgr] ERROR: cannot find server_properties.sh relative to ${SCRIPT_DIR}" >&2
  exit 1
fi

# shellcheck source=common/server_properties.sh
source "${_props_src}"

if [[ "${DRY_RUN}" != "1" && "$(id -u)" -ne 0 ]]; then
  mcmgr_die "repair-server-properties must run as root"
fi

[[ -f "${RCON_SECRET}" ]] || mcmgr_die "missing ${RCON_SECRET}"
secret="$(tr -d '\r\n' <"${RCON_SECRET}")"
[[ -n "${secret}" ]] || mcmgr_die "empty ${RCON_SECRET}"
server_properties_apply "${secret}"
mcmgr_log "repair-server-properties: managed keys applied"
