#!/usr/bin/env bash
# Reset mccontrol door state to DOOR_IDLE (clears STARTING / DEGRADED sticky states).
# Run on the door VM:  sudo bash vm2/scripts/reset_door_state.sh
set -euo pipefail

STATE="${STATE_PATH:-/var/lib/mccontrol/state.json}"

if [[ "$(id -u)" -ne 0 ]]; then
  exec sudo bash "$0" "$@"
fi

systemctl stop mccontrol

python3 - "$STATE" <<'PY'
import json, sys
path = sys.argv[1]
with open(path, encoding="utf-8") as f:
    s = json.load(f)
print(f"before: door_state={s.get('door_state')!r} last_error={s.get('last_error')!r}")
s["door_state"] = "DOOR_IDLE"
s["last_error"] = ""
s["session_started_at"] = None
s["hard_stop_deadline"] = None
with open(path, "w", encoding="utf-8") as f:
    json.dump(s, f, indent=2)
    f.write("\n")
print("after:  door_state=DOOR_IDLE")
PY

systemctl start mccontrol
sleep 1
curl -s http://127.0.0.1:8080/api/status || true
echo
