#!/usr/bin/env bash
# Fabric installer module — meta.fabricmc.net v2 three-axis launcher jar (§18).
# Does NOT write systemd, properties, EULA, or the final game-manifest (shared driver).
# No pack import — loader jar only. No checksum is published (none_published).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=../common/env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../common" && pwd)/env.sh"

FABRIC_META_BASE="${FABRIC_META_BASE:-https://meta.fabricmc.net}"
FABRIC_META_USER_AGENT="${FABRIC_META_USER_AGENT:-mcmgr-bootstrap/0.1.0 (https://github.com/maattox/oci-mc-server)}"
FABRIC_META_PY="${MCMGR_HOME}/common/fabric_meta.py"
FABRIC_LOADER_VERSION="${FABRIC_LOADER_VERSION:-}"
FABRIC_INSTALLER_VERSION="${FABRIC_INSTALLER_VERSION:-}"

# Exports for driver:
#   RESOLVED_MC_VERSION, ARTIFACT_FILENAME, ARTIFACT_DOWNLOAD_URL,
#   ARTIFACT_HASH_ALG, ARTIFACT_HASH_VALUE, ARTIFACT_HASH_VERIFIED_AT,
#   ARTIFACT_KIND, LOADER_VERSION, FABRIC_INSTALLER_VERSION_RESOLVED,
#   FABRIC_JAVA_MAJOR

fabric_curl() {
  curl -fsSL -A "${FABRIC_META_USER_AGENT}" "$@"
}

fabric_resolve_want() {
  local want="${MINECRAFT_VERSION}"
  case "${want}" in
    latest|latest.release|latest.snapshot)
      mcmgr_die "Fabric installer needs a concrete Minecraft version (got ${want})"
      ;;
  esac
  printf '%s\n' "${want}"
}

fabric_resolve_and_place() {
  local py
  py="$(mcmgr_python)"
  [[ -f "${FABRIC_META_PY}" ]] || mcmgr_die "missing ${FABRIC_META_PY}"
  if [[ -z "${MCMGR_FIXTURES_DIR}" ]]; then
    mcmgr_need_cmd curl
  fi

  local want
  want="$(fabric_resolve_want)"

  local installer_json loader_json
  if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
    installer_json="${MCMGR_FIXTURES_DIR}/fabric-meta-installer.json"
    loader_json="${MCMGR_FIXTURES_DIR}/fabric-meta-loader-${want}.json"
    [[ -f "${installer_json}" ]] || mcmgr_die "fixture missing: ${installer_json}"
    [[ -f "${loader_json}" ]] || mcmgr_die "fixture missing: ${loader_json}"
  else
    installer_json="$(mktemp)"
    loader_json="$(mktemp)"
    fabric_curl -o "${installer_json}" "${FABRIC_META_BASE}/v2/versions/installer"
    fabric_curl -o "${loader_json}" "${FABRIC_META_BASE}/v2/versions/loader/${want}"
  fi

  local resolve_args=("${FABRIC_META_PY}" resolve "${installer_json}" "${loader_json}" "${want}")
  if [[ -n "${FABRIC_LOADER_VERSION}" ]]; then
    resolve_args+=(--loader-version "${FABRIC_LOADER_VERSION}")
  fi
  if [[ -n "${FABRIC_INSTALLER_VERSION}" ]]; then
    resolve_args+=(--installer-version "${FABRIC_INSTALLER_VERSION}")
  fi
  local resolved_json
  resolved_json="$("${py}" "${resolve_args[@]}")"

  local filename url loader_ver installer_ver java_major
  local _fields
  _fields="$(
    "${py}" - "${resolved_json}" <<'PY'
import json, sys
d = json.loads(sys.argv[1])
print(d["minecraft_version"])
print(d["filename"])
print(d["download_url"])
print(d["loader_version"])
print(d["installer_version"])
print(int(d["java_major"]))
PY
  )"
  want="$(printf '%s\n' "${_fields}" | sed -n '1p')"
  filename="$(printf '%s\n' "${_fields}" | sed -n '2p')"
  url="$(printf '%s\n' "${_fields}" | sed -n '3p')"
  loader_ver="$(printf '%s\n' "${_fields}" | sed -n '4p')"
  installer_ver="$(printf '%s\n' "${_fields}" | sed -n '5p')"
  java_major="$(printf '%s\n' "${_fields}" | sed -n '6p')"
  want="${want%$'\r'}"
  filename="${filename%$'\r'}"
  url="${url%$'\r'}"
  loader_ver="${loader_ver%$'\r'}"
  installer_ver="${installer_ver%$'\r'}"
  java_major="${java_major%$'\r'}"

  mkdir -p "${SERVER_DIR}"
  local dest="${SERVER_DIR}/${filename}"

  if [[ "${DRY_RUN}" == "1" ]]; then
    printf 'dry-run-placeholder-fabric-launcher\n' >"${dest}"
    mcmgr_log "fabric: dry-run placeholder jar at ${dest} (none_published)"
  else
    local tmp="${dest}.part"
    local attempt=0
    local ok=0
    while [[ "${attempt}" -lt 2 ]]; do
      attempt=$((attempt + 1))
      if fabric_curl -o "${tmp}" "${url}"; then
        ok=1
        break
      fi
      mcmgr_log "fabric: download failed attempt=${attempt}"
      rm -f "${tmp}"
    done
    [[ "${ok}" == "1" ]] || mcmgr_die "Fabric launcher jar download failed after retry"
    # §18.2: do not sha256 locally and call it verified.
    mv -f "${tmp}" "${dest}"
    chown mcmgr:mcmgr "${dest}" 2>/dev/null || true
    chmod 0640 "${dest}"
  fi

  export RESOLVED_MC_VERSION="${want}"
  export ARTIFACT_FILENAME="${filename}"
  export ARTIFACT_DOWNLOAD_URL="${url}"
  export ARTIFACT_KIND="launcher_jar"
  export ARTIFACT_HASH_ALG="none_published"
  export ARTIFACT_HASH_VALUE=""
  export ARTIFACT_HASH_VERIFIED_AT=""
  export LOADER_VERSION="${loader_ver}"
  export FABRIC_INSTALLER_VERSION_RESOLVED="${installer_ver}"
  export FABRIC_JAVA_MAJOR="${java_major}"
  mcmgr_log "fabric: ${want} loader=${loader_ver} installer=${installer_ver} jar=${filename} java_major=${java_major}"
}
