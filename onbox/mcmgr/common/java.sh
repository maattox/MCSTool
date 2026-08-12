#!/usr/bin/env bash
# Install Temurin JRE for the required Java major (§9).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Outputs (exported for driver):
#   JAVA_MAJOR, JAVA_EXECUTABLE, JAVA_INSTALL_PATH, JAVA_SOURCE, JAVA_RESOLVED_AT

java_install() {
  local major="${1:?java major required}"
  JAVA_MAJOR="${major}"
  JAVA_RESOLVED_AT="$(date -u +%Y-%m-%dT%H:%M:%SZ 2>/dev/null || date -u +%Y-%m-%dT%H:%M:%SZ)"

  if [[ "${DRY_RUN}" == "1" ]]; then
    JAVA_SOURCE="distro_package"
    JAVA_INSTALL_PATH="/usr/lib/jvm/temurin-${major}-jre-arm64"
    JAVA_EXECUTABLE="${JAVA_INSTALL_PATH}/bin/java"
    export JAVA_MAJOR JAVA_EXECUTABLE JAVA_INSTALL_PATH JAVA_SOURCE JAVA_RESOLVED_AT
    mcmgr_log "java: dry-run stub major=${major} executable=${JAVA_EXECUTABLE}"
    # Persist stub for dry-run consumers
    mkdir -p "${VAR_MCMGR}"
    printf '%s\n' "${JAVA_EXECUTABLE}" >"${VAR_MCMGR}/java_executable.path"
    return 0
  fi

  if java_try_apt "${major}"; then
    JAVA_SOURCE="distro_package"
  else
    mcmgr_log "java: apt path failed; trying Adoptium REST archive fallback"
    java_try_adoptium_api "${major}"
    JAVA_SOURCE="adoptium_api_archive"
  fi

  export JAVA_MAJOR JAVA_EXECUTABLE JAVA_INSTALL_PATH JAVA_SOURCE JAVA_RESOLVED_AT
  mcmgr_log "java: ready major=${JAVA_MAJOR} source=${JAVA_SOURCE} exe=${JAVA_EXECUTABLE}"
}

java_try_apt() {
  local major="$1"
  local pkg="temurin-${major}-jre-headless"

  if ! command -v apt-get >/dev/null 2>&1; then
    return 1
  fi

  if [[ ! -f /etc/apt/sources.list.d/adoptium.list ]]; then
    mcmgr_need_cmd wget
    wget -qO /etc/apt/trusted.gpg.d/adoptium.asc \
      https://packages.adoptium.net/artifactory/api/gpg/key/public
    local codename
    codename="$(awk -F= '/^VERSION_CODENAME/{print$2}' /etc/os-release)"
    echo "deb https://packages.adoptium.net/artifactory/deb ${codename} main" \
      >/etc/apt/sources.list.d/adoptium.list
    apt-get update -y
  fi

  apt-get install -y "${pkg}" || return 1

  local cand
  cand="$(update-alternatives --list java 2>/dev/null | grep -F "temurin-${major}" | head -n1 || true)"
  if [[ -z "${cand}" ]]; then
    cand="$(command -v java || true)"
  fi
  [[ -n "${cand}" ]] || return 1
  JAVA_EXECUTABLE="$(readlink -f "${cand}")"
  JAVA_INSTALL_PATH="$(dirname "$(dirname "${JAVA_EXECUTABLE}")")"
  return 0
}

java_try_adoptium_api() {
  local major="$1"
  mcmgr_need_cmd curl
  mcmgr_need_cmd tar
  local dest="/usr/lib/jvm/temurin-${major}-jre-arm64"
  mkdir -p /usr/lib/jvm
  local url="https://api.adoptium.net/v3/binary/latest/${major}/ga/linux/aarch64/jre/hotspot/normal/eclipse"
  local tmp
  tmp="$(mktemp /tmp/temurin-XXXXXX.tar.gz)"
  curl -fsSL -o "${tmp}" "${url}"
  mkdir -p "${dest}"
  tar -xzf "${tmp}" -C "${dest}" --strip-components=1
  rm -f "${tmp}"
  JAVA_EXECUTABLE="${dest}/bin/java"
  JAVA_INSTALL_PATH="${dest}"
  [[ -x "${JAVA_EXECUTABLE}" ]] || mcmgr_die "Adoptium extract missing java at ${JAVA_EXECUTABLE}"
}
