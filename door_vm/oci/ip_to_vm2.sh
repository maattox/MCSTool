#!/usr/bin/env bash
# Move the reserved public IP back to VM2's VNIC private IP.
set -euo pipefail
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"
RESERVED_PUBLIC_IP_ID="${RESERVED_PUBLIC_IP_ID-}"
RESERVED_PUBLIC_IP_ID="${RESERVED_PUBLIC_IP_ID//$'\r'/}"
VM2_PRIVATE_IP_ID="${VM2_PRIVATE_IP_ID-}"
VM2_PRIVATE_IP_ID="${VM2_PRIVATE_IP_ID//$'\r'/}"

: "${RESERVED_PUBLIC_IP_ID:?RESERVED_PUBLIC_IP_ID must be set}"
: "${VM2_PRIVATE_IP_ID:?VM2_PRIVATE_IP_ID must be set}"

current="$(oci network public-ip get --public-ip-id "$RESERVED_PUBLIC_IP_ID" \
  --query 'data."assigned-entity-id"' --raw-output 2>/dev/null || true)"
current="${current//$'\r'/}"
if [[ "$current" == "$VM2_PRIVATE_IP_ID" ]]; then
  echo "ip_to_vm2: reserved IP already on door play private IP"
  exit 0
fi

# --force: move reserved IP even when currently on another private IP (VM1 play).
exec oci network public-ip update \
  --public-ip-id "$RESERVED_PUBLIC_IP_ID" \
  --private-ip-id "$VM2_PRIVATE_IP_ID" \
  --force
