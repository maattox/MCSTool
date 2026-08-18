#!/usr/bin/env bash
# Paper installer module — Fill v3 STABLE resolve → download → sha256 → place jar (§17).
# Does NOT write systemd, properties, EULA, or the final game-manifest (shared driver).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=../common/env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../common" && pwd)/env.sh"

FILL_V3_PROJECT_URL="${FILL_V3_PROJECT_URL:-https://fill.papermc.io/v3/projects/paper}"
PAPER_FILL_USER_AGENT="${PAPER_FILL_USER_AGENT:-mcmgr-bootstrap/0.1.0 (https://github.com/maattox/oci-mc-server)}"
PAPER_FILL_PY="${MCMGR_HOME}/common/paper_fill_v3.py"

# Exports for driver:
#   RESOLVED_MC_VERSION, ARTIFACT_FILENAME, ARTIFACT_DOWNLOAD_URL,
#   ARTIFACT_HASH_ALG, ARTIFACT_HASH_VALUE, ARTIFACT_HASH_VERIFIED_AT,
#   PAPER_JAVA_MAJOR, PAPER_JVM_FLAGS_JSON

paper_curl() {
  curl -fsSL -A "${PAPER_FILL_USER_AGENT}" "$@"
}

paper_sha256() {
  local file="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "${file}" | awk '{print $1}'
  else
    "$(mcmgr_python)" - "${file}" <<'PY'
import hashlib, sys
h = hashlib.sha256()
with open(sys.argv[1], "rb") as f:
    for chunk in iter(lambda: f.read(1024 * 1024), b""):
        h.update(chunk)
print(h.hexdigest())
PY
  fi
}

paper_resolve_want() {
  local want="${MINECRAFT_VERSION}"
  local py
  py="$(mcmgr_python)"
  case "${want}" in
    latest|latest.release|latest.stable)
      local project_json
      if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
        project_json="${MCMGR_FIXTURES_DIR}/paper-fill-v3-project.json"
        [[ -f "${project_json}" ]] || mcmgr_die "fixture missing: ${project_json}"
      else
        mcmgr_need_cmd curl
        project_json="$(mktemp)"
        paper_curl -o "${project_json}" "${FILL_V3_PROJECT_URL}"
      fi
      want="$("${py}" "${PAPER_FILL_PY}" default-version "${project_json}")"
      ;;
    latest.snapshot)
      mcmgr_die "Paper installer does not install snapshots; pick a STABLE Minecraft version"
      ;;
  esac
  printf '%s\n' "${want}"
}

paper_resolve_and_place() {
  local py
  py="$(mcmgr_python)"
  [[ -f "${PAPER_FILL_PY}" ]] || mcmgr_die "missing ${PAPER_FILL_PY}"
  if [[ -z "${MCMGR_FIXTURES_DIR}" ]]; then
    mcmgr_need_cmd curl
  fi

  local want
  want="$(paper_resolve_want)"

  local builds_json version_json=""
  if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
    builds_json="${MCMGR_FIXTURES_DIR}/paper-fill-v3-builds-${want}.json"
    [[ -f "${builds_json}" ]] || mcmgr_die "fixture missing: ${builds_json}"
    if [[ -f "${MCMGR_FIXTURES_DIR}/paper-fill-v3-version-${want}.json" ]]; then
      version_json="${MCMGR_FIXTURES_DIR}/paper-fill-v3-version-${want}.json"
    fi
  else
    builds_json="$(mktemp)"
    paper_curl -o "${builds_json}" "${FILL_V3_PROJECT_URL}/versions/${want}/builds"
    version_json="$(mktemp)"
    if ! paper_curl -o "${version_json}" "${FILL_V3_PROJECT_URL}/versions/${want}"; then
      rm -f "${version_json}"
      version_json=""
    fi
  fi

  local resolved_json
  local resolve_args=("${PAPER_FILL_PY}" resolve "${builds_json}" "${want}")
  if [[ -n "${version_json}" ]]; then
    resolve_args+=(--version-json "${version_json}")
  fi
  resolved_json="$("${py}" "${resolve_args[@]}")"

  local filename url sha256 java_major flags_json
  local _fields
  _fields="$(
    "${py}" - "${resolved_json}" <<'PY'
import json, sys
d = json.loads(sys.argv[1])
print(d["minecraft_version"])
print(d["filename"])
print(d["download_url"])
print(d["sha256"])
print(int(d["java_major"]))
print(json.dumps(d["jvm_flags"]))
PY
  )"
  want="$(printf '%s\n' "${_fields}" | sed -n '1p')"
  filename="$(printf '%s\n' "${_fields}" | sed -n '2p')"
  url="$(printf '%s\n' "${_fields}" | sed -n '3p')"
  sha256="$(printf '%s\n' "${_fields}" | sed -n '4p')"
  java_major="$(printf '%s\n' "${_fields}" | sed -n '5p')"
  flags_json="$(printf '%s\n' "${_fields}" | sed -n '6p')"
  want="${want%$'\r'}"
  filename="${filename%$'\r'}"
  url="${url%$'\r'}"
  sha256="${sha256%$'\r'}"
  java_major="${java_major%$'\r'}"
  flags_json="${flags_json%$'\r'}"

  mkdir -p "${SERVER_DIR}"
  local dest="${SERVER_DIR}/${filename}"
  local verified_at
  verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  if [[ "${DRY_RUN}" == "1" ]]; then
    printf 'dry-run-placeholder-paper-jar\n' >"${dest}"
    mcmgr_log "paper: dry-run placeholder jar at ${dest} (expected sha256=${sha256})"
  else
    local tmp="${dest}.part"
    local attempt=0
    local ok=0
    while [[ "${attempt}" -lt 2 ]]; do
      attempt=$((attempt + 1))
      paper_curl -o "${tmp}" "${url}"
      local got
      got="$(paper_sha256 "${tmp}")"
      if [[ "${got}" == "${sha256}" ]]; then
        ok=1
        break
      fi
      mcmgr_log "paper: sha256 mismatch (got ${got}, want ${sha256}) attempt=${attempt}"
      rm -f "${tmp}"
    done
    [[ "${ok}" == "1" ]] || mcmgr_die "Paper jar failed integrity check after retry"
    mv -f "${tmp}" "${dest}"
    chown mcmgr:mcmgr "${dest}" 2>/dev/null || true
    chmod 0640 "${dest}"
    verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  fi

  export RESOLVED_MC_VERSION="${want}"
  export ARTIFACT_FILENAME="${filename}"
  export ARTIFACT_DOWNLOAD_URL="${url}"
  export ARTIFACT_HASH_ALG="sha256"
  export ARTIFACT_HASH_VALUE="${sha256}"
  export ARTIFACT_HASH_VERIFIED_AT="${verified_at}"
  export PAPER_JAVA_MAJOR="${java_major}"
  export PAPER_JVM_FLAGS_JSON="${flags_json}"
  mcmgr_log "paper: ${want} jar=${filename} java_major=${java_major}"
}
