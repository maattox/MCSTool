#!/usr/bin/env bash
set -euo pipefail
REPO=/mnt/c/Projects/AI/MinecraftServerDeploy
SMOKE=/tmp/mccontrol-client-test
pkill -f 'mccontrol .*mccontrol-client-test' 2>/dev/null || true
rm -rf "$SMOKE"
mkdir -p "$SMOKE/oci" "$SMOKE/data" "$SMOKE/web"
cp -a "$REPO/vm2/web/static" "$SMOKE/web/"
cp -a "$REPO/vm2/assets/icons" "$SMOKE/"

for s in start_vm1 stop_vm1 ip_to_vm1 ip_to_vm2 wait_forge; do
  printf '%s\n' '#!/usr/bin/env bash' 'echo "STUB: '"$s"'" >&2' 'exit 0' > "$SMOKE/oci/${s}.sh"
  chmod +x "$SMOKE/oci/${s}.sh"
done

cat > "$SMOKE/oci.env" <<'EOF'
INSTANCE_ID=ocid1.instance.oc1..smoke
RESERVED_PUBLIC_IP_ID=ocid1.publicip.oc1..smoke
VM1_PRIVATE_IP_ID=ocid1.privateip.oc1..vm1
VM2_PRIVATE_IP_ID=ocid1.privateip.oc1..vm2
VM1_PRIVATE_IP=127.0.0.1
WAIT_TIMEOUT_SEC=5
EOF

# Bind 0.0.0.0 so Windows Minecraft can reach WSL via localhost forwarding
cat > "$SMOKE/config.json" <<EOF
{
  "state_path": "$SMOKE/data/state.json",
  "ledger_path": "$SMOKE/data/ledger.json",
  "oci_dir": "$SMOKE/oci",
  "oci_env_file": "$SMOKE/oci.env",
  "web_root": "$SMOKE/web/static",
  "icons_dir": "$SMOKE/icons",
  "bind_host": "0.0.0.0",
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
make mccontrol >/dev/null
nohup "$REPO/vm2/build/mccontrol" "$SMOKE/config.json" >"$SMOKE/mccontrol.log" 2>&1 &
echo $! > "$SMOKE/mccontrol.pid"
sleep 1
if curl -sf "http://127.0.0.1:18080/api/status" >/dev/null; then
  echo "RUNNING pid=$(cat "$SMOKE/mccontrol.pid")"
  echo "MC: 127.0.0.1:25565"
  echo "UI: http://127.0.0.1:18080/"
  echo "Stop: kill \$(cat $SMOKE/mccontrol.pid)"
else
  echo "FAILED to start"
  tail -20 "$SMOKE/mccontrol.log"
  exit 1
fi
