"""Clamp guest JVM heap to the live host before Minecraft starts.

Used by record_boot.py (Before=minecraft.service). Rewrites via on-box
apply-jvm-heap.py and daemon-reload. Does not start or restart Minecraft.
"""
from __future__ import annotations

import os
import re
import subprocess
import sys

ALLOWED = ("4G", "6G", "8G", "10G", "12G")
OS_HEADROOM_GB = 4
MAX_OFFERED_GB = 12
DEFAULT_HEAP = "4G"
DEFAULT_HOST_GB = 24
APPLY_SCRIPT_CANDIDATES = (
    "/opt/mc-manager/bin/apply-jvm-heap.py",
    "/opt/mcmgr/lib/apply-jvm-heap.py",
    "/tmp/mcmgr-heap/apply-jvm-heap.py",
)
JVM_ENV_DEFAULT = "/etc/mcmgr/jvm.env"
UNIT_PATH_DEFAULT = "/etc/systemd/system/minecraft.service"


def normalize(token: str | None) -> str:
    t = (token or "").strip().upper()
    return t if t in ALLOWED else DEFAULT_HEAP


def gigabytes(token: str | None) -> int:
    n = normalize(token)
    if n.endswith("G"):
        try:
            return int(n[:-1])
        except ValueError:
            return 4
    return 4


def product_host_memory_gb(detected: float) -> int:
    """Snap live MemTotal to the product 12 GB or 24 GB size."""
    if detected is None or detected <= 0:
        return DEFAULT_HOST_GB
    n = max(1, int(round(float(detected))))
    return 24 if abs(n - 24) <= abs(n - 12) else 12


def max_for_host_memory_gb(host_memory_gb: float) -> str:
    host = product_host_memory_gb(host_memory_gb)
    cap_gb = min(MAX_OFFERED_GB, max(4, host - OS_HEADROOM_GB))
    best = DEFAULT_HEAP
    for token in ALLOWED:
        if gigabytes(token) <= cap_gb:
            best = token
    return best


def clamp_to_host(token: str | None, host_memory_gb: float) -> str:
    current = normalize(token)
    cap = max_for_host_memory_gb(host_memory_gb)
    return current if gigabytes(current) <= gigabytes(cap) else cap


def capped_token_if_oversized(token: str | None, host_memory_gb: float) -> str | None:
    current = normalize(token)
    capped = clamp_to_host(current, host_memory_gb)
    return None if current == capped else capped


def read_current_heap(jvm_env: str | None = None, unit_path: str | None = None) -> str:
    env_path = jvm_env or os.environ.get("MCMGR_JVM_ENV", JVM_ENV_DEFAULT)
    unit = unit_path or os.environ.get("MCMGR_SYSTEMD_UNIT", UNIT_PATH_DEFAULT)
    if os.path.isfile(env_path):
        try:
            with open(env_path, encoding="utf-8") as f:
                text = f.read()
        except OSError:
            text = ""
        m = re.search(r"JVM_XMX=(\S+)", text)
        if m:
            token = m.group(1).strip().strip("\"'").upper()
            if token in ALLOWED:
                return token
    if os.path.isfile(unit):
        try:
            with open(unit, encoding="utf-8") as f:
                unit_text = f.read()
        except OSError:
            unit_text = ""
        m = re.search(r"-Xmx(\S+)", unit_text)
        if m:
            token = m.group(1).strip().upper()
            if token in ALLOWED:
                return token
    return DEFAULT_HEAP


def resolve_apply_script(explicit: str | None = None) -> str | None:
    if explicit:
        return explicit if os.path.isfile(explicit) else None
    env = os.environ.get("MCMGR_APPLY_JVM_HEAP", "").strip()
    if env and os.path.isfile(env):
        return env
    for path in APPLY_SCRIPT_CANDIDATES:
        if os.path.isfile(path):
            return path
    return None


def ensure_fits_host(
    host_memory_gb: float,
    *,
    apply_script: str | None = None,
    python: str | None = None,
    daemon_reload: bool = True,
) -> tuple[int, str]:
    """Rewrite guest heap when it exceeds the host cap.

    Returns (0, message) when the guest already fits or the rewrite succeeded.
    Non-zero means Minecraft must not start (oversized allocation still on disk).
    """
    current = read_current_heap()
    cap = capped_token_if_oversized(current, host_memory_gb)
    host = product_host_memory_gb(host_memory_gb)
    if cap is None:
        return 0, f"Guest server memory {current} fits {host} GB host."

    script = resolve_apply_script(apply_script)
    if script is None:
        return (
            1,
            f"Guest server memory {current} exceeds {host} GB host (need {cap}); "
            "apply-jvm-heap.py was not found.",
        )

    py = python or os.environ.get("MCMGR_APPLY_PYTHON") or sys.executable
    proc = subprocess.run(
        [py, script, cap],
        check=False,
        capture_output=True,
        text=True,
    )
    out = ((proc.stdout or "") + (proc.stderr or "")).strip()
    if proc.returncode != 0:
        return (
            proc.returncode or 1,
            f"apply-jvm-heap.py {cap} failed (rc={proc.returncode}): {out}",
        )
    if "OK heap=" not in (proc.stdout or ""):
        return 1, f"apply-jvm-heap.py {cap} did not confirm OK: {out}"

    if daemon_reload:
        reload = subprocess.run(
            ["systemctl", "daemon-reload"],
            check=False,
            capture_output=True,
            text=True,
        )
        if reload.returncode != 0:
            err = (reload.stderr or reload.stdout or "").strip()
            return (
                reload.returncode or 1,
                f"systemctl daemon-reload failed after heap clamp to {cap}: {err}",
            )

    return 0, f"Clamped guest server memory {current} → {cap} for {host} GB host."
