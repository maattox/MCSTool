#!/usr/bin/env bash
# Write eula.txt after operator acceptance (§7.2).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

eula_write() {
  local mc_version="${1:?minecraft version}"
  if [[ "${EULA_ACCEPTED}" != "true" && "${EULA_ACCEPTED}" != "1" ]]; then
    mcmgr_die "EULA_ACCEPTED must be true (operator must accept Mojang EULA in Setup)"
  fi
  local stamp
  stamp="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  mkdir -p "${SERVER_DIR}"
  cat >"${SERVER_DIR}/eula.txt" <<EOF
#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).
#${stamp}
eula=true
EOF
  if [[ "${DRY_RUN}" != "1" ]]; then
    chown mcmgr:mcmgr "${SERVER_DIR}/eula.txt" 2>/dev/null || true
    chmod 0640 "${SERVER_DIR}/eula.txt"
  fi
  export EULA_ACCEPTED_AT="${stamp}"
  export EULA_ACCEPTED_VERSION_CONTEXT="${mc_version}"
  mcmgr_log "eula: accepted at ${stamp} for ${mc_version}"
}
