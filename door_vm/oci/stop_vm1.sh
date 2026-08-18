#!/usr/bin/env bash
# Graceful Minecraft stop is VM1's job; this issues an OCI SOFTSTOP.
# No-op success if already STOPPED/STOPPING (idle SoftStop + reconcile
# idle-empty otherwise 409s "currently being modified" and used to look
# like handback failed even when ip_to_vm2 still ran).
set -euo pipefail
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"
INSTANCE_ID="${INSTANCE_ID-}"
INSTANCE_ID="${INSTANCE_ID//$'\r'/}"

: "${INSTANCE_ID:?INSTANCE_ID must be set}"

lifecycle() {
  local life
  life="$(oci compute instance get --instance-id "$INSTANCE_ID" \
    --query 'data."lifecycle-state"' --raw-output 2>/dev/null || true)"
  life="${life//$'\r'/}"
  printf '%s' "$life"
}

life="$(lifecycle)"
case "$life" in
  STOPPED|STOPPING)
    echo "stop_vm1: VM1 already $life — skip SOFTSTOP"
    exit 0
    ;;
esac

set +e
out="$(oci compute instance action \
  --instance-id "$INSTANCE_ID" \
  --action SOFTSTOP 2>&1)"
rc=$?
set -e
if [[ "$rc" -eq 0 ]]; then
  printf '%s\n' "$out"
  exit 0
fi

life="$(lifecycle)"
case "$life" in
  STOPPED|STOPPING)
    echo "stop_vm1: SOFTSTOP failed (exit $rc) but VM1 is $life — treating as success"
    exit 0
    ;;
esac

printf '%s\n' "$out" >&2
echo "stop_vm1: SOFTSTOP failed (exit $rc); VM1 lifecycle=${life:-unknown}" >&2
exit "$rc"
