#!/usr/bin/env bash
# Write authoritative /etc/mcmgr/game-manifest.json once at successful end (§3 / §14.2).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Expects env vars set by driver / vanilla module:
#   RESOLVED_MC_VERSION, JAVA_*, ARTIFACT_*, LAUNCH_*, EULA_*, RCON_PASSWORD_REF
manifest_write() {
  mkdir -p "${ETC_MCMGR}"
  local py
  py="$(mcmgr_python)"
  local out="${GAME_MANIFEST}"
  # Partial write to .tmp then rename — never leave a success-looking incomplete file.
  local tmp="${out}.tmp"

  "${py}" - "${tmp}" <<'PY'
import json, os, sys, datetime

out = sys.argv[1]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

def req(name):
    v = os.environ.get(name)
    if v is None or v == "":
        raise SystemExit(f"missing env {name}")
    return v

args = json.loads(req("LAUNCH_ARGS_JSON"))
hash_alg = req("ARTIFACT_HASH_ALG")
hash_val = req("ARTIFACT_HASH_VALUE")
hash_at = os.environ.get("ARTIFACT_HASH_VERIFIED_AT") or now

doc = {
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": "vanilla",
  "minecraft_version": req("RESOLVED_MC_VERSION"),
  "loader": None,
  "loader_version": None,
  "java_major": int(req("JAVA_MAJOR")),
  "java": {
    "major": int(req("JAVA_MAJOR")),
    "vendor": "temurin",
    "package_type": "jre",
    "install_path": req("JAVA_INSTALL_PATH"),
    "source": req("JAVA_SOURCE"),
    "resolved_at": req("JAVA_RESOLVED_AT"),
  },
  "server_artifact": {
    "kind": "single_jar",
    "filename": req("ARTIFACT_FILENAME"),
    "download_url": req("ARTIFACT_DOWNLOAD_URL"),
    "installer_filename": None,
    "installer_download_url": None,
    "unix_args_path": None,
  },
  "artifact_hash": {
    "algorithm": hash_alg,
    "value": hash_val,
    "verified_at": hash_at,
  },
  "launch_command": {
    "working_directory": req("SERVER_DIR"),
    "executable": req("JAVA_EXECUTABLE"),
    "args": args,
    "jvm_memory_args_source": "launch_args",
  },
  "world_path": req("WORLD_PATH"),
  "server_dir": req("SERVER_DIR"),
  "minecraft_unit": os.environ.get("MINECRAFT_UNIT", "minecraft"),
  "server_properties_managed_keys": [
    "enable-rcon", "rcon.port", "rcon.password",
    "motd", "difficulty", "max-players",
    "white-list", "enforce-whitelist", "online-mode",
  ],
  "eula": {
    "accepted": True,
    "accepted_at": req("EULA_ACCEPTED_AT"),
    "accepted_version_context": req("EULA_ACCEPTED_VERSION_CONTEXT"),
  },
  "rcon": {
    "enabled": True,
    "port": 25575,
    "bind_address": "127.0.0.1",
    "password_secret_ref": req("RCON_PASSWORD_REF"),
  },
  "modpack": None,
  "install": {
    "installed_at": now,
    "installed_by": os.environ.get("MCMGR_INSTALLED_BY", "bootstrap"),
    "bootstrap_tool_version": os.environ.get("MCMGR_BOOTSTRAP_VERSION", "mcmgr-bootstrap/0.1.0"),
    "os_arch": os.environ.get("MCMGR_OS_ARCH", "aarch64"),
  },
  "previous": None,
}

# Safety: never embed password
blob = json.dumps(doc)
secret_path = os.environ.get("RCON_SECRET", "")
if secret_path and os.path.isfile(secret_path):
    pw = open(secret_path, encoding="utf-8").read().strip()
    if pw and pw in blob:
        raise SystemExit("refusing to write manifest: rcon password would leak into JSON")

with open(out, "w", encoding="utf-8", newline="\n") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY

  mv -f "${tmp}" "${out}"
  if [[ "${DRY_RUN}" != "1" ]]; then
    chown root:mcmgr "${out}" 2>/dev/null || true
    chmod 0640 "${out}"
  fi
  mcmgr_log "manifest: wrote ${out}"
}
