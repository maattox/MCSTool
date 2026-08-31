#!/usr/bin/env bash
# NeoForge installer module — Maven XML metadata + --installServer argfile tree (§19).
# Does NOT write systemd, properties, EULA, or the final game-manifest (shared driver).
# No pack import. No checksum is published (none_published).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=../common/env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../common" && pwd)/env.sh"

NEOFORGE_MAVEN_BASE="${NEOFORGE_MAVEN_BASE:-https://maven.neoforged.net/releases/net/neoforged/neoforge}"
NEOFORGE_USER_AGENT="${NEOFORGE_USER_AGENT:-mcmgr-bootstrap/0.1.0 (https://github.com/maattox/MCSTool)}"
NEOFORGE_META_PY="${MCMGR_HOME}/common/neoforge_meta.py"
NEOFORGE_VERSION="${NEOFORGE_VERSION:-}"
NEOFORGE_CURL_MAX_TIME="${NEOFORGE_CURL_MAX_TIME:-45}"

# Exports for driver:
#   RESOLVED_MC_VERSION, INSTALLER_FILENAME, INSTALLER_DOWNLOAD_URL,
#   UNIX_ARGS_PATH, ARTIFACT_KIND, ARTIFACT_HASH_ALG, ARTIFACT_HASH_VALUE,
#   ARTIFACT_HASH_VERIFIED_AT, LOADER_VERSION, NEOFORGE_JAVA_MAJOR,
#   ARTIFACT_FILENAME (installer jar, layout verify)

neoforge_curl() {
  curl -fsSL --max-time "${NEOFORGE_CURL_MAX_TIME}" -A "${NEOFORGE_USER_AGENT}" "$@"
}

neoforge_resolve_want() {
  local want="${MINECRAFT_VERSION}"
  case "${want}" in
    latest|latest.release|latest.snapshot)
      mcmgr_die "NeoForge installer needs a concrete Minecraft version (got ${want})"
      ;;
  esac
  printf '%s\n' "${want}"
}

neoforge_fetch_metadata() {
  local dest="$1"
  local url="${NEOFORGE_MAVEN_BASE}/maven-metadata.xml"
  local attempt=0
  local ok=0
  while [[ "${attempt}" -lt 3 ]]; do
    attempt=$((attempt + 1))
    if neoforge_curl -o "${dest}" "${url}"; then
      ok=1
      break
    fi
    mcmgr_log "neoforge: maven-metadata fetch failed attempt=${attempt}"
    rm -f "${dest}"
  done
  [[ "${ok}" == "1" ]] || mcmgr_die "could not reach maven.neoforged.net (metadata GET failed after retry)"
}

neoforge_resolve_and_place() {
  local py
  py="$(mcmgr_python)"
  [[ -f "${NEOFORGE_META_PY}" ]] || mcmgr_die "missing ${NEOFORGE_META_PY}"
  if [[ -z "${MCMGR_FIXTURES_DIR}" ]]; then
    mcmgr_need_cmd curl
  fi

  local want
  want="$(neoforge_resolve_want)"

  local metadata_xml
  if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
    metadata_xml="${MCMGR_FIXTURES_DIR}/neoforge-maven-metadata.xml"
    [[ -f "${metadata_xml}" ]] || mcmgr_die "fixture missing: ${metadata_xml}"
  else
    metadata_xml="$(mktemp)"
    neoforge_fetch_metadata "${metadata_xml}"
  fi

  local resolve_args=("${NEOFORGE_META_PY}" resolve "${metadata_xml}" "${want}")
  if [[ -n "${NEOFORGE_VERSION}" ]]; then
    resolve_args+=(--neoforge-version "${NEOFORGE_VERSION}")
  fi
  local resolved_json
  if ! resolved_json="$("${py}" "${resolve_args[@]}")"; then
    mcmgr_die "NeoForge metadata resolve failed (see maven.neoforged.net / XML parse errors above)"
  fi

  local filename url loader_ver unix_path java_major
  local _fields
  _fields="$(
    "${py}" - "${resolved_json}" <<'PY'
import json, sys
d = json.loads(sys.argv[1])
print(d["minecraft_version"])
print(d["installer_filename"])
print(d["installer_download_url"])
print(d["loader_version"])
print(d["unix_args_path"])
print(int(d["java_major"]))
PY
  )"
  want="$(printf '%s\n' "${_fields}" | sed -n '1p')"
  filename="$(printf '%s\n' "${_fields}" | sed -n '2p')"
  url="$(printf '%s\n' "${_fields}" | sed -n '3p')"
  loader_ver="$(printf '%s\n' "${_fields}" | sed -n '4p')"
  unix_path="$(printf '%s\n' "${_fields}" | sed -n '5p')"
  java_major="$(printf '%s\n' "${_fields}" | sed -n '6p')"
  want="${want%$'\r'}"
  filename="${filename%$'\r'}"
  url="${url%$'\r'}"
  loader_ver="${loader_ver%$'\r'}"
  unix_path="${unix_path%$'\r'}"
  java_major="${java_major%$'\r'}"

  mkdir -p "${SERVER_DIR}"
  local dest="${SERVER_DIR}/${filename}"

  if [[ "${DRY_RUN}" == "1" ]]; then
    printf 'dry-run-placeholder-neoforge-installer\n' >"${dest}"
    mcmgr_log "neoforge: dry-run placeholder installer at ${dest} (none_published)"
  else
    local tmp="${dest}.part"
    local attempt=0
    local ok=0
    while [[ "${attempt}" -lt 3 ]]; do
      attempt=$((attempt + 1))
      if neoforge_curl -o "${tmp}" "${url}"; then
        ok=1
        break
      fi
      mcmgr_log "neoforge: installer download failed attempt=${attempt}"
      rm -f "${tmp}"
    done
    [[ "${ok}" == "1" ]] || mcmgr_die "could not reach maven.neoforged.net (installer jar download failed after retry)"
    mv -f "${tmp}" "${dest}"
    chown mcmgr:mcmgr "${dest}" 2>/dev/null || true
    chmod 0640 "${dest}"
  fi

  export RESOLVED_MC_VERSION="${want}"
  export ARTIFACT_FILENAME="${filename}"
  export ARTIFACT_DOWNLOAD_URL="${url}"
  export INSTALLER_FILENAME="${filename}"
  export INSTALLER_DOWNLOAD_URL="${url}"
  export UNIX_ARGS_PATH="${unix_path}"
  export ARTIFACT_KIND="argfile_tree"
  export ARTIFACT_HASH_ALG="none_published"
  export ARTIFACT_HASH_VALUE=""
  export ARTIFACT_HASH_VERIFIED_AT=""
  export LOADER_VERSION="${loader_ver}"
  export NEOFORGE_JAVA_MAJOR="${java_major}"
  mcmgr_log "neoforge: ${want} loader=${loader_ver} installer=${filename} java_major=${java_major}"
}

# Run after java_install. Transitions installer_jar → argfile_tree on disk (§12.1 / §19.3).
neoforge_run_installer() {
  [[ -n "${INSTALLER_FILENAME:-}" ]] || mcmgr_die "neoforge_run_installer: missing INSTALLER_FILENAME"
  [[ -n "${UNIX_ARGS_PATH:-}" ]] || mcmgr_die "neoforge_run_installer: missing UNIX_ARGS_PATH"
  local installer="${SERVER_DIR}/${INSTALLER_FILENAME}"
  [[ -f "${installer}" ]] || mcmgr_die "neoforge_run_installer: missing ${installer}"

  local unix_abs="${SERVER_DIR}/${UNIX_ARGS_PATH}"
  local jvm_args="${SERVER_DIR}/user_jvm_args.txt"

  if [[ "${DRY_RUN}" == "1" ]]; then
    mkdir -p "$(dirname "${unix_abs}")"
    printf '# dry-run unix_args placeholder\n' >"${unix_abs}"
    mcmgr_log "neoforge: dry-run placeholder unix_args at ${unix_abs}"
  else
    [[ -n "${JAVA_EXECUTABLE:-}" && -x "${JAVA_EXECUTABLE}" ]] \
      || mcmgr_die "neoforge_run_installer: JAVA_EXECUTABLE not ready"
    mcmgr_log "neoforge: running installer --installServer"
    (
      cd "${SERVER_DIR}"
      "${JAVA_EXECUTABLE}" -jar "${INSTALLER_FILENAME}" --installServer
    ) || mcmgr_die "NeoForge --installServer failed"
    [[ -f "${unix_abs}" ]] || mcmgr_die "NeoForge installer did not write ${unix_abs}"
  fi

  # Memory lives in the JVM argfile, not ExecStart (§6.3 / §19.4).
  {
    printf '%s\n' "# Managed by mcmgr — JVM memory for argfile launch"
    printf '%s\n' "-Xms${JVM_XMS}"
    printf '%s\n' "-Xmx${JVM_XMX}"
  } >"${jvm_args}"

  if [[ "${DRY_RUN}" != "1" ]]; then
    chown mcmgr:mcmgr "${jvm_args}" "${unix_abs}" "${installer}" 2>/dev/null || true
    chmod 0640 "${jvm_args}" "${unix_abs}" "${installer}" 2>/dev/null || true
  fi
}
