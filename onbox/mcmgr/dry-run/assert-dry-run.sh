#!/usr/bin/env bash
# Assert dry-run outputs match blueprint §4.1 / Step 2.3 contract.
# shellcheck shell=bash
set -euo pipefail

MANIFEST="${1:?manifest path}"
UNIT="${2:?unit path}"
SECRET="${3:?rcon secret path}"
IDLE_CFG="${4:-}"
PROPS="${5:-}"

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
[[ -n "${IDLE_CFG}" && -f "${IDLE_CFG}" ]] || { echo "missing idle-agent config (${IDLE_CFG})"; exit 1; }
[[ -n "${PROPS}" && -f "${PROPS}" ]] || { echo "missing server.properties (${PROPS})"; exit 1; }

"${PY}" - "${MANIFEST}" "${UNIT}" "${SECRET}" "${IDLE_CFG}" "${PROPS}" <<'PY'
import json, sys, re, os

manifest_path, unit_path, secret_path, idle_cfg_path, props_path = sys.argv[1:6]
with open(manifest_path, encoding="utf-8") as f:
    doc = json.load(f)
with open(unit_path, encoding="utf-8") as f:
    unit = f.read()
password = open(secret_path, encoding="utf-8").read().strip()
assert password, "empty rcon secret"
with open(idle_cfg_path, encoding="utf-8") as f:
    idle = json.load(f)

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
assert doc["distribution"] in ("vanilla", "paper", "modded"), doc["distribution"]
assert doc["modpack"] is None
assert doc["previous"] is None
assert isinstance(doc["java_major"], int)
assert doc["java"]["vendor"] == "temurin"
assert doc["java"]["package_type"] == "jre"
assert all("\r" not in a for a in doc["launch_command"]["args"]), "CRLF leaked into launch args"
if doc["distribution"] == "modded" and doc.get("loader") == "neoforge":
    assert doc["loader_version"] == "21.1.98"
    assert doc["server_artifact"]["kind"] == "argfile_tree"
    assert doc["server_artifact"]["filename"] is None
    assert doc["server_artifact"]["download_url"] is None
    inst = doc["server_artifact"]["installer_filename"]
    assert inst == "neoforge-21.1.98-installer.jar", inst
    url = doc["server_artifact"]["installer_download_url"] or ""
    assert url == (
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.98/"
        "neoforge-21.1.98-installer.jar"
    ), url
    uap = doc["server_artifact"]["unix_args_path"]
    assert uap == "libraries/net/neoforged/neoforge/21.1.98/unix_args.txt", uap
    assert doc["artifact_hash"]["algorithm"] == "none_published"
    assert doc["artifact_hash"]["value"] is None
    assert doc["artifact_hash"]["verified_at"] is None
    args = doc["launch_command"]["args"]
    assert args == ["@user_jvm_args.txt", "@" + uap, "--nogui"], args
    assert "-jar" not in args
    assert "-Xms" not in "".join(args)
    assert doc["launch_command"]["jvm_memory_args_source"] == "user_jvm_args_file"
    assert doc["java_major"] == 21
    assert doc["minecraft_version"] == "1.21.1"
    server_dir = doc["server_dir"]
    jvm_path = server_dir.rstrip("\\/") + "/user_jvm_args.txt"
    with open(jvm_path, encoding="utf-8") as jf:
        jvm = jf.read()
    assert "-Xms" in jvm and "-Xmx" in jvm
    unix_path = server_dir.rstrip("\\/") + "/" + uap.replace("\\", "/")
    assert os.path.isfile(unix_path), unix_path
elif doc["distribution"] == "modded" and doc.get("loader") == "forge":
    assert doc["loader_version"] == "14.23.5.2854"
    assert doc["server_artifact"]["kind"] == "single_jar"
    fn = doc["server_artifact"]["filename"]
    assert fn == "forge-1.12.2-14.23.5.2854.jar", fn
    assert doc["server_artifact"]["download_url"] is None
    inst = doc["server_artifact"]["installer_filename"]
    assert inst == "forge-1.12.2-14.23.5.2854-installer.jar", inst
    url = doc["server_artifact"]["installer_download_url"] or ""
    assert url == (
        "https://maven.minecraftforge.net/net/minecraftforge/forge/"
        "1.12.2-14.23.5.2854/forge-1.12.2-14.23.5.2854-installer.jar"
    ), url
    assert "files.minecraftforge.net" not in url.lower()
    assert doc["server_artifact"]["unix_args_path"] is None
    assert doc["artifact_hash"]["algorithm"] == "none_published"
    assert doc["artifact_hash"]["value"] is None
    assert doc["artifact_hash"]["verified_at"] is None
    args = doc["launch_command"]["args"]
    assert args[-3:] == ["-jar", fn, "nogui"], args
    assert "--nogui" not in args
    assert doc["launch_command"]["jvm_memory_args_source"] == "launch_args"
    assert doc["java_major"] == 8
    assert doc["minecraft_version"] == "1.12.2"
    server_dir = doc["server_dir"]
    jar_path = server_dir.rstrip("\\/") + "/" + fn
    assert os.path.isfile(jar_path), jar_path
    vanilla_jar = server_dir.rstrip("\\/") + "/server.jar"
    assert os.path.isfile(vanilla_jar), "Forge requires Vanilla server.jar first"
elif doc["distribution"] == "modded":
    assert doc["loader"] == "fabric"
    assert doc["loader_version"] == "0.17.2"
    fn = doc["server_artifact"]["filename"]
    assert fn == "fabric-server-mc.1.21.8-loader.0.17.2-launcher.1.1.0.jar", fn
    assert doc["server_artifact"]["kind"] == "launcher_jar"
    url = doc["server_artifact"]["download_url"] or ""
    assert url == "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/1.1.0/server/jar"
    parts = [p for p in url.split("/") if p]
    assert parts[-5:-2] == ["1.21.8", "0.17.2", "1.1.0"], parts
    assert parts[-2:] == ["server", "jar"]
    assert doc["artifact_hash"]["algorithm"] == "none_published"
    assert doc["artifact_hash"]["value"] is None
    assert doc["artifact_hash"]["verified_at"] is None
    assert doc["launch_command"]["args"][-1] == "nogui"
    assert doc["launch_command"]["args"][-3] == "-jar"
    assert doc["launch_command"]["args"][-2] == fn
    assert "--nogui" not in doc["launch_command"]["args"]
    assert doc["java_major"] == 21
    assert doc["minecraft_version"] == "1.21.8"
    assert "-jar" in doc["launch_command"]["args"]
elif doc["distribution"] == "paper":
    assert doc["loader"] is None
    assert doc["loader_version"] is None
    fn = doc["server_artifact"]["filename"]
    assert fn.startswith("paper-") and fn.endswith(".jar"), fn
    assert doc["server_artifact"]["kind"] == "single_jar"
    assert "api.papermc.io" not in (doc["server_artifact"]["download_url"] or "").lower()
    assert doc["artifact_hash"]["algorithm"] == "sha256"
    assert re.fullmatch(r"[0-9a-f]{64}", doc["artifact_hash"]["value"]), "sha256 shape"
    assert doc["launch_command"]["args"][-1] == "--nogui"
    assert doc["launch_command"]["args"][-3] == "-jar"
    assert doc["launch_command"]["args"][-2] == fn
    assert "-XX:+UseG1GC" in doc["launch_command"]["args"]
    assert doc["java_major"] == 21
else:
    assert doc["loader"] is None
    assert doc["loader_version"] is None
    assert doc["server_artifact"]["kind"] == "single_jar"
    assert doc["server_artifact"]["filename"] == "server.jar"
    assert doc["artifact_hash"]["algorithm"] == "sha1"
    assert re.fullmatch(r"[0-9a-f]{40}", doc["artifact_hash"]["value"]), "sha1 shape"
    assert doc["launch_command"]["args"][-2:] == ["server.jar", "nogui"]
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
if doc["distribution"] == "paper":
    assert doc["server_artifact"]["filename"] in unit
    assert "--nogui" in unit
elif doc["distribution"] == "modded" and doc.get("loader") == "neoforge":
    assert "@user_jvm_args.txt" in unit
    assert "@" + doc["server_artifact"]["unix_args_path"] in unit
    assert "--nogui" in unit
    assert "-jar" not in unit
    assert "bash -c" not in unit
elif doc["distribution"] == "modded" and doc.get("loader") == "forge":
    assert doc["server_artifact"]["filename"] in unit
    assert "nogui" in unit
    assert "--nogui" not in unit
    assert "bash -c" not in unit
elif doc["distribution"] == "modded":
    assert doc["server_artifact"]["filename"] in unit
    assert "nogui" in unit
    assert "--nogui" not in unit
else:
    assert "server.jar" in unit
    assert "nogui" in unit
    assert "--nogui" not in unit
assert "rcon-graceful-stop.sh" in unit
assert "ExecStop=+" in unit
assert "RestartPreventExitStatus=200" in unit
assert "ProtectSystem=strict" in unit
assert "PrivateTmp=true" in unit
assert "WorkingDirectory=" in unit
# Generic generator: no hard-coded vanilla-only comment required, but must not shell-wrap.
assert "bash -c" not in unit

# §10.2 idle-agent sync
assert idle.get("world_path") == doc["world_path"], "idle world_path mismatch"
assert idle.get("minecraft_unit") == doc["minecraft_unit"], "idle minecraft_unit mismatch"
assert int(idle.get("rcon_port")) == int(doc["rcon"]["port"]), "idle rcon_port mismatch"
assert idle.get("rcon_password") == password, "idle rcon_password mismatch"

# SETUP-ISSUE-3 / §7.3: in-game whitelist off; never online-mode=false
props = {}
with open(props_path, encoding="utf-8") as f:
    for line in f:
        line = line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        k, _, v = line.partition("=")
        props[k.strip()] = v.strip()
assert props.get("white-list") == "false", f"white-list want false got {props.get('white-list')}"
assert props.get("enforce-whitelist") == "false", f"enforce-whitelist want false got {props.get('enforce-whitelist')}"
assert props.get("online-mode") == "true", f"online-mode want true got {props.get('online-mode')}"

print("assert-dry-run: all checks passed (incl. §10.2 idle sync, §7.3 whitelist off)")
PY
