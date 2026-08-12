#!/usr/bin/env bash
# Offline dry-run: fixture-backed bootstrap into a temp MCMGR_ROOT (no apt/systemctl/live /opt).
# shellcheck shell=bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "${ROOT}/../.." && pwd)"
FIXTURES="${MCMGR_FIXTURES_DIR:-${REPO_ROOT}/tests/fixtures/game-metadata}"
STAGING="${MCMGR_DRY_STAGING:-}"
KEEP="${MCMGR_DRY_KEEP:-0}"

if [[ -z "${STAGING}" ]]; then
  STAGING="$(mktemp -d "${TMPDIR:-/tmp}/mcmgr-dry-XXXXXX")"
fi

export DRY_RUN=1
export MCMGR_ROOT="${STAGING}"
export MCMGR_FIXTURES_DIR="${FIXTURES}"
export EULA_ACCEPTED=true
export MINECRAFT_VERSION="${MINECRAFT_VERSION:-1.21.1}"
export DISTRIBUTION=vanilla
export MCMGR_INSTALLED_BY=dry_run

echo "[dry-run] staging=${STAGING}"
echo "[dry-run] fixtures=${FIXTURES}"
echo "[dry-run] version=${MINECRAFT_VERSION}"

bash "${ROOT}/common/driver.sh"

MANIFEST="${STAGING}/etc/mcmgr/game-manifest.json"
UNIT="${STAGING}/etc/systemd/system/minecraft.service"
SECRET="${STAGING}/etc/mcmgr/rcon.secret"
IDLE_CFG="${STAGING}/etc/mc-manager/config.json"

bash "${ROOT}/dry-run/assert-dry-run.sh" "${MANIFEST}" "${UNIT}" "${SECRET}" "${IDLE_CFG}"

echo "[dry-run] OK"
echo "[dry-run] manifest=${MANIFEST}"
echo "[dry-run] unit=${UNIT}"

if [[ "${KEEP}" != "1" ]]; then
  # Leave staging for inspection when KEEP=1; otherwise remove.
  rm -rf "${STAGING}"
  echo "[dry-run] cleaned staging (set MCMGR_DRY_KEEP=1 to retain)"
else
  echo "[dry-run] retained staging at ${STAGING}"
fi
