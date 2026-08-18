#!/usr/bin/env bash
set -euo pipefail
REPO=/mnt/c/Projects/AI/MinecraftServerDeploy
SMOKE=/tmp/mccontrol-smoke
rm -rf "$SMOKE"
mkdir -p "$SMOKE/oci" "$SMOKE/data" "$SMOKE/web"
cp -a "$REPO/vm2/web/static" "$SMOKE/web/"
cp -a "$REPO/vm2/assets/icons" "$SMOKE/"

for s in start_vm1 stop_vm1 ip_to_vm1 ip_to_vm2 wait_forge; do
  cat > "$SMOKE/oci/${s}.sh" <<EOF
#!/usr/bin/env bash
echo "STUB: ${s} \$*" >&2
exit 0
EOF
  chmod +x "$SMOKE/oci/${s}.sh"
done

# control.c likely invokes start_vm1.sh etc. — also provide bare names
for s in start_vm1 stop_vm1 ip_to_vm1 ip_to_vm2 wait_forge; do
  ln -sf "${s}.sh" "$SMOKE/oci/${s}"
done

cat > "$SMOKE/oci.env" <<'EOF'
INSTANCE_ID=ocid1.instance.oc1..smoke
RESERVED_PUBLIC_IP_ID=ocid1.publicip.oc1..smoke
VM1_PRIVATE_IP_ID=ocid1.privateip.oc1..vm1
VM2_PRIVATE_IP_ID=ocid1.privateip.oc1..vm2
VM1_PRIVATE_IP=127.0.0.1
WAIT_TIMEOUT_SEC=5
EOF

cat > "$SMOKE/config.json" <<EOF
{
  "state_path": "$SMOKE/data/state.json",
  "ledger_path": "$SMOKE/data/ledger.json",
  "oci_dir": "$SMOKE/oci",
  "oci_env_file": "$SMOKE/oci.env",
  "web_root": "$SMOKE/web/static",
  "icons_dir": "$SMOKE/icons",
  "bind_host": "127.0.0.1",
  "http_port": 18080,
  "mc_port": 25565,
  "daily_ocpu_limit": 45,
  "ocpus": 4,
  "vm1_private_ip": "127.0.0.1",
  "enable_mcdoor": true,
  "enable_http": true,
  "keepalive_enabled": false,
  "keepalive_interval_sec": 7200,
  "keepalive_burst_sec": 10
}
EOF

cd "$REPO/vm2"
make mccontrol
"$REPO/vm2/build/mccontrol" "$SMOKE/config.json" >"$SMOKE/mccontrol.log" 2>&1 &
MCPID=$!
echo "mccontrol pid=$MCPID"
sleep 1

# Wait for HTTP
for i in 1 2 3 4 5 6 7 8 9 10; do
  if curl -sf "http://127.0.0.1:18080/api/status" >/tmp/mcstatus.json; then
    break
  fi
  sleep 0.5
done

echo "=== /api/status ==="
cat /tmp/mcstatus.json
echo

echo "=== status_ping ==="
cd "$REPO"
PYTHONPATH=. python3 - <<'PY'
from shared.mc_status import status_ping
import json
r = status_ping("127.0.0.1", 25565, timeout=3.0)
print(json.dumps({"online": r["players"]["online"], "description": r.get("description")}, indent=2))
PY

echo "=== POST /api/wake ==="
curl -sf -X POST "http://127.0.0.1:18080/api/wake" -H "Content-Type: application/json" -d "{}" || true
echo
sleep 1
curl -sf "http://127.0.0.1:18080/api/status"
echo

echo "=== UI index ==="
curl -sf -o /dev/null -w "%{http_code}\n" "http://127.0.0.1:18080/"

kill "$MCPID" 2>/dev/null || true
wait "$MCPID" 2>/dev/null || true
echo "=== mccontrol.log (tail) ==="
tail -40 "$SMOKE/mccontrol.log"
echo DONE
