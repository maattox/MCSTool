#!/usr/bin/env bash
# Start VM1 via OCI. No-op success if already RUNNING. If STARTING (or START
# just accepted), wait until RUNNING before returning so wait_forge does not
# burn its TCP timeout during the compute lifecycle.
# Caller records budget session start after success.
set -euo pipefail
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"
INSTANCE_ID="${INSTANCE_ID-}"
INSTANCE_ID="${INSTANCE_ID//$'\r'/}"
# SDK-style waiter: few seconds → max 30s, ~20 min (docs/OCI-API-Usage.md).
START_WAIT_TIMEOUT_SEC="${START_WAIT_TIMEOUT_SEC:-1200}"
START_WAIT_TIMEOUT_SEC="${START_WAIT_TIMEOUT_SEC//$'\r'/}"

: "${INSTANCE_ID:?INSTANCE_ID must be set}"

lifecycle() {
  local life
  life="$(oci compute instance get --instance-id "$INSTANCE_ID" \
    --query 'data."lifecycle-state"' --raw-output 2>/dev/null || true)"
  life="${life//$'\r'/}"
  printf '%s' "$life"
}

wait_running() {
  local timeout="$START_WAIT_TIMEOUT_SEC"
  local delay=5
  local start_ts=$SECONDS
  local life=""
  echo "start_vm1: waiting for RUNNING (timeout ${timeout}s)..."
  while (( SECONDS - start_ts < timeout )); do
    life="$(lifecycle)"
    case "$life" in
      RUNNING)
        echo "start_vm1: VM1 RUNNING after $((SECONDS - start_ts))s"
        return 0
        ;;
      STOPPING|STOPPED)
        echo "start_vm1: VM1 became $life while waiting for RUNNING" >&2
        return 1
        ;;
    esac
    echo "  start_vm1: lifecycle=${life:-unknown} ($((SECONDS - start_ts))s elapsed)..."
    sleep "$delay"
    if (( delay < 30 )); then
      delay=$(( delay + delay / 2 ))
      if (( delay > 30 )); then
        delay=30
      fi
    fi
  done
  echo "start_vm1: timeout after ${timeout}s waiting for RUNNING (last=${life:-unknown})" >&2
  return 1
}

life="$(lifecycle)"
case "$life" in
  RUNNING)
    echo "start_vm1: VM1 already RUNNING — skip START"
    exit 0
    ;;
  STARTING)
    echo "start_vm1: VM1 already STARTING — skip START"
    wait_running
    exit $?
    ;;
esac

set +e
out="$(oci compute instance action \
  --instance-id "$INSTANCE_ID" \
  --action START 2>&1)"
rc=$?
set -e
if [[ "$rc" -eq 0 ]]; then
  printf '%s\n' "$out"
  wait_running
  exit $?
fi

life="$(lifecycle)"
case "$life" in
  RUNNING)
    echo "start_vm1: START failed (exit $rc) but VM1 is $life — treating as success"
    exit 0
    ;;
  STARTING)
    echo "start_vm1: START failed (exit $rc) but VM1 is $life — waiting for RUNNING"
    wait_running
    exit $?
    ;;
esac

printf '%s\n' "$out" >&2
echo "start_vm1: START failed (exit $rc); VM1 lifecycle=${life:-unknown}" >&2
exit "$rc"
