#!/usr/bin/env bash
# After VM2→VM1:25565 TCP works, clear sticky STARTING and re-run wake so
# wait_forge + ip_to_vm1 complete → PLAYABLE. Run on the door.
set -euo pipefail

STATUS_URL="${STATUS_URL:-http://127.0.0.1:8080/api/status}"
WAKE_URL="${WAKE_URL:-http://127.0.0.1:8080/api/wake}"
DIAG="$(cd "$(dirname "$0")" && pwd)/diagnose_wait_forge.sh"
RESET="$(cd "$(dirname "$0")" && pwd)/reset_door_state.sh"

echo "=== pre-check: private TCP to Forge ==="
bash "$DIAG"

echo "=== reset sticky door state → IDLE ==="
bash "$RESET"

echo "=== POST /api/wake (async) ==="
curl -fsS -X POST "$WAKE_URL" -H "Content-Type: application/json" -d "{}" || true
echo

echo "=== waiting for PLAYABLE (up to 120s) ==="
deadline=$((SECONDS + 120))
while (( SECONDS < deadline )); do
  body="$(curl -fsS "$STATUS_URL" 2>/dev/null || true)"
  door="$(printf '%s' "$body" | python3 -c 'import json,sys; d=json.load(sys.stdin); print(d.get("door") or d.get("door_state") or "")' 2>/dev/null || true)"
  echo "  door=$door"
  case "$door" in
    PLAYABLE|DOOR_PLAYABLE)
      echo "$body"
      echo "Done. Clients should reach Forge on the reserved play IP."
      exit 0
      ;;
    DEGRADED|DOOR_DEGRADED)
      echo "$body"
      echo "Wake landed in DEGRADED — check journalctl -u mccontrol" >&2
      exit 1
      ;;
  esac
  sleep 5
done

echo "Timed out waiting for PLAYABLE; last status:" >&2
curl -s "$STATUS_URL" || true
echo >&2
exit 1
