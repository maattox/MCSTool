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
export DISTRIBUTION="${DISTRIBUTION:-vanilla}"
if [[ "${DISTRIBUTION}" == "paper" ]]; then
  export MINECRAFT_VERSION="${MINECRAFT_VERSION:-1.21.10}"
elif [[ "${DISTRIBUTION}" == "fabric" ]]; then
  export MINECRAFT_VERSION="${MINECRAFT_VERSION:-1.21.8}"
elif [[ "${DISTRIBUTION}" == "neoforge" ]]; then
  export MINECRAFT_VERSION="${MINECRAFT_VERSION:-1.21.1}"
else
  export MINECRAFT_VERSION="${MINECRAFT_VERSION:-1.21.1}"
fi
export MCMGR_INSTALLED_BY=dry_run

echo "[dry-run] staging=${STAGING}"
echo "[dry-run] fixtures=${FIXTURES}"
echo "[dry-run] distribution=${DISTRIBUTION}"
echo "[dry-run] version=${MINECRAFT_VERSION}"

if [[ "${DISTRIBUTION}" == "paper" || "${DISTRIBUTION}" == "fabric" || "${DISTRIBUTION}" == "neoforge" ]]; then
  PY=""
  if command -v python3 >/dev/null 2>&1; then PY=python3
  elif command -v python >/dev/null 2>&1; then PY=python
  else
    echo "dry-run: need python for meta self-test" >&2
    exit 1
  fi
  if [[ "${DISTRIBUTION}" == "paper" ]]; then
    "${PY}" "${ROOT}/common/paper_fill_v3.py" self-test --fixtures "${FIXTURES}"
  elif [[ "${DISTRIBUTION}" == "fabric" ]]; then
    "${PY}" "${ROOT}/common/fabric_meta.py" self-test --fixtures "${FIXTURES}"
  else
    "${PY}" "${ROOT}/common/neoforge_meta.py" self-test --fixtures "${FIXTURES}"
  fi
fi

bash "${ROOT}/common/driver.sh"

MANIFEST="${STAGING}/etc/mcmgr/game-manifest.json"
UNIT="${STAGING}/etc/systemd/system/minecraft.service"
SECRET="${STAGING}/etc/mcmgr/rcon.secret"
IDLE_CFG="${STAGING}/etc/mc-manager/config.json"
PROPS="${STAGING}/opt/mcmgr/server/server.properties"

bash "${ROOT}/dry-run/assert-dry-run.sh" "${MANIFEST}" "${UNIT}" "${SECRET}" "${IDLE_CFG}" "${PROPS}"

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
