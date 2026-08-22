#!/usr/bin/env bash
# Offline: java_install surfaces a clear error when Temurin cannot be installed.
# shellcheck shell=bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# shellcheck source=../common/env.sh
source "${ROOT}/common/env.sh"
# shellcheck source=../common/java.sh
source "${ROOT}/common/java.sh"

export DRY_RUN=0
export MCMGR_JAVA_INSTALL_FAIL=1

set +e
output="$(java_install 25 2>&1)"
status=$?
set -e

if [[ "${status}" -eq 0 ]]; then
  echo "java-install-fail: expected non-zero exit" >&2
  exit 1
fi

if ! grep -q 'This pack needs Java 25, and the installer could not provide it.' <<<"${output}"; then
  echo "java-install-fail: missing expected copy" >&2
  echo "${output}" >&2
  exit 1
fi

echo "[java-install-fail] OK"
