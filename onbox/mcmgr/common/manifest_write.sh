#!/usr/bin/env bash
# Write authoritative /etc/mcmgr/game-manifest.json once at successful end (§3 / §14.2).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Expects env vars set by driver / installer module:
#   DISTRIBUTION, RESOLVED_MC_VERSION, JAVA_*, ARTIFACT_*, LAUNCH_*, EULA_*, RCON_PASSWORD_REF
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
hash_at = os.environ.get("ARTIFACT_HASH_VERIFIED_AT") or now
dist = req("DISTRIBUTION")
if dist not in ("vanilla", "paper", "fabric", "neoforge", "forge"):
    raise SystemExit(f"unsupported distribution {dist}")

artifact_filename = req("ARTIFACT_FILENAME")
artifact_download_url = req("ARTIFACT_DOWNLOAD_URL")
installer_filename = None
installer_download_url = None
unix_args_path = None
jvm_mem_src = "launch_args"

if dist == "fabric":
    written_dist = "modded"
    loader = "fabric"
    loader_version = req("LOADER_VERSION")
    artifact_kind = os.environ.get("ARTIFACT_KIND") or "launcher_jar"
    if hash_alg != "none_published":
        raise SystemExit("fabric artifact_hash.algorithm must be none_published")
    hash_obj = {"algorithm": "none_published", "value": None, "verified_at": None}
elif dist == "neoforge":
    written_dist = "modded"
    loader = "neoforge"
    loader_version = req("LOADER_VERSION")
    artifact_kind = os.environ.get("ARTIFACT_KIND") or "argfile_tree"
    if artifact_kind != "argfile_tree":
        raise SystemExit("neoforge server_artifact.kind must be argfile_tree after install")
    if hash_alg != "none_published":
        raise SystemExit("neoforge artifact_hash.algorithm must be none_published")
    hash_obj = {"algorithm": "none_published", "value": None, "verified_at": None}
    installer_filename = req("INSTALLER_FILENAME")
    installer_download_url = req("INSTALLER_DOWNLOAD_URL")
    unix_args_path = req("UNIX_ARGS_PATH")
    artifact_filename = None
    artifact_download_url = None
    jvm_mem_src = "user_jvm_args_file"
    if not args or args[0] != "@user_jvm_args.txt" or args[-1] != "--nogui":
        raise SystemExit("neoforge launch args must be @user_jvm_args.txt @unix_args --nogui")
    if not any(a.startswith("@libraries/") for a in args):
        raise SystemExit("neoforge launch args missing @unix_args path")
elif dist == "forge":
    written_dist = "modded"
    loader = "forge"
    loader_version = req("LOADER_VERSION")
    artifact_kind = os.environ.get("ARTIFACT_KIND") or ""
    if hash_alg != "none_published":
        raise SystemExit("forge artifact_hash.algorithm must be none_published")
    hash_obj = {"algorithm": "none_published", "value": None, "verified_at": None}
    installer_filename = req("INSTALLER_FILENAME")
    installer_download_url = req("INSTALLER_DOWNLOAD_URL")
    if artifact_kind == "argfile_tree":
        unix_args_path = req("UNIX_ARGS_PATH")
        artifact_filename = None
        artifact_download_url = None
        jvm_mem_src = "user_jvm_args_file"
        if not args or args[0] != "@user_jvm_args.txt" or args[-1] != "--nogui":
            raise SystemExit("forge argfile launch args must be @user_jvm_args.txt @unix_args --nogui")
        if not any(a.startswith("@libraries/net/minecraftforge/forge/") for a in args):
            raise SystemExit("forge launch args missing @unix_args path")
    elif artifact_kind == "single_jar":
        artifact_filename = req("ARTIFACT_FILENAME")
        artifact_download_url = None
        unix_args_path = None
        jvm_mem_src = "launch_args"
        if len(args) < 3 or args[-3] != "-jar" or args[-1] != "nogui":
            raise SystemExit("forge single_jar launch args must end with -jar <forge.jar> nogui")
        if args[-2] != artifact_filename:
            raise SystemExit("forge single_jar launch jar does not match ARTIFACT_FILENAME")
        if "--nogui" in args:
            raise SystemExit("forge single_jar uses nogui, not --nogui")
    else:
        raise SystemExit(f"forge server_artifact.kind must be single_jar or argfile_tree (got {artifact_kind})")
else:
    written_dist = dist
    loader = None
    loader_version = None
    artifact_kind = "single_jar"
    hash_obj = {
        "algorithm": hash_alg,
        "value": req("ARTIFACT_HASH_VALUE"),
        "verified_at": hash_at,
    }

doc = {
  "schema_version": 1,
  "game_type": "minecraft",
  "distribution": written_dist,
  "minecraft_version": req("RESOLVED_MC_VERSION"),
  "loader": loader,
  "loader_version": loader_version,
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
    "kind": artifact_kind,
    "filename": artifact_filename,
    "download_url": artifact_download_url,
    "installer_filename": installer_filename,
    "installer_download_url": installer_download_url,
    "unix_args_path": unix_args_path,
  },
  "artifact_hash": hash_obj,
  "launch_command": {
    "working_directory": req("SERVER_DIR"),
    "executable": req("JAVA_EXECUTABLE"),
    "args": args,
    "jvm_memory_args_source": jvm_mem_src,
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
