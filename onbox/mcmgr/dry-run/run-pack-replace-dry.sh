#!/usr/bin/env bash
# Offline proof: prepare-pack-replace keeps world + identity and clears pack/loader files.
# No apt, no systemctl, no live /opt.
# shellcheck shell=bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
STAGING="${MCMGR_DRY_STAGING:-}"
KEEP="${MCMGR_DRY_KEEP:-0}"

if [[ -z "${STAGING}" ]]; then
  STAGING="$(mktemp -d "${TMPDIR:-/tmp}/mcmgr-pack-replace-XXXXXX")"
fi

export DRY_RUN=1
export MCMGR_ROOT="${STAGING}"

SERVER="${STAGING}/opt/mcmgr/server"
ETC="${STAGING}/etc/mcmgr"
VAR="${STAGING}/var/lib/mcmgr"

mkdir -p \
  "${SERVER}/world/region" \
  "${SERVER}/world_nether" \
  "${SERVER}/mods" \
  "${SERVER}/config" \
  "${SERVER}/libraries" \
  "${ETC}" \
  "${VAR}"

printf 'level-marker\n' >"${SERVER}/world/level.dat"
printf 'nether-marker\n' >"${SERVER}/world_nether/dummy"
printf 'old-mod\n' >"${SERVER}/mods/oldmod.jar"
printf 'old-cfg\n' >"${SERVER}/config/old.toml"
printf 'old-lib\n' >"${SERVER}/libraries/old.jar"
printf 'eula=true\n' >"${SERVER}/eula.txt"
printf 'online-mode=true\n' >"${SERVER}/server.properties"
printf 'op\n' >"${SERVER}/ops.json"
printf 'secret-keep\n' >"${ETC}/rcon.secret"
printf '{"operation":"install","stages_completed":["artifact_placed","manifest_written"]}\n' \
  >"${VAR}/bootstrap-state.json"
printf '{"minecraft_version":"1.21.1","loader":"fabric"}\n' >"${ETC}/game-manifest.json"

echo "[pack-replace-dry] staging=${STAGING}"
echo "[pack-replace-dry] KEEP_WORLD=1"
KEEP_WORLD=1 WIPE_WORLD=0 bash "${ROOT}/prepare-pack-replace.sh"

fail() { echo "pack-replace-dry FAIL: $*" >&2; exit 1; }

[[ -f "${SERVER}/world/level.dat" ]] || fail "world/level.dat was not kept"
grep -q 'level-marker' "${SERVER}/world/level.dat" || fail "world contents changed"
[[ -f "${SERVER}/world_nether/dummy" ]] || fail "world_nether was not kept"
[[ -f "${SERVER}/eula.txt" ]] || fail "eula.txt was not kept"
[[ -f "${SERVER}/server.properties" ]] || fail "server.properties was not kept"
[[ -f "${SERVER}/ops.json" ]] || fail "ops.json was not kept"
[[ -f "${ETC}/rcon.secret" ]] || fail "rcon.secret was touched/removed"
grep -q 'secret-keep' "${ETC}/rcon.secret" || fail "rcon.secret contents changed"
[[ -f "${ETC}/game-manifest.previous.json" ]] || fail "manifest snapshot missing"
[[ ! -e "${SERVER}/mods/oldmod.jar" ]] || fail "old mods/ was not cleared"
[[ ! -e "${SERVER}/config/old.toml" ]] || fail "old config/ was not cleared"
[[ ! -e "${SERVER}/libraries/old.jar" ]] || fail "old libraries/ was not cleared"
[[ ! -e "${VAR}/bootstrap-state.json" ]] || fail "bootstrap-state.json must be reset"

echo "[pack-replace-dry] KEEP_WORLD OK"

mkdir -p "${SERVER}/world" "${SERVER}/mods"
printf 'wipe-me\n' >"${SERVER}/world/level.dat"
printf 'wipe-mod\n' >"${SERVER}/mods/x.jar"
printf 'eula=true\n' >"${SERVER}/eula.txt"

echo "[pack-replace-dry] WIPE_WORLD=1"
KEEP_WORLD=0 WIPE_WORLD=1 bash "${ROOT}/prepare-pack-replace.sh"

[[ ! -e "${SERVER}/world/level.dat" ]] || fail "world was kept despite WIPE_WORLD=1"
[[ ! -e "${SERVER}/mods/x.jar" ]] || fail "mods/ not cleared on wipe"
[[ -f "${SERVER}/eula.txt" ]] || fail "eula.txt must survive wipe"
[[ -f "${ETC}/rcon.secret" ]] || fail "rcon.secret must survive wipe"

echo "[pack-replace-dry] WIPE_WORLD OK"
echo "[pack-replace-dry] OK"

if [[ "${KEEP}" != "1" ]]; then
  rm -rf "${STAGING}"
  echo "[pack-replace-dry] cleaned staging (set MCMGR_DRY_KEEP=1 to retain)"
else
  echo "[pack-replace-dry] retained staging at ${STAGING}"
fi
