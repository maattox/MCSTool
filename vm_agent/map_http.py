#!/usr/bin/env python3
"""Serve the player-map tar on VM1's primary private IP only (subnet pull)."""

from __future__ import annotations

import json
import os
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.request import Request, urlopen

CONFIG_PATH = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")
DEFAULT_MAP_ROOT = "/var/lib/mcmgr-map"
TILE_PORT = int(os.environ.get("VM1_TILE_PORT", "8765"))
METADATA_URL = "http://169.254.169.254/opc/v2/vnics/"


def load_config() -> dict:
    if not os.path.isfile(CONFIG_PATH):
        return {}
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def http_root(cfg: dict) -> Path:
    root = Path(cfg.get("player_map_root") or DEFAULT_MAP_ROOT)
    path = root / "http"
    path.mkdir(parents=True, exist_ok=True)
    return path


def from_metadata() -> str:
    req = Request(
        METADATA_URL,
        headers={"Authorization": "Bearer Oracle", "Accept": "application/json"},
    )
    with urlopen(req, timeout=3) as resp:  # noqa: S310 — link-local OCI metadata
        vnics = json.load(resp)
    if not isinstance(vnics, list):
        return ""
    for vnic in vnics:
        if not isinstance(vnic, dict):
            continue
        if vnic.get("nicIndex") == 0 or vnic.get("isPrimary") is True:
            ip = str(vnic.get("privateIp") or "").strip()
            if ip:
                return ip
    if vnics and isinstance(vnics[0], dict):
        return str(vnics[0].get("privateIp") or "").strip()
    return ""


def bind_ip(cfg: dict) -> str:
    env_ip = str(os.environ.get("VM1_PRIVATE_IP") or "").strip()
    if env_ip:
        return env_ip
    cfg_ip = str(cfg.get("vm1_private_ip") or "").strip()
    if cfg_ip:
        return cfg_ip
    meta = from_metadata()
    if meta:
        return meta
    raise SystemExit("map-http: no primary private IP (config vm1_private_ip / metadata)")


def main() -> int:
    cfg = load_config()
    root = http_root(cfg)
    host = bind_ip(cfg)
    port = int(cfg.get("player_map_tile_port") or TILE_PORT)

    class Handler(SimpleHTTPRequestHandler):
        def __init__(self, *args, **kwargs):
            super().__init__(*args, directory=str(root), **kwargs)

        def log_message(self, fmt: str, *args: object) -> None:
            sys.stderr.write("%s - %s\n" % (self.address_string(), fmt % args))

    httpd = ThreadingHTTPServer((host, port), Handler)
    print(f"map-http listening on {host}:{port} (root {root})", flush=True)
    httpd.serve_forever()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
