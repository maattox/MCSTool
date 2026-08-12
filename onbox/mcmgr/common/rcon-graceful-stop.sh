#!/usr/bin/env bash
# systemd ExecStop helper — RCON save-all flush + stop (no systemctl recursion, no OS backup).
# Installed to /opt/mcmgr/bin/rcon-graceful-stop.sh
# shellcheck shell=bash
set -euo pipefail

SECRET_FILE="${MCMGR_RCON_SECRET:-/etc/mcmgr/rcon.secret}"
HOST="${MCMGR_RCON_HOST:-127.0.0.1}"
PORT="${MCMGR_RCON_PORT:-25575}"

if [[ ! -f "${SECRET_FILE}" ]]; then
  echo "rcon-graceful-stop: missing ${SECRET_FILE}" >&2
  exit 0
fi

PASSWORD="$(tr -d '\r\n' <"${SECRET_FILE}")"
export MCMGR_RCON_HOST="${HOST}"
export MCMGR_RCON_PORT="${PORT}"
export MCMGR_RCON_PASSWORD="${PASSWORD}"

PY=""
if command -v python3 >/dev/null 2>&1; then
  PY=python3
elif command -v python >/dev/null 2>&1; then
  PY=python
else
  echo "rcon-graceful-stop: no python; skipping RCON" >&2
  exit 0
fi

"${PY}" - <<'PY' || true
import os, socket, struct, select, sys

host = os.environ.get("MCMGR_RCON_HOST", "127.0.0.1")
port = int(os.environ.get("MCMGR_RCON_PORT", "25575"))
password = os.environ.get("MCMGR_RCON_PASSWORD", "")

def send_packet(sock, req_id, req_type, body: str):
    payload = struct.pack("<ii", req_id, req_type) + body.encode("utf-8") + b"\x00\x00"
    sock.sendall(struct.pack("<i", len(payload)) + payload)

def read_packet(sock, timeout=5.0):
    sock.settimeout(timeout)
    raw_len = sock.recv(4)
    if len(raw_len) < 4:
        return None
    (length,) = struct.unpack("<i", raw_len)
    data = b""
    while len(data) < length:
        chunk = sock.recv(length - len(data))
        if not chunk:
            break
        data += chunk
    if len(data) < 8:
        return None
    req_id, req_type = struct.unpack("<ii", data[:8])
    body = data[8:-2].decode("utf-8", errors="replace") if length >= 10 else ""
    return req_id, req_type, body

try:
    with socket.create_connection((host, port), timeout=5) as sock:
        send_packet(sock, 1, 3, password)  # AUTH
        auth = read_packet(sock)
        if auth is None or auth[0] == -1:
            print("rcon-graceful-stop: auth failed", file=sys.stderr)
            raise SystemExit(0)
        for cmd in ("save-all flush", "save-off", "stop"):
            send_packet(sock, 2, 2, cmd)
            read_packet(sock)
except Exception as exc:
    print(f"rcon-graceful-stop: {exc}", file=sys.stderr)
PY
