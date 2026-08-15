#!/usr/bin/env bash
# Blueprint §5 ownership/mode contract. Idempotent apply + fail-closed verify.
# Never chown -R /opt/mcmgr (that would smash bin/ vs server/ owners).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

_layout_is_live() {
  [[ "${DRY_RUN}" != "1" ]]
}

# System group + user. If mcmgr already exists (cloud-init), usermod onto group mcmgr
# and home /opt/mcmgr/server — do not leave a wrong primary gid (SETUP-ISSUE-4).
layout_ensure_accounts() {
  mcmgr_log "layout: ensuring mcmgr system user/group"

  if ! _layout_is_live; then
    mcmgr_log "layout: dry-run skip useradd"
    return 0
  fi

  if ! getent group mcmgr >/dev/null 2>&1; then
    groupadd --system mcmgr
  fi

  if ! id -u mcmgr >/dev/null 2>&1; then
    useradd --system --home-dir "${SERVER_DIR}" --shell /usr/sbin/nologin --gid mcmgr mcmgr
  else
    local want_gid have_gid have_home have_shell
    want_gid="$(getent group mcmgr | cut -d: -f3)"
    have_gid="$(id -g mcmgr)"
    have_home="$(getent passwd mcmgr | cut -d: -f6)"
    have_shell="$(getent passwd mcmgr | cut -d: -f7)"
    if [[ "${have_gid}" != "${want_gid}" || "${have_home}" != "${SERVER_DIR}" || "${have_shell}" != "/usr/sbin/nologin" ]]; then
      usermod -g mcmgr -d "${SERVER_DIR}" -s /usr/sbin/nologin mcmgr
    fi
  fi
}

# Install repair entrypoint + layout library so Setup/SSH can re-apply without the staging tree.
_layout_install_repair_helpers() {
  mkdir -p "${BIN_DIR}" "${OPT_MCMGR}/lib"
  local src_common
  src_common="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  local src_home
  src_home="$(cd "${src_common}/.." && pwd)"

  cp -f "${src_common}/env.sh" "${OPT_MCMGR}/lib/env.sh"
  cp -f "${src_common}/layout.sh" "${OPT_MCMGR}/lib/layout.sh"
  cp -f "${src_common}/server_properties.sh" "${OPT_MCMGR}/lib/server_properties.sh"
  if [[ -f "${src_home}/repair-permissions.sh" ]]; then
    cp -f "${src_home}/repair-permissions.sh" "${BIN_DIR}/repair-permissions.sh"
  fi
  if [[ -f "${src_home}/repair-server-properties.sh" ]]; then
    cp -f "${src_home}/repair-server-properties.sh" "${BIN_DIR}/repair-server-properties.sh"
  fi
  if [[ -f "${src_common}/rcon-graceful-stop.sh" ]]; then
    cp -f "${src_common}/rcon-graceful-stop.sh" "${BIN_DIR}/rcon-graceful-stop.sh"
  fi
  if _layout_is_live; then
    chown root:mcmgr "${OPT_MCMGR}/lib" "${OPT_MCMGR}/lib/env.sh" "${OPT_MCMGR}/lib/layout.sh" "${OPT_MCMGR}/lib/server_properties.sh" 2>/dev/null || true
    chmod 0750 "${OPT_MCMGR}/lib"
    chmod 0640 "${OPT_MCMGR}/lib/env.sh" "${OPT_MCMGR}/lib/layout.sh" "${OPT_MCMGR}/lib/server_properties.sh"
    if [[ -f "${BIN_DIR}/repair-permissions.sh" ]]; then
      chown root:mcmgr "${BIN_DIR}/repair-permissions.sh"
      chmod 0755 "${BIN_DIR}/repair-permissions.sh"
    fi
    if [[ -f "${BIN_DIR}/repair-server-properties.sh" ]]; then
      chown root:mcmgr "${BIN_DIR}/repair-server-properties.sh"
      chmod 0755 "${BIN_DIR}/repair-server-properties.sh"
    fi
    if [[ -f "${BIN_DIR}/rcon-graceful-stop.sh" ]]; then
      chown root:mcmgr "${BIN_DIR}/rcon-graceful-stop.sh"
      chmod 0755 "${BIN_DIR}/rcon-graceful-stop.sh"
    fi
  else
    chmod 0755 "${BIN_DIR}/repair-permissions.sh" 2>/dev/null || true
    chmod 0755 "${BIN_DIR}/repair-server-properties.sh" 2>/dev/null || true
    chmod 0755 "${BIN_DIR}/rcon-graceful-stop.sh" 2>/dev/null || true
  fi
}

# Patch an already-installed unit (repair / resume). New units come from the template.
layout_patch_unit() {
  local unit="${SYSTEMD_UNIT_PATH}"
  [[ -f "${unit}" ]] || return 0
  local py
  py="$(mcmgr_python)"
  "${py}" - "${unit}" <<'PY'
import re, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    text = f.read()
orig = text
text = re.sub(r"(?m)^ExecStop=(?!\+)", "ExecStop=+", text)
if "RestartPreventExitStatus=" not in text:
    text, n = re.subn(r"(?m)^RestartSec=.*$", lambda m: m.group(0) + "\nRestartPreventExitStatus=200", text, count=1)
    if n != 1:
        text += "\nRestartPreventExitStatus=200\n"
if text != orig:
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)
PY
  if _layout_is_live; then
    systemctl daemon-reload || true
  fi
  mcmgr_log "layout: patched unit ${unit}"
}

# Idempotent per-path contract. Safe to call after every mkdir stage.
layout_apply() {
  mcmgr_log "layout: applying §5 permission contract under ${OPT_MCMGR}"

  mkdir -p \
    "${OPT_MCMGR}" \
    "${SERVER_DIR}" \
    "${WORLD_PATH}" \
    "${BIN_DIR}" \
    "${BACKUPS_WORK}" \
    "${ETC_MCMGR}" \
    "${VAR_MCMGR}" \
    "$(dirname "${SYSTEMD_UNIT_PATH}")"

  if _layout_is_live; then
    # Per-path only — never chown -R "${OPT_MCMGR}".
    chown root:mcmgr "${OPT_MCMGR}"
    chmod 0750 "${OPT_MCMGR}"
    chown mcmgr:mcmgr "${SERVER_DIR}" "${WORLD_PATH}" "${BACKUPS_WORK}"
    chmod 0750 "${SERVER_DIR}" "${WORLD_PATH}" "${BACKUPS_WORK}"
    if [[ -d "${SERVER_DIR}" ]]; then
      chown -R mcmgr:mcmgr "${SERVER_DIR}"
      find "${SERVER_DIR}" -type d -exec chmod 0750 {} \;
      find "${SERVER_DIR}" -type f -exec chmod 0640 {} \;
    fi
    chown root:mcmgr "${ETC_MCMGR}"
    chmod 0750 "${ETC_MCMGR}"
    chown root:root "${VAR_MCMGR}"
    chmod 0750 "${VAR_MCMGR}"
    chown root:mcmgr "${BIN_DIR}"
    chmod 0750 "${BIN_DIR}"
    if [[ -f "${GAME_MANIFEST}" ]]; then
      chown root:mcmgr "${GAME_MANIFEST}"
      chmod 0640 "${GAME_MANIFEST}"
    fi
    if [[ -f "${RCON_SECRET}" ]]; then
      chown root:root "${RCON_SECRET}"
      chmod 0600 "${RCON_SECRET}"
    fi

    mkdir -p "${OPT_MC_MANAGER}" "${ETC_MC_MANAGER}"
    chown root:root "${OPT_MC_MANAGER}" "${ETC_MC_MANAGER}"
    chmod 0750 "${OPT_MC_MANAGER}" "${ETC_MC_MANAGER}"
    if [[ -f "${MC_MANAGER_CONFIG}" ]]; then
      chown root:root "${MC_MANAGER_CONFIG}"
      chmod 0640 "${MC_MANAGER_CONFIG}"
    fi
  fi

  _layout_install_repair_helpers
  layout_patch_unit
}

_layout_run_as_mcmgr() {
  # argv is the command to run as mcmgr.
  if [[ "$(id -u)" -eq 0 ]]; then
    runuser -u mcmgr -- "$@"
  else
    sudo -u mcmgr -- "$@"
  fi
}

_layout_stat_ugm() {
  # prints owner:group mode (mode as 4-digit octal, e.g. 0750)
  local path="$1"
  stat -c '%U:%G %a' "${path}" | awk '{
    mode=$2
    while (length(mode) < 4) mode = "0" mode
    printf "%s %s\n", $1, mode
  }'
}

_layout_expect() {
  local path="$1"
  local want_ug="$2"
  local want_mode="$3"
  [[ -e "${path}" ]] || mcmgr_die "layout verify: missing ${path}"
  if ! _layout_is_live; then
    return 0
  fi
  local got ug mode
  got="$(_layout_stat_ugm "${path}")"
  ug="${got%% *}"
  mode="${got##* }"
  [[ "${ug}" == "${want_ug}" ]] || mcmgr_die "layout verify: ${path} owner ${ug} want ${want_ug}"
  [[ "${mode}" == "${want_mode}" ]] || mcmgr_die "layout verify: ${path} mode ${mode} want ${want_mode}"
}

_layout_java_exe() {
  local exe=""
  if [[ -f "${VAR_MCMGR}/java_executable.path" ]]; then
    exe="$(tr -d '\r\n' <"${VAR_MCMGR}/java_executable.path")"
  fi
  if [[ -z "${exe}" && -f "${SYSTEMD_UNIT_PATH}" ]]; then
    exe="$(awk -F= '/^ExecStart=/{print $2; exit}' "${SYSTEMD_UNIT_PATH}" | awk '{print $1}')"
  fi
  if [[ -z "${exe}" ]]; then
    exe="$(ls -1 /usr/lib/jvm/temurin-*-jre-*/bin/java 2>/dev/null | head -n 1 || true)"
  fi
  printf '%s' "${exe}"
}

layout_verify() {
  mcmgr_log "layout: verifying §5 contract (fail closed)"

  [[ -d "${OPT_MCMGR}" ]] || mcmgr_die "layout verify: missing ${OPT_MCMGR}"
  [[ -d "${SERVER_DIR}" ]] || mcmgr_die "layout verify: missing ${SERVER_DIR}"
  [[ -d "${WORLD_PATH}" ]] || mcmgr_die "layout verify: missing ${WORLD_PATH}"
  [[ -d "${BIN_DIR}" ]] || mcmgr_die "layout verify: missing ${BIN_DIR}"
  [[ -d "${BACKUPS_WORK}" ]] || mcmgr_die "layout verify: missing ${BACKUPS_WORK}"
  [[ -d "${ETC_MCMGR}" ]] || mcmgr_die "layout verify: missing ${ETC_MCMGR}"
  [[ -d "${VAR_MCMGR}" ]] || mcmgr_die "layout verify: missing ${VAR_MCMGR}"

  if ! _layout_is_live; then
    [[ -f "${SERVER_DIR}/server.jar" ]] || mcmgr_die "layout verify: missing ${SERVER_DIR}/server.jar"
    [[ -f "${SYSTEMD_UNIT_PATH}" ]] || mcmgr_die "layout verify: missing unit ${SYSTEMD_UNIT_PATH}"
    grep -q '^User=mcmgr' "${SYSTEMD_UNIT_PATH}" || mcmgr_die "layout verify: unit missing User=mcmgr"
    grep -q '^RestartPreventExitStatus=200' "${SYSTEMD_UNIT_PATH}" || mcmgr_die "layout verify: unit missing RestartPreventExitStatus=200"
    grep -q '^ExecStop=+' "${SYSTEMD_UNIT_PATH}" || mcmgr_die "layout verify: unit ExecStop must run as root (+ prefix)"
    mcmgr_log "layout: dry-run verify ok (dirs + unit text)"
    return 0
  fi

  _layout_expect "${OPT_MCMGR}" "root:mcmgr" "0750"
  _layout_expect "${SERVER_DIR}" "mcmgr:mcmgr" "0750"
  _layout_expect "${WORLD_PATH}" "mcmgr:mcmgr" "0750"
  _layout_expect "${BACKUPS_WORK}" "mcmgr:mcmgr" "0750"
  _layout_expect "${BIN_DIR}" "root:mcmgr" "0750"
  _layout_expect "${ETC_MCMGR}" "root:mcmgr" "0750"
  _layout_expect "${VAR_MCMGR}" "root:root" "0750"
  _layout_expect "${OPT_MC_MANAGER}" "root:root" "0750"
  if [[ -f "${MC_MANAGER_CONFIG}" ]]; then
    _layout_expect "${MC_MANAGER_CONFIG}" "root:root" "0640"
  fi
  if [[ -f "${GAME_MANIFEST}" ]]; then
    _layout_expect "${GAME_MANIFEST}" "root:mcmgr" "0640"
  fi
  [[ -f "${RCON_SECRET}" ]] || mcmgr_die "layout verify: missing ${RCON_SECRET}"
  _layout_expect "${RCON_SECRET}" "root:root" "0600"

  [[ -f "${SERVER_DIR}/server.jar" ]] || mcmgr_die "layout verify: missing ${SERVER_DIR}/server.jar"
  _layout_run_as_mcmgr bash -c 'cd "$1" && test -r server.jar' _ "${SERVER_DIR}" \
    || mcmgr_die "layout verify: mcmgr cannot cd ${SERVER_DIR} / read server.jar (CHDIR class)"

  local java_exe
  java_exe="$(_layout_java_exe)"
  [[ -n "${java_exe}" && -x "${java_exe}" ]] || mcmgr_die "layout verify: Temurin java not executable (${java_exe:-unset})"
  case "${java_exe}" in
    /usr/lib/jvm/*) ;;
    *) mcmgr_log "layout: warning: java is not under /usr/lib/jvm/: ${java_exe}" ;;
  esac
  _layout_run_as_mcmgr test -x "${java_exe}" \
    || mcmgr_die "layout verify: mcmgr cannot exec ${java_exe}"

  [[ -x "${BIN_DIR}/rcon-graceful-stop.sh" ]] || mcmgr_die "layout verify: ExecStop helper not executable"

  [[ -f "${SYSTEMD_UNIT_PATH}" ]] || mcmgr_die "layout verify: missing unit ${SYSTEMD_UNIT_PATH}"
  grep -q '^User=mcmgr' "${SYSTEMD_UNIT_PATH}" || mcmgr_die "layout verify: unit User= must be mcmgr"
  grep -q "^WorkingDirectory=${SERVER_DIR}" "${SYSTEMD_UNIT_PATH}" \
    || mcmgr_die "layout verify: unit WorkingDirectory= mismatch"
  grep -q "^ReadWritePaths=${SERVER_DIR}" "${SYSTEMD_UNIT_PATH}" \
    || mcmgr_die "layout verify: unit ReadWritePaths= mismatch"
  grep -q '^StartLimitBurst=' "${SYSTEMD_UNIT_PATH}" || mcmgr_die "layout verify: unit missing StartLimitBurst"
  grep -q '^RestartPreventExitStatus=200' "${SYSTEMD_UNIT_PATH}" \
    || mcmgr_die "layout verify: unit missing RestartPreventExitStatus=200"
  grep -q '^ExecStop=+' "${SYSTEMD_UNIT_PATH}" \
    || mcmgr_die "layout verify: unit ExecStop must use + (root) so it can read rcon.secret"

  local ugid
  ugid="$(id -u mcmgr):$(id -g mcmgr)"
  local gname
  gname="$(id -gn mcmgr)"
  [[ "${gname}" == "mcmgr" ]] || mcmgr_die "layout verify: mcmgr primary group is ${gname} want mcmgr (${ugid})"

  mcmgr_log "layout: verify ok (mcmgr can chdir + exec java)"
}

# Backward-compatible name used by older driver stages.
layout_create() {
  layout_ensure_accounts
  layout_apply
}
