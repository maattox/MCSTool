#!/usr/bin/env bash
# Poll VM1 private IP :25565 until TCP accepts or timeout.
set -euo pipefail
# ${VAR-} / ${VAR:-default} first: `set -u` + ${UNSET//cr} aborts before defaults.
VM1_PRIVATE_IP="${VM1_PRIVATE_IP-}"
VM1_PRIVATE_IP="${VM1_PRIVATE_IP//$'\r'/}"
WAIT_TIMEOUT_SEC="${WAIT_TIMEOUT_SEC:-600}"
WAIT_TIMEOUT_SEC="${WAIT_TIMEOUT_SEC//$'\r'/}"
POLL_INTERVAL_SEC="${POLL_INTERVAL_SEC:-10}"
POLL_INTERVAL_SEC="${POLL_INTERVAL_SEC//$'\r'/}"

: "${VM1_PRIVATE_IP:?VM1_PRIVATE_IP must be set}"

HOST="$VM1_PRIVATE_IP"
PORT=25565

try_tcp() {
  # Cap each probe: a DROP (no RST) can hang /dev/tcp for minutes and
  # push wait_forge well past WAIT_TIMEOUT_SEC, leaving Manager on Starting….
  if command -v timeout >/dev/null 2>&1; then
    timeout 5 bash -c "echo >/dev/tcp/${HOST}/${PORT}" 2>/dev/null
  else
    (echo >/dev/tcp/"$HOST"/"$PORT") 2>/dev/null
  fi
}

start_ts=$SECONDS
echo "Waiting for Forge on ${HOST}:${PORT} (timeout ${WAIT_TIMEOUT_SEC}s)..."

while (( SECONDS - start_ts < WAIT_TIMEOUT_SEC )); do
  if try_tcp; then
    elapsed=$((SECONDS - start_ts))
    echo "Forge accepting TCP on ${HOST}:${PORT} after ${elapsed}s"
    exit 0
  fi

  elapsed=$((SECONDS - start_ts))
  remaining=$((WAIT_TIMEOUT_SEC - elapsed))
  echo "  still waiting (${elapsed}s elapsed, ${remaining}s remaining)..."
  sleep "$POLL_INTERVAL_SEC"
done

echo "Timeout after ${WAIT_TIMEOUT_SEC}s waiting for ${HOST}:${PORT}" >&2
exit 1
