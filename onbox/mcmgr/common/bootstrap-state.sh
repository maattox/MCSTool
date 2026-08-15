#!/usr/bin/env bash
# Bootstrap stage tracking — /var/lib/mcmgr/bootstrap-state.json
# shellcheck shell=bash
set -euo pipefail

# shellcheck source=env.sh
source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/env.sh"

_state_py="$(mcmgr_python)"

bootstrap_state_init() {
  local op="${1:-install}"
  local ver="${2:-}"
  local dist="${3:-vanilla}"
  mkdir -p "${VAR_MCMGR}"
  "${_state_py}" - "${BOOTSTRAP_STATE}" "${op}" "${ver}" "${dist}" <<'PY'
import json, sys, datetime, os
path, op, ver, dist = sys.argv[1:5]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
ver_n = ver or None
existing = None
if os.path.isfile(path):
    try:
        with open(path, encoding="utf-8") as f:
            existing = json.load(f)
    except (OSError, json.JSONDecodeError):
        existing = None
partial = (existing or {}).get("target_manifest_partial") or {}
same = (
    existing is not None
    and existing.get("operation") == op
    and partial.get("distribution") == dist
    and (partial.get("minecraft_version") or None) == ver_n
)
if same:
    existing["updated_at"] = now
    existing["current_stage"] = None
    existing["last_error"] = None
    existing.setdefault("stages_completed", [])
    doc = existing
else:
    doc = {
      "operation": op,
      "started_at": now,
      "target_manifest_partial": {"distribution": dist, "minecraft_version": ver_n},
      "stages_completed": [],
      "current_stage": None,
      "last_error": None,
      "updated_at": now,
    }
with open(path, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY
}

bootstrap_state_set_current() {
  local stage="$1"
  "${_state_py}" - "${BOOTSTRAP_STATE}" "${stage}" <<'PY'
import json, sys, datetime
path, stage = sys.argv[1:3]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
with open(path, encoding="utf-8") as f:
    doc = json.load(f)
doc["current_stage"] = stage
doc["updated_at"] = now
doc["last_error"] = None
with open(path, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY
}

bootstrap_state_complete() {
  local stage="$1"
  "${_state_py}" - "${BOOTSTRAP_STATE}" "${stage}" <<'PY'
import json, sys, datetime
path, stage = sys.argv[1:3]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
with open(path, encoding="utf-8") as f:
    doc = json.load(f)
done = doc.setdefault("stages_completed", [])
if stage not in done:
    done.append(stage)
doc["current_stage"] = None
doc["updated_at"] = now
doc["last_error"] = None
with open(path, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY
}

bootstrap_state_has() {
  local stage="$1"
  "${_state_py}" - "${BOOTSTRAP_STATE}" "${stage}" <<'PY'
import json, sys
path, stage = sys.argv[1:3]
try:
    with open(path, encoding="utf-8") as f:
        doc = json.load(f)
except FileNotFoundError:
    raise SystemExit(1)
raise SystemExit(0 if stage in doc.get("stages_completed", []) else 1)
PY
}

bootstrap_state_fail() {
  local msg="$1"
  [[ -f "${BOOTSTRAP_STATE}" ]] || return 0
  "${_state_py}" - "${BOOTSTRAP_STATE}" "${msg}" <<'PY'
import json, sys, datetime
path, msg = sys.argv[1:3]
now = datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
with open(path, encoding="utf-8") as f:
    doc = json.load(f)
doc["last_error"] = msg
doc["updated_at"] = now
with open(path, "w", encoding="utf-8") as f:
    json.dump(doc, f, indent=2)
    f.write("\n")
PY
}
