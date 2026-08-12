#!/usr/bin/env bash
# Assert dry-run outputs match blueprint §4.1 / Step 2.3 contract.
# shellcheck shell=bash
set -euo pipefail

MANIFEST="${1:?manifest path}"
UNIT="${2:?unit path}"
SECRET="${3:?rcon secret path}"

PY=""
if command -v python3 >/dev/null 2>&1; then PY=python3
elif command -v python >/dev/null 2>&1; then PY=python
else
  echo "assert: need python" >&2
  exit 1
fi

[[ -f "${MANIFEST}" ]] || { echo "missing manifest"; exit 1; }
[[ -f "${UNIT}" ]] || { echo "missing unit"; exit 1; }
[[ -f "${SECRET}" ]] || { echo "missing rcon secret"; exit 1; }

"${PY}" - "${MANIFEST}" "${UNIT}" "${SECRET}" <<'PY'
import json, sys, re

manifest_path, unit_path, secret_path = sys.argv[1:4]
with open(manifest_path, encoding="utf-8") as f:
    doc = json.load(f)
with open(unit_path, encoding="utf-8") as f:
    unit = f.read()
password = open(secret_path, encoding="utf-8").read().strip()
assert password, "empty rcon secret"

required = [
    "schema_version", "game_type", "distribution", "minecraft_version",
    "loader", "loader_version", "java_major", "java", "server_artifact",
    "artifact_hash", "launch_command", "world_path", "server_dir",
    "minecraft_unit", "server_properties_managed_keys", "eula", "rcon",
    "modpack", "install", "previous",
]
missing = [k for k in required if k not in doc]
assert not missing, f"manifest missing keys: {missing}"

assert doc["schema_version"] == 1
assert doc["game_type"] == "minecraft"
assert doc["distribution"] == "vanilla"
assert doc["loader"] is None
assert doc["loader_version"] is None
assert doc["modpack"] is None
assert doc["previous"] is None
assert isinstance(doc["java_major"], int)
assert doc["java"]["vendor"] == "temurin"
assert doc["java"]["package_type"] == "jre"
assert doc["server_artifact"]["kind"] == "single_jar"
assert doc["server_artifact"]["filename"] == "server.jar"
assert doc["artifact_hash"]["algorithm"] == "sha1"
assert re.fullmatch(r"[0-9a-f]{40}", doc["artifact_hash"]["value"]), "sha1 shape"
assert doc["launch_command"]["args"][-2:] == ["server.jar", "nogui"]
assert "-jar" in doc["launch_command"]["args"]
assert doc["world_path"].endswith("/opt/mcmgr/server/world") or doc["world_path"].endswith("\\opt\\mcmgr\\server\\world") or "/opt/mcmgr/server/world" in doc["world_path"].replace("\\", "/")
assert doc["server_dir"].replace("\\", "/").endswith("/opt/mcmgr/server")
assert doc["minecraft_unit"] == "minecraft"
assert doc["rcon"]["password_secret_ref"].startswith("file:")
assert doc["rcon"]["bind_address"] == "127.0.0.1"
assert doc["rcon"]["port"] == 25575
assert doc["eula"]["accepted"] is True

blob = json.dumps(doc)
assert password not in blob, "rcon password leaked into manifest"

assert "User=mcmgr" in unit
assert "Group=mcmgr" in unit
assert "ExecStart=" in unit
assert "server.jar" in unit
assert "nogui" in unit
assert "rcon-graceful-stop.sh" in unit
assert "ProtectSystem=strict" in unit
assert "WorkingDirectory=" in unit
# Generic generator: no hard-coded vanilla-only comment required, but must not shell-wrap.
assert "bash -c" not in unit

print("assert-dry-run: all checks passed")
PY
