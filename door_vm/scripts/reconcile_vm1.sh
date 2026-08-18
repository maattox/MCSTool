#!/usr/bin/env bash
# If door thinks a session is up but VM1 is already STOPPED (console / other tool),
# run the normal stop handback: SOFTSTOP (no-op-ish) + reserved IP → door + DOOR_IDLE.
# If STARTING/DEGRADED, VM1 RUNNING, wake idle, and private :25565 accepts TCP,
# promote_playable (wait_forge timeout recovery — do not race a live wake thread).
# If the door is already DOOR_IDLE / BUDGET_EXHAUSTED / SPEND_BRAKE, still run ip_to_vm2 (idempotent)
# so a reset or a persist-idle-before-IP-move cannot leave the play address on VM1.
#
# Object Storage policy (minimize GETs/PUTs):
# - Do NOT pull budget/ledger on every tick — wake (`do_wake` / pull_os_budget.sh)
#   owns budget freshness.
# - Ledger heal runs at most once per "VM1 down" episode, tracked by a local flag
#   file under OS_CACHE_DIR. Cleared when VM1 is RUNNING again (and by ip_to_vm1).
#
# Intended as a oneshot from mccontrol-reconcile.timer on the door.
#
# Phase 5: orphan heal runs only when VM1 is STOPPED (not STOPPING), and
# heal_os_ledger.sh closes at lease heartbeat when present.
set -uo pipefail

ENV_FILE="${OCI_ENV_FILE:-/etc/mccontrol/oci.env}"
STATUS_URL="${STATUS_URL:-http://127.0.0.1:8080/api/status}"
IDLE_URL="${IDLE_URL:-http://127.0.0.1:8080/api/idle-empty}"
HEAL_SCRIPT="${HEAL_SCRIPT:-/opt/mccontrol/oci/heal_os_ledger.sh}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export HOME="${HOME:-/home/ubuntu}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source <(tr -d '\r' < "$ENV_FILE")
  set +a
fi

INSTANCE_ID="${INSTANCE_ID-}"
INSTANCE_ID="${INSTANCE_ID//$'\r'/}"
OS_CACHE_DIR="${OS_CACHE_DIR:-/var/lib/mccontrol/os-cache}"
OS_CACHE_DIR="${OS_CACHE_DIR//$'\r'/}"
VERIFIED_FLAG="${LEDGER_HEAL_VERIFIED_FLAG:-$OS_CACHE_DIR/ledger_heal_verified}"
LEDGER_LOCAL="${OS_CACHE_DIR}/usage.json"

if [[ -z "${INSTANCE_ID:-}" ]]; then
  echo "reconcile: INSTANCE_ID not set; cannot run" >&2
  exit 1
fi

mkdir -p "$OS_CACHE_DIR"

door_state=""
wake_in_progress="false"
if door_json="$(curl -fsS --max-time 15 "$STATUS_URL" 2>/dev/null)"; then
  door_state="$(printf '%s' "$door_json" | python3 -c '
import json, sys
d = json.load(sys.stdin)
print(d.get("door_state") or d.get("door") or "")
' 2>/dev/null || true)"
  wake_in_progress="$(printf '%s' "$door_json" | python3 -c '
import json, sys
d = json.load(sys.stdin)
print("true" if d.get("wake_in_progress") else "false")
' 2>/dev/null || true)"
fi
door_state="${door_state//$'\r'/}"
wake_in_progress="${wake_in_progress//$'\r'/}"
echo "reconcile: door_state=${door_state:-unknown} wake_in_progress=${wake_in_progress}"

lifecycle=""
if lifecycle="$(oci compute instance get --instance-id "$INSTANCE_ID" \
  --query 'data."lifecycle-state"' --raw-output 2>/dev/null)"; then
  :
else
  echo "reconcile: warning: failed to query VM1 lifecycle" >&2
  lifecycle=""
fi
lifecycle="${lifecycle//$'\r'/}"
echo "reconcile: vm1_lifecycle=${lifecycle:-unknown}"

vm1_game_tcp() {
  local ip="${VM1_PRIVATE_IP-}"
  ip="${ip//$'\r'/}"
  [[ -n "$ip" ]] || return 1
  timeout 5 bash -c "echo >/dev/tcp/${ip}/25565" 2>/dev/null
}

case "$door_state" in
  PLAYABLE|STARTING|DEGRADED|DOOR_PLAYABLE|DOOR_STARTING|DOOR_DEGRADED)
    case "$lifecycle" in
      STOPPED|STOPPING)
        echo "reconcile: door=$door_state vm1=$lifecycle → POST idle-empty"
        curl -fsS --max-time 120 -X POST "$IDLE_URL" || \
          echo "reconcile: warning: idle-empty POST failed" >&2
        echo
        ;;
      RUNNING)
        case "$door_state" in
          PLAYABLE|DOOR_PLAYABLE)
            ;;
          *)
            # wait_forge timeout / sticky STARTING: game may already be up. Do not
            # race a live wake thread (promote_playable stops mccontrol).
            if [[ "$wake_in_progress" == "true" ]]; then
              echo "reconcile: door=$door_state vm1=RUNNING wake in progress — leave wait_forge"
            elif vm1_game_tcp; then
              PROMOTE="${PROMOTE_PLAYABLE:-/opt/mccontrol/scripts/promote_playable.sh}"
              if [[ -x "$PROMOTE" ]]; then
                echo "reconcile: door=$door_state vm1=RUNNING TCP :25565 ok → promote_playable"
                bash -- "$PROMOTE" || \
                  echo "reconcile: warning: promote_playable failed" >&2
              else
                echo "reconcile: warning: promote_playable missing ($PROMOTE)" >&2
              fi
            else
              echo "reconcile: door=$door_state vm1=RUNNING but :25565 not accepting yet"
            fi
            ;;
        esac
        ;;
    esac
    ;;
  DOOR_IDLE|IDLE|BUDGET_EXHAUSTED|DOOR_BUDGET_EXHAUSTED|SPEND_BRAKE|DOOR_SPEND_BRAKE)
    # State already idle, but the reserved IP can still sit on STOPPED VM1
    # (crash between persist DOOR_IDLE and ip_to_vm2, or reset_door_state.sh).
    case "$lifecycle" in
      STOPPED|STOPPING)
        IP_TO_VM2="${IP_TO_VM2:-/opt/mccontrol/oci/ip_to_vm2.sh}"
        if [[ -x "$IP_TO_VM2" ]]; then
          echo "reconcile: door=$door_state vm1=$lifecycle → ensure ip_to_vm2"
          bash -- "$IP_TO_VM2" || \
            echo "reconcile: warning: ip_to_vm2 failed" >&2
        else
          echo "reconcile: warning: ip_to_vm2 missing ($IP_TO_VM2)" >&2
        fi
        ;;
    esac
    ;;
esac

clear_verified() {
  rm -f "$VERIFIED_FLAG"
}

mark_verified() {
  date -u +"%Y-%m-%dT%H:%M:%SZ" >"$VERIFIED_FLAG" 2>/dev/null || \
    echo "verified" >"$VERIFIED_FLAG"
}

local_has_open_interval() {
  [[ -f "$LEDGER_LOCAL" ]] || return 1
  python3 - "$LEDGER_LOCAL" <<'PY'
import json, sys
try:
    with open(sys.argv[1], encoding="utf-8") as f:
        data = json.load(f)
except (OSError, json.JSONDecodeError):
    raise SystemExit(1)
for item in data.get("intervals") or []:
    if isinstance(item, dict) and not item.get("stopped_at"):
        raise SystemExit(0)
raise SystemExit(1)
PY
}

# Playing / starting: clear stop-heal latch so the next SoftStop re-checks OS once.
case "$lifecycle" in
  RUNNING|STARTING|PROVISIONING)
    if [[ -f "$VERIFIED_FLAG" ]]; then
      echo "reconcile: VM1 $lifecycle → clear ledger_heal_verified"
      clear_verified
    fi
    echo "reconcile: skip OS pull/heal (VM1 up; wake path owns budget pull)"
    exit 0
    ;;
esac

# Heal only when fully STOPPED (not STOPPING) so VM1 SoftStop publish can finish.
case "$lifecycle" in
  STOPPED) ;;
  STOPPING)
    echo "reconcile: skip OS heal (lifecycle=STOPPING; wait for STOPPED)"
    exit 0
    ;;
  "")
    # Lifecycle probe failed — only heal if we have not already verified this episode.
    echo "reconcile: warning: lifecycle unknown; heal only if latch unset"
    ;;
  *)
    echo "reconcile: skip OS heal (lifecycle=$lifecycle)"
    exit 0
    ;;
esac

# Down episode: at most one successful OS heal unless local cache still shows open.
if [[ -f "$VERIFIED_FLAG" ]]; then
  if local_has_open_interval; then
    echo "reconcile: verified flag set but local cache has open interval → re-heal"
    clear_verified
  else
    echo "reconcile: skip OS heal (ledger_heal_verified set; VM1 down, cache clean)"
    exit 0
  fi
fi

if [[ ! -x "$HEAL_SCRIPT" ]]; then
  echo "reconcile: heal script missing ($HEAL_SCRIPT)" >&2
  exit 0
fi

echo "reconcile: running heal_os_ledger.sh (first check this stop episode)"
if bash -- "$HEAL_SCRIPT"; then
  # Heal updates local cache on success; treat closed-or-already-clean as verified.
  if local_has_open_interval; then
    echo "reconcile: heal returned ok but local cache still open; not marking verified" >&2
  else
    mark_verified
    echo "reconcile: heal finished ok; marked ledger_heal_verified"
  fi
else
  echo "reconcile: heal_os_ledger warning (exit $?) — will retry next tick" >&2
fi

exit 0
