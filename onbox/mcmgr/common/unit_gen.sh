#!/usr/bin/env bash
# Generate minecraft.service from launch_command fields — generic, no per-distribution branches (§6).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Args: executable, working_directory, then remaining argv are ExecStart args.
unit_generate() {
  local executable="${1:?}"
  local working_directory="${2:?}"
  shift 2
  local args=("$@")

  local template="${MCMGR_HOME}/templates/minecraft.service.in"
  [[ -f "${template}" ]] || mcmgr_die "missing unit template: ${template}"

  local args_joined=""
  local a
  for a in "${args[@]}"; do
    if [[ -z "${args_joined}" ]]; then
      args_joined="${a}"
    else
      args_joined="${args_joined} ${a}"
    fi
  done

  mkdir -p "$(dirname "${SYSTEMD_UNIT_PATH}")" "${BIN_DIR}"
  cp "${MCMGR_HOME}/common/rcon-graceful-stop.sh" "${BIN_DIR}/rcon-graceful-stop.sh"
  chmod 0755 "${BIN_DIR}/rcon-graceful-stop.sh"
  if [[ "${DRY_RUN}" != "1" ]]; then
    chown root:mcmgr "${BIN_DIR}/rcon-graceful-stop.sh"
  fi

  local py
  py="$(mcmgr_python)"
  "${py}" - "${template}" "${SYSTEMD_UNIT_PATH}" "${executable}" "${working_directory}" "${BIN_DIR}" "${args_joined}" <<'PY'
import sys
template, out_path, executable, cwd, bin_dir, args_joined = sys.argv[1:7]
with open(template, encoding="utf-8") as f:
    text = f.read()
text = (
    text.replace("{{EXECUTABLE}}", executable)
    .replace("{{WORKING_DIRECTORY}}", cwd)
    .replace("{{BIN_DIR}}", bin_dir)
    .replace("{{ARGS_JOINED}}", args_joined)
)
with open(out_path, "w", encoding="utf-8", newline="\n") as f:
    f.write(text)
PY

  if [[ "${DRY_RUN}" != "1" ]]; then
    systemctl daemon-reload
    systemctl enable "${MINECRAFT_UNIT}.service"
    # Do not start here during bootstrap health phase — driver decides.
  fi
  mcmgr_log "unit: wrote ${SYSTEMD_UNIT_PATH}"
}
