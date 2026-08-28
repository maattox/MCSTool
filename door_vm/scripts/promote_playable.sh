#!/usr/bin/env bash
# Setup / repair: VM1 is already RUNNING with Minecraft up. Move the reserved
# play IP to VM1 and persist PLAYABLE so friends are not left on a doorbell
# (or a black hole if wake START 409s). Run as root on the door.
set -euo pipefail
export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"
export OCI_CLI_AUTH="${OCI_CLI_AUTH:-instance_principal}"

ENV_FILE="${OCI_ENV_FILE:-/etc/mccontrol/oci.env}"
STATE="${STATE_PATH:-/var/lib/mccontrol/state.json}"
IP_TO_VM1="${IP_TO_VM1:-/opt/mccontrol/oci/ip_to_vm1.sh}"

if [[ "$(id -u)" -ne 0 ]]; then
  exec sudo bash "$0" "$@"
fi

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source <(tr -d '\r' < "$ENV_FILE")
  set +a
fi

echo "promote_playable: moving reserved IP to VM1 (before flipping PLAYABLE)"
bash -- "$IP_TO_VM1"

: "${RESERVED_PUBLIC_IP_ID:?RESERVED_PUBLIC_IP_ID must be set}"
: "${VM1_PRIVATE_IP_ID:?VM1_PRIVATE_IP_ID must be set}"
assigned=""
for _ in 1 2 3 4 5 6; do
  assigned="$(oci network public-ip get --public-ip-id "$RESERVED_PUBLIC_IP_ID" \
    --query 'data."assigned-entity-id"' --raw-output 2>/dev/null || true)"
  assigned="${assigned//$'\r'/}"
  if [[ "$assigned" == "$VM1_PRIVATE_IP_ID" ]]; then
    break
  fi
  sleep 2
done
if [[ "$assigned" != "$VM1_PRIVATE_IP_ID" ]]; then
  echo "promote_playable: reserved IP is not on VM1 (assigned-entity-id=${assigned:-empty})" >&2
  exit 1
fi

systemctl stop mccontrol.service 2>/dev/null || true

python3 - "$STATE" <<'PY'
import json, os, sys
from datetime import datetime, timezone

path = sys.argv[1]
s = {}
if os.path.exists(path):
    with open(path, encoding="utf-8") as f:
        s = json.load(f)
print(f"before: door_state={s.get('door_state')!r} last_error={s.get('last_error')!r}")
s["door_state"] = "PLAYABLE"
s["last_error"] = ""
if not s.get("session_started_at"):
    s["session_started_at"] = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
with open(path, "w", encoding="utf-8") as f:
    json.dump(s, f, indent=2)
    f.write("\n")
print("after:  door_state=PLAYABLE")
PY

systemctl start mccontrol.service
sleep 1
curl -sS --max-time 5 http://127.0.0.1:8080/api/status || true
echo
echo "promote_playable: done"
