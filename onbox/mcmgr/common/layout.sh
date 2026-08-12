#!/usr/bin/env bash
# Create mcmgr user/group and product directory tree (§5).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

layout_create() {
  mcmgr_log "layout: ensuring directory tree under ${OPT_MCMGR}"

  if [[ "${DRY_RUN}" == "1" ]]; then
    mkdir -p \
      "${SERVER_DIR}" \
      "${WORLD_PATH}" \
      "${BIN_DIR}" \
      "${BACKUPS_WORK}" \
      "${ETC_MCMGR}" \
      "${VAR_MCMGR}" \
      "$(dirname "${SYSTEMD_UNIT_PATH}")"
    # Dry-run: skip useradd; ownership stays as invoking user.
    return 0
  fi

  if ! getent group mcmgr >/dev/null 2>&1; then
    groupadd --system mcmgr
  fi
  if ! id -u mcmgr >/dev/null 2>&1; then
    useradd --system --home-dir "${SERVER_DIR}" --shell /usr/sbin/nologin --gid mcmgr mcmgr
  fi

  mkdir -p \
    "${OPT_MCMGR}" \
    "${SERVER_DIR}" \
    "${WORLD_PATH}" \
    "${BIN_DIR}" \
    "${BACKUPS_WORK}" \
    "${ETC_MCMGR}" \
    "${VAR_MCMGR}"

  chown root:mcmgr "${OPT_MCMGR}"
  chmod 0750 "${OPT_MCMGR}"
  chown mcmgr:mcmgr "${SERVER_DIR}" "${WORLD_PATH}" "${BACKUPS_WORK}"
  chmod 0750 "${SERVER_DIR}" "${WORLD_PATH}" "${BACKUPS_WORK}"
  chown root:mcmgr "${ETC_MCMGR}"
  chmod 0750 "${ETC_MCMGR}"
  chown root:root "${VAR_MCMGR}"
  chmod 0750 "${VAR_MCMGR}"
  chown root:mcmgr "${BIN_DIR}"
  chmod 0750 "${BIN_DIR}"
}
