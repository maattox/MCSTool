#!/usr/bin/env bash
# Layer 3 crash quarantine wrapper. Sources env.sh so DRY_RUN / MCMGR_ROOT work.
# Usage:
#   sudo bash /opt/mcmgr/bin/quarantine_mod.sh move --mod-id examplemod --restart
#   sudo bash /opt/mcmgr/bin/quarantine_mod.sh restore --path mods/examplemod-1.0.jar --restart
# shellcheck shell=bash
set -euo pipefail

HOME="${HOME:-/home/ubuntu}"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

_env_src=""
_py_src=""
if [[ -f "${SCRIPT_DIR}/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/env.sh"
  _py_src="${SCRIPT_DIR}/quarantine_mod.py"
elif [[ -f "${SCRIPT_DIR}/../lib/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/../lib/env.sh"
  _py_src="${SCRIPT_DIR}/../lib/quarantine_mod.py"
elif [[ -f "${SCRIPT_DIR}/../common/env.sh" ]]; then
  _env_src="${SCRIPT_DIR}/../common/env.sh"
  _py_src="${SCRIPT_DIR}/../common/quarantine_mod.py"
else
  echo '{"ok":false,"error":"cannot find env.sh"}' >&2
  exit 1
fi

# shellcheck source=env.sh
source "${_env_src}"

py="$(mcmgr_python)"
export MCMGR_ROOT="${MCMGR_ROOT:-}"
exec "${py}" "${_py_src}" --server-dir "${SERVER_DIR}" --manifest "${GAME_MANIFEST}" "$@"
