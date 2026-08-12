#!/usr/bin/env bash
# Vanilla installer module — piston-meta resolve → download → sha1 → place server.jar (§16).
# Does NOT write systemd, properties, EULA, or the final game-manifest (shared driver).
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=../common/env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")/../common" && pwd)/env.sh"

PISTON_MANIFEST_URL="${PISTON_MANIFEST_URL:-https://piston-meta.mojang.com/mc/game/version_manifest_v2.json}"

# Exports for driver:
#   RESOLVED_MC_VERSION, ARTIFACT_FILENAME, ARTIFACT_DOWNLOAD_URL,
#   ARTIFACT_HASH_ALG, ARTIFACT_HASH_VALUE, ARTIFACT_HASH_VERIFIED_AT,
#   VANILLA_JAVA_MAJOR

vanilla_resolve_and_place() {
  local want="${MINECRAFT_VERSION}"
  local py
  py="$(mcmgr_python)"
  if [[ -z "${MCMGR_FIXTURES_DIR}" ]]; then
    mcmgr_need_cmd curl
  fi

  local manifest_json version_json
  if [[ -n "${MCMGR_FIXTURES_DIR}" ]]; then
    manifest_json="${MCMGR_FIXTURES_DIR}/mojang-version-manifest-v2.json"
    [[ -f "${manifest_json}" ]] || mcmgr_die "fixture missing: ${manifest_json}"
  else
    manifest_json="$(mktemp)"
    curl -fsSL -o "${manifest_json}" "${PISTON_MANIFEST_URL}"
  fi

  # Resolve id + metadata URL (or fixture path). Three lines: id, url/path, manifest-entry sha1.
  local resolved meta_url meta_sha1
  local _resolve_out
  _resolve_out="$(
    "${py}" - "${manifest_json}" "${want}" "${MCMGR_FIXTURES_DIR:-}" <<'PY'
import json, sys, os
manifest_path, want, fixtures = sys.argv[1:4]
with open(manifest_path, encoding="utf-8") as f:
    man = json.load(f)
if want in ("latest.release", "latest"):
    want = man["latest"]["release"]
elif want == "latest.snapshot":
    want = man["latest"]["snapshot"]
entry = None
for v in man["versions"]:
    if v["id"] == want:
        entry = v
        break
if entry is None:
    raise SystemExit(f"version id not found in manifest: {want}")
url = entry["url"]
# In fixture mode, map known ids to local metadata files.
if fixtures:
    local = os.path.join(fixtures, f"mojang-version-metadata-{want}.json")
    if os.path.isfile(local):
        url = local
print(want)
print(url)
print(entry.get("sha1", ""))
PY
  )"
  resolved="$(printf '%s\n' "${_resolve_out}" | sed -n '1p')"
  meta_url="$(printf '%s\n' "${_resolve_out}" | sed -n '2p')"
  meta_sha1="$(printf '%s\n' "${_resolve_out}" | sed -n '3p')"

  if [[ -n "${MCMGR_FIXTURES_DIR}" && -f "${meta_url}" ]]; then
    version_json="${meta_url}"
  else
    version_json="$(mktemp)"
    curl -fsSL -o "${version_json}" "${meta_url}"
  fi

  local jar_url jar_sha1 java_major
  local _meta_out
  _meta_out="$(
    "${py}" - "${version_json}" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    meta = json.load(f)
server = meta["downloads"]["server"]
jv = meta.get("javaVersion") or {}
major = jv.get("majorVersion")
if major is None:
    raise SystemExit("version metadata missing javaVersion.majorVersion")
print(server["url"])
print(server["sha1"])
print(int(major))
PY
  )"
  jar_url="$(printf '%s\n' "${_meta_out}" | sed -n '1p')"
  jar_sha1="$(printf '%s\n' "${_meta_out}" | sed -n '2p')"
  java_major="$(printf '%s\n' "${_meta_out}" | sed -n '3p')"

  mkdir -p "${SERVER_DIR}"
  local dest="${SERVER_DIR}/server.jar"
  local verified_at
  verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

  if [[ "${DRY_RUN}" == "1" ]]; then
    # Placeholder jar — do not download ~50 MiB. Record authoritative sha1 from metadata.
    printf 'dry-run-placeholder-server-jar\n' >"${dest}"
    mcmgr_log "vanilla: dry-run placeholder jar at ${dest} (expected sha1=${jar_sha1})"
  else
    local tmp="${dest}.part"
    local attempt=0
    local ok=0
    while [[ "${attempt}" -lt 2 ]]; do
      attempt=$((attempt + 1))
      curl -fsSL -o "${tmp}" "${jar_url}"
      local got
      got="$(sha1sum "${tmp}" | awk '{print $1}')"
      if [[ "${got}" == "${jar_sha1}" ]]; then
        ok=1
        break
      fi
      mcmgr_log "vanilla: sha1 mismatch (got ${got}, want ${jar_sha1}) attempt=${attempt}"
      rm -f "${tmp}"
    done
    [[ "${ok}" == "1" ]] || mcmgr_die "server.jar failed integrity check after retry"
    mv -f "${tmp}" "${dest}"
    if [[ "${DRY_RUN}" != "1" ]]; then
      chown mcmgr:mcmgr "${dest}" 2>/dev/null || true
      chmod 0640 "${dest}"
    fi
    verified_at="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  fi

  export RESOLVED_MC_VERSION="${resolved}"
  export ARTIFACT_FILENAME="server.jar"
  export ARTIFACT_DOWNLOAD_URL="${jar_url}"
  export ARTIFACT_HASH_ALG="sha1"
  export ARTIFACT_HASH_VALUE="${jar_sha1}"
  export ARTIFACT_HASH_VERIFIED_AT="${verified_at}"
  export VANILLA_JAVA_MAJOR="${java_major}"
  mcmgr_log "vanilla: ${resolved} jar ready java_major=${java_major}"
}
