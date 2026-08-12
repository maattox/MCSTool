#!/usr/bin/env bash
# Generate RCON secret and wire server.properties / idle-agent config (§8).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"
# shellcheck source=server_properties.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/server_properties.sh"

rcon_setup() {
  mkdir -p "${ETC_MCMGR}"
  local secret
  if [[ -f "${RCON_SECRET}" ]]; then
    secret="$(tr -d '\r\n' <"${RCON_SECRET}")"
    mcmgr_log "rcon: reusing existing ${RCON_SECRET}"
  else
    # 32 bytes base64, strip characters awkward in properties/shell.
    if command -v openssl >/dev/null 2>&1; then
      secret="$(openssl rand -base64 32 | tr -d '\n/+=\r' | head -c 40)"
    else
      secret="$("$(mcmgr_python)" -c 'import secrets; print(secrets.token_urlsafe(32))')"
    fi
    printf '%s\n' "${secret}" >"${RCON_SECRET}"
    if [[ "${DRY_RUN}" != "1" ]]; then
      chown root:root "${RCON_SECRET}"
      chmod 0600 "${RCON_SECRET}"
    else
      chmod 0600 "${RCON_SECRET}" || true
    fi
    mcmgr_log "rcon: generated ${RCON_SECRET}"
  fi

  server_properties_apply "${secret}"

  # Full idle-agent key sync (world_path / unit / port / password) runs after
  # manifest_write via idle_agent_sync.sh (§10.2). Password-only early patch removed.

  export RCON_PASSWORD_REF="file:${RCON_SECRET}"
}
