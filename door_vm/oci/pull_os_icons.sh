#!/usr/bin/env bash
# Pull Manager-composed MOTD favicons (idle/starting/exhausted) from Object Storage.
# Invoked by mccontrol after budget pull. Fail-open: missing objects keep existing files.
set -euo pipefail

export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:${HOME}/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"

NS="${OBJECT_STORAGE_NAMESPACE:-}"
BN="${OBJECT_STORAGE_BUCKET:-}"
CACHE="${OS_CACHE_DIR:-/var/lib/mccontrol/os-cache}"
ICONS_DIR="${MCCONTROL_ICONS_DIR:-/opt/mccontrol/assets/icons}"
FORCE=0
if [[ "${1:-}" == "--force" ]]; then
  FORCE=1
fi

if [[ -z "$NS" || -z "$BN" ]]; then
  echo "OBJECT_STORAGE_NAMESPACE / OBJECT_STORAGE_BUCKET not set in env" >&2
  exit 0
fi

mkdir -p "$CACHE" "$ICONS_DIR"
FLAGS_LOCAL="$CACHE/flags.json"
FLAGS_TMP="$CACHE/flags.icons.put.json"
STAGING="$CACHE/icons-staging"
mkdir -p "$STAGING"

need_icons=0
if [[ "$FORCE" -eq 1 ]]; then
  need_icons=1
elif [[ -f "$FLAGS_LOCAL" ]]; then
  need_icons="$(python3 - "$FLAGS_LOCAL" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as f:
    flags = json.load(f)
msgs = flags.get("categories", {}).get("messages", {})
print(1 if msgs.get("door") else 0)
PY
)"
else
  need_icons=1
fi

if [[ "$need_icons" != "1" ]]; then
  echo "PULL_OS_ICONS skip (messages.door not dirty)"
  exit 0
fi

echo "== pull_os_icons ns=$NS bucket=$BN force=$FORCE =="

pull_one() {
  local object_name="$1"
  local dest_name="$2"
  local tmp="$STAGING/$dest_name"
  local err="$STAGING/$dest_name.err"
  rm -f "$tmp"
  set +e
  oci os object get -ns "$NS" -bn "$BN" --name "$object_name" --file "$tmp" 2>"$err"
  local rc=$?
  set -e
  if [[ "$rc" -eq 0 && -s "$tmp" ]]; then
    cp -f "$tmp" "$ICONS_DIR/$dest_name"
    echo "ICONS $dest_name ok"
    return 0
  fi
  if [[ -s "$err" ]] && grep -Eqi \
    'ObjectNotFound|The service returned error code 404|"status":[[:space:]]*404|status:[[:space:]]*404|The object .+ was not found' \
    "$err"; then
    echo "ICONS $dest_name missing (keep existing)"
    return 0
  fi
  echo "WARN: GET $object_name failed; keep existing $dest_name" >&2
  if [[ -s "$err" ]]; then
    cat "$err" >&2
  fi
  return 0
}

pull_one "messages/door-idle.png" "idle.png"
pull_one "messages/door-starting.png" "starting.png"
pull_one "messages/door-exhausted.png" "exhausted.png"

if [[ ! -f "$FLAGS_LOCAL" ]]; then
  echo "PULL_OS_ICONS_OK (no flags cache to clear)"
  exit 0
fi

python3 - "$FLAGS_LOCAL" "$FLAGS_TMP" <<'PY'
import json, sys
from datetime import datetime, timezone
flags_path, out_path = sys.argv[1:3]
with open(flags_path, encoding="utf-8") as f:
    flags = json.load(f)
cats = flags.setdefault("categories", {})
cats.setdefault("messages", {})["door"] = False
flags["updated_at"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(flags, f, indent=2)
    f.write("\n")
print("cleared messages.door")
PY

set +e
oci os object put -ns "$NS" -bn "$BN" --name meta/flags.json --file "$FLAGS_TMP" --force
put_rc=$?
set -e
if [[ "$put_rc" -eq 0 ]]; then
  cp -f "$FLAGS_TMP" "$FLAGS_LOCAL"
else
  echo "WARN: flags PUT after icon pull failed (icons still applied)" >&2
fi
echo "PULL_OS_ICONS_OK"
