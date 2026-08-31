#!/usr/bin/env bash
# Forge installer module — promotions_slim.json + --installServer (§20).
# Vanilla server.jar is placed first (legacy installer prerequisite).
# 1.16.5 and earlier → single_jar; 1.17+ → argfile_tree (same shape as NeoForge).
# Not a Setup radio vs NeoForge. No pack import. none_published checksum.
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=../common/env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../common" && pwd)/env.sh"

FORGE_PROMOTIONS_URL="${FORGE_PROMOTIONS_URL:-https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json}"
FORGE_USER_AGENT="${FORGE_USER_AGENT:-mcmgr-bootstrap/0.1.0 (https://github.com/maattox/MCSTool)}"
FORGE_META_PY="${MCMGR_HOME}/common/forge_meta.py"
FORGE_VERSION="${FORGE_VERSION:-}"
FORGE_CURL_MAX_TIME="${FORGE_CURL_MAX_TIME:-45}"

# Exports for driver:
#   RESOLVED_MC_VERSION, INSTALLER_FILENAME, INSTALLER_DOWNLOAD_URL,
#   RUNNABLE_JAR_FILENAME, UNIX_ARGS_PATH, ARTIFACT_KIND, ARTIFACT_HASH_ALG,
#   ARTIFACT_HASH_VALUE, ARTIFACT_HASH_VERIFIED_AT, LOADER_VERSION,
#   FORGE_JAVA_MAJOR, ARTIFACT_FILENAME

forge_curl() {
  curl -fsSL --max-time "${FORGE_CURL_MAX_TIME}" -A "${FORGE_USER_AGENT}" "$@"
}

forge_resolve_want() {
  local want="${MINECRAFT_VERSION}"
  case "${want}" in
    latest|latest.release|latest.snapshot)
      mcmgr_die "Forge installer needs a concrete Minecraft version (got ${want})"
      ;;
  esac
  printf '%s\n' "${want}"
}

forge_fetch_promotions() {
  local dest="$1"
  local attempt=0
  local ok=0
  while [[ "${attempt}" -lt 3 ]]; do
    attempt=$((attempt + 1))
    if forge_curl -o "${dest}" "${FORGE_PROMOTIONS_URL}"; then
      ok=1
      break
    fi
    mcmgr_log "forge: promotions_slim fetch failed attempt=${attempt}"
    rm -f "${dest}"
  done
  [[ "${ok}" == "1" ]] || mcmgr_die "could not reach files.minecraftforge.net (promotions_slim GET failed after retry)"
}

forge_resolve_and_place() {
  local py
  py="$(mcmgr_python)"
  [[ -f "${FORGE_META_PY}" ]] || mcmgr_die "missing ${FORGE_META_PY}"
  if [[ -z "${MCMGR_FIXTURES_DIR}" ]]; then
    mcmgr_need_cmd curl
  fi

  local want
  want="$(forge_resolve_want)"

  # Always place Vanilla server.jar first (§20.2) — cheap, sidesteps legacy e-tag failures.
  # shellcheck source=bootstrap-vanilla.sh
  source "${MCMGR_HOME}/modules/bootstrap-vanilla.sh"
  vanilla_resolve_and_place

  local promotions_json
  if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
    promotions_json="${MCMGR_FIXTURES_DIR}/forge-promotions-slim.json"
    [[ -f "${promotions_json}" ]] || mcmgr_die "fixture missing: ${promotions_json}"
  else
    promotions_json="$(mktemp)"
    forge_fetch_promotions "${promotions_json}"
  fi

  local resolve_args=("${FORGE_META_PY}" resolve "${promotions_json}" "${want}")
  if [[ -n "${FORGE_VERSION}" ]]; then
    resolve_args+=(--forge-version "${FORGE_VERSION}")
  fi
  local resolved_json
  if ! resolved_json="$("${py}" "${resolve_args[@]}")"; then
    mcmgr_die "Forge promotions resolve failed (see files.minecraftforge.net / JSON parse errors above)"
  fi

  local filename url loader_ver unix_path java_major kind runnable promo
  local _fields
  _fields="$(
    "${py}" - "${resolved_json}" <<'PY'
import json, sys
d = json.loads(sys.argv[1])
print(d["minecraft_version"])
print(d["installer_filename"])
print(d["installer_download_url"])
print(d["loader_version"])
print(d.get("unix_args_path") or "")
print(int(d["java_major"]))
print(d["artifact_kind"])
print(d["runnable_jar_filename"])
print(d.get("promo_used") or "")
PY
  )"
  want="$(printf '%s\n' "${_fields}" | sed -n '1p')"
  filename="$(printf '%s\n' "${_fields}" | sed -n '2p')"
  url="$(printf '%s\n' "${_fields}" | sed -n '3p')"
  loader_ver="$(printf '%s\n' "${_fields}" | sed -n '4p')"
  unix_path="$(printf '%s\n' "${_fields}" | sed -n '5p')"
  java_major="$(printf '%s\n' "${_fields}" | sed -n '6p')"
  kind="$(printf '%s\n' "${_fields}" | sed -n '7p')"
  runnable="$(printf '%s\n' "${_fields}" | sed -n '8p')"
  promo="$(printf '%s\n' "${_fields}" | sed -n '9p')"
  want="${want%$'\r'}"
  filename="${filename%$'\r'}"
  url="${url%$'\r'}"
  loader_ver="${loader_ver%$'\r'}"
  unix_path="${unix_path%$'\r'}"
  java_major="${java_major%$'\r'}"
  kind="${kind%$'\r'}"
  runnable="${runnable%$'\r'}"
  promo="${promo%$'\r'}"

  mkdir -p "${SERVER_DIR}"
  local dest="${SERVER_DIR}/${filename}"

  if [[ "${DRY_RUN}" == "1" ]]; then
    printf 'dry-run-placeholder-forge-installer\n' >"${dest}"
    mcmgr_log "forge: dry-run placeholder installer at ${dest} (none_published)"
  else
    local tmp="${dest}.part"
    local attempt=0
    local ok=0
    while [[ "${attempt}" -lt 3 ]]; do
      attempt=$((attempt + 1))
      if forge_curl -o "${tmp}" "${url}"; then
        ok=1
        break
      fi
      mcmgr_log "forge: installer download failed attempt=${attempt}"
      rm -f "${tmp}"
    done
    [[ "${ok}" == "1" ]] || mcmgr_die "could not reach maven.minecraftforge.net (installer jar download failed after retry)"
    mv -f "${tmp}" "${dest}"
    chown mcmgr:mcmgr "${dest}" 2>/dev/null || true
    chmod 0640 "${dest}"
  fi

  export RESOLVED_MC_VERSION="${want}"
  export ARTIFACT_FILENAME="${filename}"
  export ARTIFACT_DOWNLOAD_URL="${url}"
  export INSTALLER_FILENAME="${filename}"
  export INSTALLER_DOWNLOAD_URL="${url}"
  export RUNNABLE_JAR_FILENAME="${runnable}"
  export UNIX_ARGS_PATH="${unix_path}"
  export ARTIFACT_KIND="${kind}"
  export ARTIFACT_HASH_ALG="none_published"
  export ARTIFACT_HASH_VALUE=""
  export ARTIFACT_HASH_VERIFIED_AT=""
  export LOADER_VERSION="${loader_ver}"
  export FORGE_JAVA_MAJOR="${java_major}"
  mcmgr_log "forge: ${want} loader=${loader_ver} kind=${kind} promo=${promo} installer=${filename} java_major=${java_major}"
}

# Run after java_install. Vanilla server.jar is already in SERVER_DIR (§20.2).
forge_run_installer() {
  [[ -n "${INSTALLER_FILENAME:-}" ]] || mcmgr_die "forge_run_installer: missing INSTALLER_FILENAME"
  [[ -n "${ARTIFACT_KIND:-}" ]] || mcmgr_die "forge_run_installer: missing ARTIFACT_KIND"
  local installer="${SERVER_DIR}/${INSTALLER_FILENAME}"
  [[ -f "${installer}" ]] || mcmgr_die "forge_run_installer: missing ${installer}"
  [[ -f "${SERVER_DIR}/server.jar" ]] || mcmgr_die "forge_run_installer: missing Vanilla server.jar prerequisite"

  local jvm_args="${SERVER_DIR}/user_jvm_args.txt"

  if [[ "${DRY_RUN}" == "1" ]]; then
    if [[ "${ARTIFACT_KIND}" == "argfile_tree" ]]; then
      [[ -n "${UNIX_ARGS_PATH:-}" ]] || mcmgr_die "forge_run_installer: missing UNIX_ARGS_PATH"
      mkdir -p "$(dirname "${SERVER_DIR}/${UNIX_ARGS_PATH}")"
      printf '# dry-run unix_args placeholder\n' >"${SERVER_DIR}/${UNIX_ARGS_PATH}"
      mcmgr_log "forge: dry-run placeholder unix_args at ${SERVER_DIR}/${UNIX_ARGS_PATH}"
    else
      [[ -n "${RUNNABLE_JAR_FILENAME:-}" ]] || mcmgr_die "forge_run_installer: missing RUNNABLE_JAR_FILENAME"
      printf 'dry-run-placeholder-forge-server\n' >"${SERVER_DIR}/${RUNNABLE_JAR_FILENAME}"
      mcmgr_log "forge: dry-run placeholder runnable jar at ${SERVER_DIR}/${RUNNABLE_JAR_FILENAME}"
    fi
  else
    [[ -n "${JAVA_EXECUTABLE:-}" && -x "${JAVA_EXECUTABLE}" ]] \
      || mcmgr_die "forge_run_installer: JAVA_EXECUTABLE not ready"
    mcmgr_log "forge: running installer --installServer"
    (
      cd "${SERVER_DIR}"
      "${JAVA_EXECUTABLE}" -jar "${INSTALLER_FILENAME}" --installServer
    ) || mcmgr_die "Forge --installServer failed"
    if [[ "${ARTIFACT_KIND}" == "argfile_tree" ]]; then
      [[ -n "${UNIX_ARGS_PATH:-}" && -f "${SERVER_DIR}/${UNIX_ARGS_PATH}" ]] \
        || mcmgr_die "Forge installer did not write ${SERVER_DIR}/${UNIX_ARGS_PATH:-}"
    else
      local jar="${SERVER_DIR}/${RUNNABLE_JAR_FILENAME}"
      local universal="${SERVER_DIR}/${RUNNABLE_JAR_FILENAME%.jar}-universal.jar"
      if [[ ! -f "${jar}" && -f "${universal}" ]]; then
        RUNNABLE_JAR_FILENAME="$(basename "${universal}")"
        export RUNNABLE_JAR_FILENAME
        jar="${universal}"
      fi
      [[ -f "${jar}" ]] || mcmgr_die "Forge installer did not write ${jar}"
    fi
  fi

  if [[ "${ARTIFACT_KIND}" == "argfile_tree" ]]; then
    {
      printf '%s\n' "# Managed by mcmgr — JVM memory for argfile launch"
      printf '%s\n' "-Xms${JVM_XMS}"
      printf '%s\n' "-Xmx${JVM_XMX}"
    } >"${jvm_args}"
    export ARTIFACT_FILENAME="${INSTALLER_FILENAME}"
  else
    export ARTIFACT_FILENAME="${RUNNABLE_JAR_FILENAME}"
  fi

  if [[ "${DRY_RUN}" != "1" ]]; then
    chown mcmgr:mcmgr "${installer}" "${SERVER_DIR}/${ARTIFACT_FILENAME}" 2>/dev/null || true
    chmod 0640 "${installer}" "${SERVER_DIR}/${ARTIFACT_FILENAME}" 2>/dev/null || true
    if [[ "${ARTIFACT_KIND}" == "argfile_tree" ]]; then
      chown mcmgr:mcmgr "${jvm_args}" "${SERVER_DIR}/${UNIX_ARGS_PATH}" 2>/dev/null || true
      chmod 0640 "${jvm_args}" "${SERVER_DIR}/${UNIX_ARGS_PATH}" 2>/dev/null || true
    fi
  fi
}
