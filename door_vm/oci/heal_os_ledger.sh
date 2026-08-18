#!/usr/bin/env bash
# Phase 5: if VM1 is STOPPED (not STOPPING) and Object Storage ledger still has
# an open interval, close it using lease last_heartbeat_at when available
# (else wall clock), mark stop_uncertain, and republish.
# Invoked from reconcile_vm1.sh (and Testing2). Uses OCI CLI + python3 only.
set -euo pipefail

# systemd oneshots often omit HOME; with `set -u` bare ${HOME} aborts before heal.
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:${HOME}/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"

ENV_FILE="${OCI_ENV_FILE:-/etc/mccontrol/oci.env}"
if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source <(tr -d '\r' < "$ENV_FILE")
  set +a
fi

NS="${OBJECT_STORAGE_NAMESPACE:-}"
BN="${OBJECT_STORAGE_BUCKET:-}"
CACHE="${OS_CACHE_DIR:-/var/lib/mccontrol/os-cache}"
INSTANCE_ID="${INSTANCE_ID:-}"
# Seconds after last heartbeat before heal will close (default 15 min ≈ 2×5m + buffer).
LEASE_STALE_GRACE_SEC="${LEASE_STALE_GRACE_SEC:-900}"

if [[ -z "$NS" || -z "$BN" ]]; then
  echo "OBJECT_STORAGE_NAMESPACE / OBJECT_STORAGE_BUCKET not set; skip heal" >&2
  exit 0
fi
if [[ -z "$INSTANCE_ID" ]]; then
  echo "INSTANCE_ID not set; skip heal" >&2
  exit 0
fi

mkdir -p "$CACHE"
LEDGER_LOCAL="$CACHE/usage.json"
LEASE_LOCAL="$CACHE/lease.json"
FLAGS_LOCAL="$CACHE/flags.json"
FLAGS_TMP="$CACHE/flags.heal.put.json"
LEDGER_TMP="$CACHE/usage.heal.put.json"
rm -f "$LEDGER_TMP" "$FLAGS_TMP" "${LEDGER_TMP}.count"

echo "== heal_os_ledger ns=$NS bucket=$BN =="

lifecycle="$(oci compute instance get --instance-id "$INSTANCE_ID" \
  --query 'data."lifecycle-state"' --raw-output)"
lifecycle="${lifecycle//$'\r'/}"
echo "vm1 lifecycle=$lifecycle"

# Only heal when fully STOPPED so SoftStop publish during STOPPING is not raced.
case "$lifecycle" in
  STOPPED) ;;
  STOPPING)
    echo "HEAL_SKIP lifecycle=STOPPING (wait until STOPPED so VM1 can finish publish)"
    exit 0
    ;;
  *)
    echo "HEAL_SKIP lifecycle=$lifecycle (VM1 not STOPPED)"
    exit 0
    ;;
esac

echo "== get ledger/usage.json (force) =="
oci os object get -ns "$NS" -bn "$BN" --name ledger/usage.json --file "$LEDGER_LOCAL"
oci os object get -ns "$NS" -bn "$BN" --name meta/flags.json --file "$FLAGS_LOCAL" \
  2>/dev/null || true
echo "== get ledger/lease.json (best-effort) =="
if oci os object get -ns "$NS" -bn "$BN" --name ledger/lease.json --file "$LEASE_LOCAL" \
  2>/dev/null; then
  :
else
  rm -f "$LEASE_LOCAL"
  echo "lease.json missing; will use wall clock if heal needed"
fi

python3 - "$LEDGER_LOCAL" "$LEDGER_TMP" "$FLAGS_LOCAL" "$FLAGS_TMP" \
  "$LEASE_LOCAL" "$LEASE_STALE_GRACE_SEC" <<'PY'
import json, os, sys
from datetime import datetime, timezone

ledger_path, ledger_out, flags_path, flags_out, lease_path, grace_s = sys.argv[1:7]
grace = float(grace_s or "900")
now_dt = datetime.now(timezone.utc)
now = now_dt.strftime("%Y-%m-%dT%H:%M:%SZ")

def parse_iso(value):
    if not value:
        return None
    text = str(value).strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(text)
    except ValueError:
        return None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)

with open(ledger_path, encoding="utf-8") as f:
    ledger = json.load(f)
if not isinstance(ledger, dict):
    raise SystemExit("ledger is not a JSON object")

intervals = ledger.get("intervals")
if not isinstance(intervals, list):
    intervals = []
    ledger["intervals"] = intervals

opens = [
    item for item in intervals
    if isinstance(item, dict) and not item.get("stopped_at")
]
if not opens:
    print("HEAL_SKIP no_open_intervals")
    raise SystemExit(0)

lease = None
if lease_path and os.path.exists(lease_path):
    try:
        with open(lease_path, encoding="utf-8") as f:
            loaded = json.load(f)
        if isinstance(loaded, dict):
            lease = loaded
    except (OSError, json.JSONDecodeError):
        lease = None

hb = parse_iso(lease.get("last_heartbeat_at")) if lease else None
age = None
if hb is not None:
    age = max(0.0, (now_dt - hb).total_seconds())

# VM1 is STOPPED: always close orphans. Prefer lease heartbeat over wall clock.
if hb is not None:
    stop_at = hb.strftime("%Y-%m-%dT%H:%M:%SZ")
    reason = (
        "VM1 STOPPED with open interval; door closed at lease last_heartbeat_at"
    )
    print(f"HEAL_USE_LEASE heartbeat={stop_at} age_sec={age}")
else:
    stop_at = now
    reason = (
        "VM1 STOPPED with open interval; no lease heartbeat — "
        "door closed stop time as approximate wall clock"
    )
    print("HEAL_USE_CLOCK (no lease heartbeat)")

closed = 0
for item in opens:
    started = parse_iso(item.get("started_at"))
    stamp = stop_at
    if started is not None:
        stop_dt = parse_iso(stamp) or now_dt
        if stop_dt < started:
            stamp = started.strftime("%Y-%m-%dT%H:%M:%SZ")
    item["stopped_at"] = stamp
    item["stop_source"] = "door_reconcile"
    item["stop_uncertain"] = True
    item["uncertain_reason"] = reason
    closed += 1

ledger["idle_since"] = None
try:
    rev = int(ledger.get("revision") or 0) + 1
except (TypeError, ValueError):
    rev = 1
ledger["revision"] = rev
ledger["version"] = max(int(ledger.get("version") or 1), 2)

with open(ledger_out, "w", encoding="utf-8") as f:
    json.dump(ledger, f, indent=2)
    f.write("\n")

flags = {
    "version": 1,
    "updated_at": now,
    "categories": {
        cat: {"manager": False, "door": False, "vm1": False}
        for cat in ("ledger", "budget", "meta", "ip", "messages")
    },
    "help": (
        "When a writer updates a category, set that category's consumer "
        "flags to true so each side knows to pull. Consumers clear only "
        "their own flag after a successful pull."
    ),
}
try:
    with open(flags_path, encoding="utf-8") as f:
        loaded = json.load(f)
    if isinstance(loaded, dict):
        flags["version"] = int(loaded.get("version") or 1)
        if isinstance(loaded.get("categories"), dict):
            for cat, row in loaded["categories"].items():
                if cat in flags["categories"] and isinstance(row, dict):
                    for k in ("manager", "door", "vm1"):
                        if k in row:
                            flags["categories"][cat][k] = bool(row[k])
        if loaded.get("help"):
            flags["help"] = loaded["help"]
except (OSError, json.JSONDecodeError):
    pass

# Door wrote the ledger heal: dirty manager + vm1; door already has it.
flags["categories"]["ledger"]["manager"] = True
flags["categories"]["ledger"]["door"] = False
flags["categories"]["ledger"]["vm1"] = True
flags["updated_at"] = now

with open(flags_out, "w", encoding="utf-8") as f:
    json.dump(flags, f, indent=2)
    f.write("\n")

print(f"HEAL_CLOSED={closed}")
with open(ledger_out + ".count", "w", encoding="utf-8") as f:
    f.write(str(closed))
PY

COUNT_FILE="${LEDGER_TMP}.count"
if [[ ! -f "$LEDGER_TMP" || ! -f "$COUNT_FILE" ]]; then
  echo "HEAL_OS_OK closed=0"
  exit 0
fi
CLOSED_N="$(tr -d "[:space:]" < "$COUNT_FILE")"

echo "== put ledger/usage.json (closed=$CLOSED_N) =="
oci os object put -ns "$NS" -bn "$BN" --name ledger/usage.json --file "$LEDGER_TMP" --force
cp -f "$LEDGER_TMP" "$LEDGER_LOCAL"

# Mark lease inactive after heal so readers do not treat it as live.
python3 - "$LEASE_LOCAL" <<'PY'
import json, os, sys
from datetime import datetime, timezone
path = sys.argv[1]
now = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
lease = {
    "version": 1,
    "active": False,
    "session_id": None,
    "interval_id": None,
    "started_at": None,
    "last_heartbeat_at": None,
    "ocpus": None,
    "memory_gb": None,
    "updated_at": now,
    "cleared_at": now,
    "clear_reason": "door_heal",
}
if path and os.path.exists(path):
    try:
        with open(path, encoding="utf-8") as f:
            loaded = json.load(f)
        if isinstance(loaded, dict):
            for k in ("session_id", "interval_id", "started_at", "last_heartbeat_at", "ocpus", "memory_gb"):
                if loaded.get(k) is not None:
                    lease[k] = loaded.get(k)
            lease["active"] = False
            lease["cleared_at"] = now
            lease["clear_reason"] = "door_heal"
            lease["updated_at"] = now
    except (OSError, json.JSONDecodeError):
        pass
with open(path, "w", encoding="utf-8") as f:
    json.dump(lease, f, indent=2)
    f.write("\n")
print("HEAL_LEASE_CLEARED")
PY
if [[ -f "$LEASE_LOCAL" ]]; then
  echo "== put ledger/lease.json (cleared) =="
  oci os object put -ns "$NS" -bn "$BN" --name ledger/lease.json --file "$LEASE_LOCAL" --force
fi

echo "== put meta/flags.json =="
oci os object put -ns "$NS" -bn "$BN" --name meta/flags.json --file "$FLAGS_TMP" --force
cp -f "$FLAGS_TMP" "$FLAGS_LOCAL"

# Reload mccontrol memory if the refresh endpoint exists (best-effort).
curl -sS -m 60 -X POST http://127.0.0.1:8080/api/os-refresh >/dev/null 2>&1 || true

rm -f "$LEDGER_TMP" "$FLAGS_TMP" "$COUNT_FILE"
echo "HEAL_OS_OK closed=$CLOSED_N"
