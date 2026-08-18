#!/usr/bin/env bash
# Shared bootstrap driver — layout → java → installer module → eula/props/rcon → unit → manifest.
# Usage:
#   EULA_ACCEPTED=true MINECRAFT_VERSION=1.21.1 sudo -E ./common/driver.sh
#   EULA_ACCEPTED=true DISTRIBUTION=paper MINECRAFT_VERSION=1.21.10 sudo -E ./common/driver.sh
#   EULA_ACCEPTED=true DISTRIBUTION=fabric MINECRAFT_VERSION=1.21.8 sudo -E ./common/driver.sh
#   EULA_ACCEPTED=true DISTRIBUTION=neoforge MINECRAFT_VERSION=1.21.1 sudo -E ./common/driver.sh
# Dry-run:
#   DRY_RUN=1 MCMGR_ROOT=/tmp/mcmgr-dry EULA_ACCEPTED=true ./common/driver.sh
#   DRY_RUN=1 DISTRIBUTION=paper MINECRAFT_VERSION=1.21.10 MCMGR_ROOT=/tmp/mcmgr-dry EULA_ACCEPTED=true ./common/driver.sh
#   DRY_RUN=1 DISTRIBUTION=fabric MINECRAFT_VERSION=1.21.8 MCMGR_ROOT=/tmp/mcmgr-dry EULA_ACCEPTED=true ./common/driver.sh
#   DRY_RUN=1 DISTRIBUTION=neoforge MINECRAFT_VERSION=1.21.1 MCMGR_ROOT=/tmp/mcmgr-dry EULA_ACCEPTED=true ./common/driver.sh
# shellcheck shell=bash
set -euo pipefail

COMMON_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=env.sh
source "${COMMON_DIR}/env.sh"
# shellcheck source=bootstrap-state.sh
source "${COMMON_DIR}/bootstrap-state.sh"
# shellcheck source=layout.sh
source "${COMMON_DIR}/layout.sh"
# shellcheck source=java.sh
source "${COMMON_DIR}/java.sh"
# shellcheck source=eula.sh
source "${COMMON_DIR}/eula.sh"
# shellcheck source=rcon.sh
source "${COMMON_DIR}/rcon.sh"
# shellcheck source=unit_gen.sh
source "${COMMON_DIR}/unit_gen.sh"
# shellcheck source=manifest_write.sh
source "${COMMON_DIR}/manifest_write.sh"
# shellcheck source=idle_agent_sync.sh
source "${COMMON_DIR}/idle_agent_sync.sh"

on_err() {
  local ec=$?
  bootstrap_state_fail "driver exited with code ${ec}" || true
  mcmgr_log "driver failed (exit ${ec})"
  exit "${ec}"
}
trap on_err ERR

main() {
  case "${DISTRIBUTION}" in
    vanilla|paper|fabric|neoforge) ;;
    *) mcmgr_die "only distribution=vanilla|paper|fabric|neoforge is implemented (got ${DISTRIBUTION})" ;;
  esac

  if [[ "${DRY_RUN}" != "1" && "$(id -u)" -ne 0 ]]; then
    mcmgr_die "live install must run as root (or use DRY_RUN=1)"
  fi

  export MCMGR_OS_ARCH
  MCMGR_OS_ARCH="$(uname -m 2>/dev/null || echo aarch64)"
  # Product target is Ampere aarch64; dry-run may run on x86_64 Windows/Git Bash — stamp aarch64 for fixtures.
  if [[ "${DRY_RUN}" == "1" ]]; then
    MCMGR_OS_ARCH="aarch64"
  fi

  bootstrap_state_init "install" "${MINECRAFT_VERSION}" "${DISTRIBUTION}"

  run_stage() {
    local name="$1"
    shift
    if bootstrap_state_has "${name}"; then
      mcmgr_log "skip completed stage: ${name}"
      return 0
    fi
    bootstrap_state_set_current "${name}"
    "$@"
    bootstrap_state_complete "${name}"
  }

  # Accounts may skip on resume; permission apply/verify must never skip (SETUP-ISSUE-4).
  run_stage layout_ready layout_ensure_accounts
  layout_apply

  # Resolve + place artifact first so Java major comes from upstream metadata.
  local java_major_from_module=""
  if [[ "${DISTRIBUTION}" == "paper" ]]; then
    # shellcheck source=../modules/bootstrap-paper.sh
    source "${MCMGR_HOME}/modules/bootstrap-paper.sh"
    run_stage artifact_placed paper_resolve_and_place
    java_major_from_module="${PAPER_JAVA_MAJOR:-}"
  elif [[ "${DISTRIBUTION}" == "fabric" ]]; then
    # shellcheck source=../modules/bootstrap-fabric.sh
    source "${MCMGR_HOME}/modules/bootstrap-fabric.sh"
    run_stage artifact_placed fabric_resolve_and_place
    java_major_from_module="${FABRIC_JAVA_MAJOR:-}"
  elif [[ "${DISTRIBUTION}" == "neoforge" ]]; then
    # shellcheck source=../modules/bootstrap-neoforge.sh
    source "${MCMGR_HOME}/modules/bootstrap-neoforge.sh"
    run_stage artifact_placed neoforge_resolve_and_place
    java_major_from_module="${NEOFORGE_JAVA_MAJOR:-}"
  else
    # shellcheck source=../modules/bootstrap-vanilla.sh
    source "${MCMGR_HOME}/modules/bootstrap-vanilla.sh"
    run_stage artifact_placed vanilla_resolve_and_place
    java_major_from_module="${VANILLA_JAVA_MAJOR:-}"
  fi

  run_stage java_resolved java_install "${java_major_from_module}"

  if [[ "${DISTRIBUTION}" == "neoforge" ]]; then
    # --installServer needs the Temurin we just placed (§19.3 / §12.1).
    run_stage installer_run neoforge_run_installer
  fi

  run_stage eula_written eula_write "${RESOLVED_MC_VERSION}"
  run_stage rcon_ready rcon_setup
  # Properties apply must never skip on resume (SETUP-ISSUE-3): rcon_ready may
  # already be complete from a prior run that wrote white-list=true.
  if [[ -f "${RCON_SECRET}" ]]; then
    server_properties_apply "$(tr -d '\r\n' <"${RCON_SECRET}")"
  fi

  # Build launch_command args. unit_gen stays generic (§6.3):
  # Vanilla uses bare `nogui`; Paper uses `--nogui`; Fabric launcher_jar uses bare `nogui`.
  # NeoForge argfile_tree: `@user_jvm_args.txt @unix_args --nogui` (memory is in the argfile).
  local launch_args
  if [[ "${DISTRIBUTION}" == "neoforge" ]]; then
    [[ -n "${UNIX_ARGS_PATH:-}" ]] || mcmgr_die "neoforge launch needs UNIX_ARGS_PATH"
    launch_args=(
      "@user_jvm_args.txt"
      "@${UNIX_ARGS_PATH}"
      "--nogui"
    )
  else
    launch_args=(
      "-Xms${JVM_XMS}"
      "-Xmx${JVM_XMX}"
    )
    if [[ "${DISTRIBUTION}" == "paper" ]]; then
      export PAPER_JVM_FLAGS_JSON="${PAPER_JVM_FLAGS_JSON:-[]}"
      local flag
      while IFS= read -r flag; do
        flag="${flag%$'\r'}"
        [[ -n "${flag}" ]] && launch_args+=("${flag}")
      done < <("$(mcmgr_python)" -c 'import json,os,sys
flags=json.loads(os.environ.get("PAPER_JVM_FLAGS_JSON") or "[]")
if not flags:
    flags=["-XX:+UseG1GC"]
sys.stdout.buffer.write(("\n".join(flags) + "\n").encode("utf-8"))
')
      launch_args+=("-jar" "${ARTIFACT_FILENAME}" "--nogui")
    elif [[ "${DISTRIBUTION}" == "fabric" ]]; then
      launch_args+=("-jar" "${ARTIFACT_FILENAME}" "nogui")
    else
      launch_args+=("-XX:+UseG1GC" "-jar" "server.jar" "nogui")
    fi
  fi
  export LAUNCH_ARGS_JSON
  LAUNCH_ARGS_JSON="$("$(mcmgr_python)" -c 'import json,sys; print(json.dumps(sys.argv[1:]))' "${launch_args[@]}")"

  run_stage unit_written unit_generate "${JAVA_EXECUTABLE}" "${SERVER_DIR}" "${launch_args[@]}"

  # Export paths for manifest_write
  export SERVER_DIR WORLD_PATH

  run_stage manifest_written manifest_write
  run_stage idle_agent_synced idle_agent_sync_from_manifest

  layout_apply
  layout_verify

  if [[ "${DRY_RUN}" != "1" ]]; then
    # Optional start + light health check (RCON may not be ready until first world gen).
    systemctl start "${MINECRAFT_UNIT}.service" || mcmgr_log "warning: systemctl start failed (first boot may need manual start)"
  fi

  if [[ "${DISTRIBUTION}" == "paper" ]]; then
    mcmgr_log "SUCCESS: Paper ${RESOLVED_MC_VERSION} bootstrap complete"
  elif [[ "${DISTRIBUTION}" == "fabric" ]]; then
    mcmgr_log "SUCCESS: Fabric ${RESOLVED_MC_VERSION} loader=${LOADER_VERSION:-} bootstrap complete"
  elif [[ "${DISTRIBUTION}" == "neoforge" ]]; then
    mcmgr_log "SUCCESS: NeoForge ${RESOLVED_MC_VERSION} loader=${LOADER_VERSION:-} bootstrap complete"
  else
    mcmgr_log "SUCCESS: Vanilla ${RESOLVED_MC_VERSION} bootstrap complete"
  fi
  mcmgr_log "  server_dir=${SERVER_DIR}"
  mcmgr_log "  manifest=${GAME_MANIFEST}"
  mcmgr_log "  unit=${SYSTEMD_UNIT_PATH}"
}

main "$@"
