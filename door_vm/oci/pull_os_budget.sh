#!/usr/bin/env bash
# Pull Object Storage ledger + budget + spend-brake lock for door wake gate
# (instance principal). Invoked by mccontrol via run_script before the budget
# / lock check when OS mode is on.
set -euo pipefail

# systemd oneshots often omit HOME; with `set -u` bare ${HOME} aborts the pull.
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:${HOME}/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"

NS="${OBJECT_STORAGE_NAMESPACE:-}"
BN="${OBJECT_STORAGE_BUCKET:-}"
CACHE="${OS_CACHE_DIR:-/var/lib/mccontrol/os-cache}"
FORCE=0
if [[ "${1:-}" == "--force" ]]; then
  FORCE=1
fi

if [[ -z "$NS" || -z "$BN" ]]; then
  echo "OBJECT_STORAGE_NAMESPACE / OBJECT_STORAGE_BUCKET not set in env" >&2
  exit 2
fi

mkdir -p "$CACHE"
FLAGS_LOCAL="$CACHE/flags.json"
LEDGER_LOCAL="$CACHE/usage.json"
BUDGET_LOCAL="$CACHE/budget.json"
LOCK_LOCAL="$CACHE/spend-brake-triggered.json"
LOCK_ERR="$CACHE/spend-brake-get.err"
FLAGS_TMP="$CACHE/flags.put.json"

echo "== pull_os_budget ns=$NS bucket=$BN force=$FORCE =="

oci os object get -ns "$NS" -bn "$BN" --name meta/flags.json --file "$FLAGS_LOCAL"

eval "$(python3 - "$FLAGS_LOCAL" "$LEDGER_LOCAL" "$BUDGET_LOCAL" "$FORCE" <<'PY'
import json, os, sys
flags_path, ledger_path, budget_path, force_s = sys.argv[1:5]
force = force_s == "1"
with open(flags_path, encoding="utf-8") as f:
    flags = json.load(f)
cats = flags.setdefault("categories", {})
ledger = cats.setdefault("ledger", {})
budget = cats.setdefault("budget", {})
need_ledger = force or bool(ledger.get("door")) or (not os.path.exists(ledger_path))
need_budget = force or bool(budget.get("door")) or (not os.path.exists(budget_path))
print(f"NEED_LEDGER={1 if need_ledger else 0}")
print(f"NEED_BUDGET={1 if need_budget else 0}")
PY
)"

# Spend-brake lock is not a dirty-flag category. GET on every pull (wake
# uses --force). Presence = locked even if JSON is malformed. 404 = unlocked
# (delete stale cache). Any other GET error fails this script so mccontrol
# will not START VM1 (fail closed). No extra Python.
echo "== get meta/spend-brake-triggered.json =="
set +e
oci os object get -ns "$NS" -bn "$BN" --name meta/spend-brake-triggered.json \
  --file "$LOCK_LOCAL" 2>"$LOCK_ERR"
lock_rc=$?
set -e
if [[ "$lock_rc" -eq 0 ]]; then
  echo "SPEND_BRAKE_LOCK=1"
elif [[ -s "$LOCK_ERR" ]] && grep -Eqi 'ObjectNotFound|status:[[:space:]]*404|The object .+ was not found' "$LOCK_ERR"; then
  rm -f "$LOCK_LOCAL"
  echo "SPEND_BRAKE_LOCK=0"
else
  echo "ERROR: spend-brake lock GET failed (not 404); fail closed" >&2
  if [[ -s "$LOCK_ERR" ]]; then
    cat "$LOCK_ERR" >&2
  fi
  exit 2
fi

if [[ "$NEED_LEDGER" -eq 1 ]]; then
  echo "== get ledger/usage.json =="
  if ! oci os object get -ns "$NS" -bn "$BN" --name ledger/usage.json --file "$LEDGER_LOCAL"; then
    echo "WARN: ledger/usage.json missing (first deploy); using empty ledger cache" >&2
    printf '%s\n' '{"version":2,"revision":0,"intervals":[]}' >"$LEDGER_LOCAL"
  fi
fi
if [[ "$NEED_BUDGET" -eq 1 ]]; then
  echo "== get budget/config.json =="
  if ! oci os object get -ns "$NS" -bn "$BN" --name budget/config.json --file "$BUDGET_LOCAL"; then
    echo "WARN: budget/config.json missing" >&2
    exit 2
  fi
fi

if [[ "$NEED_LEDGER" -eq 0 && "$NEED_BUDGET" -eq 0 ]]; then
  echo "PULL_OS_OK ledger=0 budget=0 (no door dirty flags; skipped flags put)"
  exit 0
fi

python3 - "$FLAGS_LOCAL" "$FLAGS_TMP" "$NEED_LEDGER" "$NEED_BUDGET" <<'PY'
import json, sys
from datetime import datetime, timezone
flags_path, out_path, need_ledger, need_budget = sys.argv[1:5]
with open(flags_path, encoding="utf-8") as f:
    flags = json.load(f)
cats = flags.setdefault("categories", {})
if need_ledger == "1":
    cats.setdefault("ledger", {})["door"] = False
if need_budget == "1":
    cats.setdefault("budget", {})["door"] = False
flags["updated_at"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
with open(out_path, "w", encoding="utf-8") as f:
    json.dump(flags, f, indent=2)
    f.write("\n")
print("cleared door flags where pulled")
PY

oci os object put -ns "$NS" -bn "$BN" --name meta/flags.json --file "$FLAGS_TMP" --force
# Keep local cache in sync with what we just wrote (summary tools read this file).
cp -f "$FLAGS_TMP" "$FLAGS_LOCAL"
echo "PULL_OS_OK ledger=$NEED_LEDGER budget=$NEED_BUDGET"

