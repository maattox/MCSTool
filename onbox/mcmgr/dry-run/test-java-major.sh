#!/usr/bin/env bash
# Offline: Fabric MC 26.x + JAVA_MAJOR=25 lands in manifest and systemd unit (pack-change path).
# shellcheck shell=bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
REPO_ROOT="$(cd "${ROOT}/../.." && pwd)"
FIXTURES="${MCMGR_FIXTURES_DIR:-${REPO_ROOT}/tests/fixtures/game-metadata}"
STAGING="${MCMGR_DRY_STAGING:-}"

if [[ -z "${STAGING}" ]]; then
  STAGING="$(mktemp -d "${TMPDIR:-/tmp}/mcmgr-java-major-XXXXXX")"
fi

export DRY_RUN=1
export MCMGR_ROOT="${STAGING}"
export MCMGR_FIXTURES_DIR="${FIXTURES}"
export EULA_ACCEPTED=true
export DISTRIBUTION=fabric
export MINECRAFT_VERSION=26.2
export JAVA_MAJOR=25
export MCMGR_INSTALLED_BY=dry_run

echo "[java-major-dry] staging=${STAGING}"
echo "[java-major-dry] fabric ${MINECRAFT_VERSION} JAVA_MAJOR=${JAVA_MAJOR}"

PY=""
if command -v python3 >/dev/null 2>&1; then PY=python3
elif command -v python >/dev/null 2>&1; then PY=python
else
  echo "java-major-dry: need python" >&2
  exit 1
fi

"${PY}" "${ROOT}/common/fabric_meta.py" self-test --fixtures "${FIXTURES}"
bash "${ROOT}/common/driver.sh"

MANIFEST="${STAGING}/etc/mcmgr/game-manifest.json"
UNIT="${STAGING}/etc/systemd/system/minecraft.service"

"${PY}" - "${MANIFEST}" "${UNIT}" <<'PY'
import json, sys

manifest_path, unit_path = sys.argv[1:3]
with open(manifest_path, encoding="utf-8") as f:
    doc = json.load(f)
with open(unit_path, encoding="utf-8") as f:
    unit = f.read()

assert doc["minecraft_version"] == "26.2", doc["minecraft_version"]
assert doc["java_major"] == 25, doc["java_major"]
assert "temurin-25" in doc["java"]["install_path"], doc["java"]
assert "temurin-25" in doc["launch_command"]["executable"], doc["launch_command"]
assert "temurin-25" in unit, unit
print("java-major-dry: manifest + unit OK", file=sys.stderr)
PY

echo "[java-major-dry] OK"
