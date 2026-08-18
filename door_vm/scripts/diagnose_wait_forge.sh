#!/usr/bin/env bash
# Diagnose door hang on wait_forge ("still waiting") — run on VM2 (door).
# Prints config IPs, live TCP probe, and actionable hints.
set -euo pipefail

ENV_FILE="${OCI_ENV_FILE:-/etc/mccontrol/oci.env}"
CFG_FILE="${MCCONTROL_CONFIG:-/etc/mccontrol/config.json}"
PORT=25565

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source <(tr -d '\r' < "$ENV_FILE")
  set +a
fi

ENV_IP="${VM1_PRIVATE_IP:-}"
CFG_IP=""
if [[ -f "$CFG_FILE" ]]; then
  CFG_IP="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1])).get("vm1_private_ip") or "")' "$CFG_FILE" 2>/dev/null || true)"
fi

IP="${ENV_IP:-$CFG_IP}"
IP="${IP//$'\r'/}"

echo "=== wait_forge diagnosis (door) ==="
echo "oci.env VM1_PRIVATE_IP: ${ENV_IP:-<unset>}"
echo "config.json vm1_private_ip: ${CFG_IP:-<unset>}"
echo "probe target: ${IP:-<none>}:$PORT"
echo

if [[ -z "$IP" ]]; then
  echo "FAIL: no VM1_PRIVATE_IP configured"
  exit 1
fi

if [[ -n "$ENV_IP" && -n "$CFG_IP" && "$ENV_IP" != "$CFG_IP" ]]; then
  echo "WARN: oci.env and config.json disagree — mccontrol may override from config.json"
fi

echo "=== recent mccontrol log (wait_forge) ==="
journalctl -u mccontrol -n 40 --no-pager 2>/dev/null | grep -E 'Waiting for Forge|still waiting|wait_forge|DEGRADED|ip_to|Forge accepting' || echo "(no matching journal lines)"
echo

echo "=== TCP probe ==="
if (echo >/dev/tcp/"$IP"/"$PORT") 2>/dev/null; then
  echo "OK: $IP:$PORT accepts TCP from this host"
  echo
  echo "If the door is still STARTING, reset sticky state:"
  echo "  sudo bash /opt/mccontrol/scripts/reset_door_state.sh"
  echo "  # or: sudo bash ~/MinecraftServerDeploy/vm2/scripts/reset_door_state.sh"
  exit 0
fi

echo "FAIL: cannot connect to $IP:$PORT from this host"
echo
echo "Next checks on VM1 (Forge):"
echo "  ss -lntp | grep $PORT"
echo "  # expect *:25565 or 0.0.0.0:25565"
echo "  ip -4 addr show | grep 'inet '"
echo "  # private IP must match $IP"
echo "  sudo systemctl is-active firewalld; sudo iptables -L INPUT -n -v | head -40"
echo
echo "Likely fixes:"
echo "  1) On VM1: allow host TCP $PORT (see vm1/scripts/ensure_forge_port.sh)"
echo "  2) OCI Security List: add ingress TCP $PORT from VCN CIDR (door private poll),"
echo "     in addition to friend public /32s"
echo "  3) Fix stale VM1_PRIVATE_IP in $ENV_FILE and $CFG_FILE, then restart mccontrol"
exit 1
