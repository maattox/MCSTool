#!/usr/bin/env bash
# Read-modify-write managed server.properties keys only (§7.1 / §7.3).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

# Never write online-mode=false. Managed allow-list only.
server_properties_apply() {
  local rcon_password="${1:?rcon password}"
  local props="${SERVER_DIR}/server.properties"
  mkdir -p "${SERVER_DIR}"
  local py
  py="$(mcmgr_python)"
  "${py}" - "${props}" "${rcon_password}" <<'PY'
import sys
path, password = sys.argv[1:3]
managed = {
    "enable-rcon": "true",
    "rcon.port": "25575",
    "rcon.password": password,
    "white-list": "false",
    "enforce-whitelist": "false",
    "difficulty": "normal",
    "max-players": "20",
    "online-mode": "true",
}
# Identity (name/icon/description) owns motd after first write. Only seed a
# default when the key is missing so repair does not clobber Manager saves.
if_missing = {
    "motd": "A Minecraft Server",
}
# Intentionally never allow online-mode false via this writer.
assert managed["online-mode"] == "true"

lines = []
seen = set()
try:
    with open(path, encoding="utf-8") as f:
        lines = f.read().splitlines()
except FileNotFoundError:
    lines = []

out = []
for line in lines:
    raw = line
    if not line.strip() or line.lstrip().startswith("#"):
        out.append(raw)
        continue
    if "=" not in line:
        out.append(raw)
        continue
    key, _, _val = line.partition("=")
    key = key.strip()
    if key in managed:
        out.append(f"{key}={managed[key]}")
        seen.add(key)
        continue
    if key in if_missing:
        seen.add(key)
    out.append(raw)

for key, val in managed.items():
    if key not in seen:
        out.append(f"{key}={val}")
for key, val in if_missing.items():
    if key not in seen:
        out.append(f"{key}={val}")

with open(path, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out) + "\n")
PY
  if [[ "${DRY_RUN}" != "1" ]]; then
    chown mcmgr:mcmgr "${props}" 2>/dev/null || true
    chmod 0640 "${props}"
  fi
  mcmgr_log "server.properties: managed keys applied"
}
