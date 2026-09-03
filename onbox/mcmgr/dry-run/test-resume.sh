#!/usr/bin/env bash
# Second driver pass against an existing dry-run tree (completed stages skip).
# Catches SETUP-ISSUE-16: skip artifact_placed left RESOLVED_MC_VERSION unset.
# shellcheck shell=bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "${ROOT}/../.." && pwd)"
FIXTURES="${MCMGR_FIXTURES_DIR:-${REPO_ROOT}/tests/fixtures/game-metadata}"
STAGING="$(mktemp -d "${TMPDIR:-/tmp}/mcmgr-resume-XXXXXX")"

export DRY_RUN=1
export MCMGR_ROOT="${STAGING}"
export MCMGR_FIXTURES_DIR="${FIXTURES}"
export EULA_ACCEPTED=true
export DISTRIBUTION=forge
export MINECRAFT_VERSION=1.12.2
export MCMGR_INSTALLED_BY=dry_run_resume

echo "[resume-dry] staging=${STAGING}"
bash "${ROOT}/common/driver.sh"

STATE="${STAGING}/var/lib/mcmgr/bootstrap-state.json"
[[ -f "${STATE}" ]] || { echo "missing bootstrap-state after first pass"; exit 1; }

echo "[resume-dry] second pass (expect skip completed stages)"
LOG="${STAGING}/resume-second.log"
if ! bash "${ROOT}/common/driver.sh" >"${LOG}" 2>&1; then
  echo "[resume-dry] FAIL: second driver pass exited non-zero"
  cat "${LOG}"
  rm -rf "${STAGING}"
  exit 1
fi
cat "${LOG}"
grep -F "skip completed stage: artifact_placed" "${LOG}" >/dev/null \
  || { echo "expected skip of artifact_placed"; rm -rf "${STAGING}"; exit 1; }
grep -F "RESOLVED_MC_VERSION: unbound variable" "${LOG}" >/dev/null && {
  echo "unbound RESOLVED_MC_VERSION on resume"
  rm -rf "${STAGING}"
  exit 1
}
grep -F "SUCCESS: Forge" "${LOG}" >/dev/null \
  || { echo "expected SUCCESS on resume"; rm -rf "${STAGING}"; exit 1; }
grep -F "resume: restored resolve/java env from" "${LOG}" >/dev/null \
  || { echo "expected manifest restore log"; rm -rf "${STAGING}"; exit 1; }

MANIFEST="${STAGING}/etc/mcmgr/game-manifest.json"
UNIT="${STAGING}/etc/systemd/system/minecraft.service"
SECRET="${STAGING}/etc/mcmgr/rcon.secret"
IDLE_CFG="${STAGING}/etc/mc-manager/config.json"
PROPS="${STAGING}/opt/mcmgr/server/server.properties"
bash "${ROOT}/dry-run/assert-dry-run.sh" "${MANIFEST}" "${UNIT}" "${SECRET}" "${IDLE_CFG}" "${PROPS}"

rm -rf "${STAGING}"
echo "[resume-dry] OK"
