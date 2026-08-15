#!/usr/bin/env bash
# Re-apply blueprint §5 ownership/mode contract (SETUP-ISSUE-4).
# Same functions as bootstrap layout — not an ad-hoc chmod.
# Usage (as root):
#   sudo bash /path/to/onbox/mcmgr/repair-permissions.sh
#   sudo bash /opt/mcmgr/bin/repair-permissions.sh
# shellcheck shell=bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

_layout_src=""
if [[ -f "${SCRIPT_DIR}/common/layout.sh" ]]; then
  _layout_src="${SCRIPT_DIR}/common/layout.sh"
elif [[ -f "${SCRIPT_DIR}/../lib/layout.sh" ]]; then
  _layout_src="${SCRIPT_DIR}/../lib/layout.sh"
elif [[ -f "${SCRIPT_DIR}/layout.sh" ]]; then
  _layout_src="${SCRIPT_DIR}/layout.sh"
else
  echo "[mcmgr] ERROR: cannot find layout.sh relative to ${SCRIPT_DIR}" >&2
  exit 1
fi

# shellcheck source=common/layout.sh
source "${_layout_src}"

if [[ "${DRY_RUN}" != "1" && "$(id -u)" -ne 0 ]]; then
  mcmgr_die "repair-permissions must run as root"
fi

layout_ensure_accounts
layout_apply
layout_verify

if [[ "${DRY_RUN}" != "1" ]]; then
  systemctl daemon-reload || true
  systemctl reset-failed "${MINECRAFT_UNIT}.service" 2>/dev/null || true
  mcmgr_log "repair-permissions: contract applied; unit not started (operator/driver decides)"
fi
