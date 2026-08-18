#!/usr/bin/env bash
# Graceful Minecraft stop via RCON then systemctl, then world backup to Object Storage.
set -euo pipefail

MSG="${1:-Server shutting down. Saving world…}"
CONFIG="${MC_MANAGER_CONFIG:-/etc/mc-manager/config.json}"
PYTHON="${MC_MANAGER_PYTHON:-/opt/mc-manager/venv/bin/python}"
OPT_LIB="/opt/mc-manager/lib"

export PYTHONPATH="${OPT_LIB}:${PYTHONPATH:-}"
export MC_STOP_MESSAGE="${MSG}"

"${PYTHON}" - <<'PY'
import json, os, sys, time
sys.path.insert(0, "/opt/mc-manager/lib")
from rcon_client import RconClient

cfg_path = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")
msg = os.environ.get("MC_STOP_MESSAGE", "Server shutting down.")
with open(cfg_path, encoding="utf-8") as f:
    cfg = json.load(f)
host = cfg.get("rcon_host", "127.0.0.1")
port = int(cfg.get("rcon_port", 25575))
password = cfg.get("rcon_password", "")
state_path = cfg.get("state_path", "/var/lib/mc-manager/idle_state.json")
ledger_path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
# Clear idle countdown so the next start does not inherit a finished timer.
os.makedirs(os.path.dirname(state_path), exist_ok=True)
st = {}
if os.path.exists(state_path):
    try:
        with open(state_path, encoding="utf-8") as f:
            st = json.load(f) or {}
    except Exception:
        st = {}
st["idle_since"] = None
st.pop("idle_warned", None)
with open(state_path, "w", encoding="utf-8") as f:
    json.dump(st, f, indent=2)
    f.write("\n")
if os.path.exists(ledger_path):
    try:
        with open(ledger_path, encoding="utf-8") as f:
            data = json.load(f) or {}
        data["idle_since"] = None
        with open(ledger_path, "w", encoding="utf-8") as f:
            json.dump(data, f, indent=2)
            f.write("\n")
    except Exception as exc:
        print(f"ledger idle clear warning: {exc}", file=sys.stderr)
try:
    with RconClient(host, port, password) as r:
        r.command("say " + msg)
        r.command("save-all flush")
except Exception as exc:
    print(f"RCON warning: {exc}", file=sys.stderr)
time.sleep(15)
PY

systemctl stop minecraft

# Best-effort world zip → Object Storage (Minecraft already stopped → cold mode).
# SoftStop / Force Stop should still proceed if backup fails.
"${PYTHON}" - <<'PY' || true
import sys
sys.path.insert(0, "/opt/mc-manager/lib")
import world_backup
try:
    print(world_backup.backup_from_config(mode="cold"))
except Exception as exc:
    print(f"World backup warning: {exc}", file=sys.stderr)
    sys.exit(1)
PY
