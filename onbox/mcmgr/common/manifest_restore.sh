#!/usr/bin/env bash
# Re-export driver env from a completed game-manifest.json (bootstrap resume).
# Skipping artifact_placed / java_resolved does not re-run the resolve functions,
# so RESOLVED_MC_VERSION / JAVA_EXECUTABLE / ARTIFACT_KIND stay unset (SETUP-ISSUE-16).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Prints `export KEY=value` lines for bash eval. Returns 1 if the manifest is missing
# or unreadable.
bootstrap_restore_env_from_manifest() {
  [[ -f "${GAME_MANIFEST}" ]] || return 1
  local py
  py="$(mcmgr_python)"
  local exports
  if ! exports="$("${py}" - "${GAME_MANIFEST}" <<'PY'
import json, shlex, sys

path = sys.argv[1]
try:
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)

art = doc.get("server_artifact") or {}
java = doc.get("java") or {}
eula = doc.get("eula") or {}
launch = doc.get("launch_command") or {}
ah = doc.get("artifact_hash") or {}
rcon = doc.get("rcon") or {}

kind = art.get("kind") or ""
filename = art.get("filename") or art.get("installer_filename") or ""
download = art.get("download_url") or art.get("installer_download_url") or ""
installer = art.get("installer_filename") or ""
installer_url = art.get("installer_download_url") or ""
unix_args = art.get("unix_args_path") or ""
exe = (launch.get("executable") or "").strip()
mc = (doc.get("minecraft_version") or "").strip()
if not mc or not exe:
    raise SystemExit(1)

env = {
    "RESOLVED_MC_VERSION": mc,
    "ARTIFACT_KIND": kind,
    "ARTIFACT_FILENAME": filename,
    "ARTIFACT_DOWNLOAD_URL": download,
    "INSTALLER_FILENAME": installer,
    "INSTALLER_DOWNLOAD_URL": installer_url,
    "UNIX_ARGS_PATH": unix_args,
    "RUNNABLE_JAR_FILENAME": art.get("runnable_jar_filename") or "",
    "ARTIFACT_HASH_ALG": ah.get("algorithm") or "none_published",
    "ARTIFACT_HASH_VALUE": ah.get("value") or "",
    "ARTIFACT_HASH_VERIFIED_AT": ah.get("verified_at") or "",
    "LOADER_VERSION": doc.get("loader_version") or "",
    "JAVA_MAJOR": str(doc.get("java_major") or java.get("major") or ""),
    "JAVA_EXECUTABLE": exe,
    "JAVA_INSTALL_PATH": java.get("install_path") or "",
    "JAVA_SOURCE": java.get("source") or "",
    "JAVA_RESOLVED_AT": java.get("resolved_at") or "",
    "EULA_ACCEPTED_AT": eula.get("accepted_at") or "",
    "EULA_ACCEPTED_VERSION_CONTEXT": eula.get("accepted_version_context") or mc,
    "RCON_PASSWORD_REF": rcon.get("password_secret_ref") or "",
}

major = env["JAVA_MAJOR"]
loader = (doc.get("loader") or "").strip().lower()
dist = (doc.get("distribution") or "").strip().lower()
if loader == "forge" or (dist == "modded" and loader == "forge"):
    env["FORGE_JAVA_MAJOR"] = major
elif loader == "neoforge":
    env["NEOFORGE_JAVA_MAJOR"] = major
elif loader == "fabric":
    env["FABRIC_JAVA_MAJOR"] = major
elif dist == "paper":
    env["PAPER_JAVA_MAJOR"] = major
else:
    env["VANILLA_JAVA_MAJOR"] = major

for key, value in env.items():
    print(f"export {key}={shlex.quote(str(value))}")
PY
  )"; then
    return 1
  fi
  eval "${exports}"
  mcmgr_log "resume: restored resolve/java env from ${GAME_MANIFEST}"
}
