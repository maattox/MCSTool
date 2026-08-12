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

  # Minimal seam: if idle-agent config exists, keep rcon_password in sync.
  # Full world_path/minecraft_unit sync is Step 2.4.
  if [[ -f "${MC_MANAGER_CONFIG}" ]]; then
    local py
    py="$(mcmgr_python)"
    "${py}" - "${MC_MANAGER_CONFIG}" "${secret}" <<'PY'
import json, sys
path, password = sys.argv[1:3]
with open(path, encoding="utf-8") as f:
    cfg = json.load(f)
cfg["rcon_password"] = password
cfg.setdefault("rcon_host", "127.0.0.1")
cfg.setdefault("rcon_port", 25575)
with open(path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
    f.write("\n")
PY
    mcmgr_log "rcon: patched ${MC_MANAGER_CONFIG} rcon_password"
  fi

  export RCON_PASSWORD_REF="file:${RCON_SECRET}"
}
