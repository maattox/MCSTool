#!/usr/bin/env bash
# Move the reserved public IP to VM1's VNIC private IP.
# Also clears the door's one-shot ledger-heal latch so the next SoftStop
# will re-check Object Storage once.
set -euo pipefail
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"
RESERVED_PUBLIC_IP_ID="${RESERVED_PUBLIC_IP_ID-}"
RESERVED_PUBLIC_IP_ID="${RESERVED_PUBLIC_IP_ID//$'\r'/}"
VM1_PRIVATE_IP_ID="${VM1_PRIVATE_IP_ID-}"
VM1_PRIVATE_IP_ID="${VM1_PRIVATE_IP_ID//$'\r'/}"
OS_CACHE_DIR="${OS_CACHE_DIR:-/var/lib/mccontrol/os-cache}"
OS_CACHE_DIR="${OS_CACHE_DIR//$'\r'/}"
VERIFIED_FLAG="${LEDGER_HEAL_VERIFIED_FLAG:-$OS_CACHE_DIR/ledger_heal_verified}"

: "${RESERVED_PUBLIC_IP_ID:?RESERVED_PUBLIC_IP_ID must be set}"
: "${VM1_PRIVATE_IP_ID:?VM1_PRIVATE_IP_ID must be set}"

rm -f "$VERIFIED_FLAG" 2>/dev/null || true

current="$(oci network public-ip get --public-ip-id "$RESERVED_PUBLIC_IP_ID" \
  --query 'data."assigned-entity-id"' --raw-output 2>/dev/null || true)"
current="${current//$'\r'/}"
if [[ "$current" == "$VM1_PRIVATE_IP_ID" ]]; then
  echo "ip_to_vm1: reserved IP already on VM1 play private IP"
  exit 0
fi

# --force: move even when currently parked on the door secondary.
exec oci network public-ip update \
  --public-ip-id "$RESERVED_PUBLIC_IP_ID" \
  --private-ip-id "$VM1_PRIVATE_IP_ID" \
  --force
