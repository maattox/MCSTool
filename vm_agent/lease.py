"""Active-session lease / heartbeat for usage metering (Object Storage Phase 5)."""

from __future__ import annotations

import json
import os
import uuid
from datetime import datetime, timezone
from typing import Any


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


def to_iso(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_iso(value: str | None) -> datetime | None:
    if not value:
        return None
    text = value.strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        dt = datetime.fromisoformat(text)
    except ValueError:
        return None
    if dt.tzinfo is None:
        dt = dt.replace(tzinfo=timezone.utc)
    return dt.astimezone(timezone.utc)


def empty_lease() -> dict[str, Any]:
    return {
        "version": 1,
        "active": False,
        "session_id": None,
        "interval_id": None,
        "started_at": None,
        "last_heartbeat_at": None,
        "ocpus": None,
        "memory_gb": None,
        "updated_at": None,
        "cleared_at": None,
        "clear_reason": None,
    }


def load_lease(path: str) -> dict[str, Any]:
    if not os.path.exists(path):
        return empty_lease()
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
    except (OSError, json.JSONDecodeError):
        return empty_lease()
    if not isinstance(data, dict):
        return empty_lease()
    out = empty_lease()
    out.update({k: data.get(k) for k in out.keys() if k in data})
    out["version"] = int(data.get("version") or 1)
    out["active"] = bool(data.get("active"))
    return out


def save_lease(path: str, lease: dict[str, Any]) -> None:
    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(lease, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def open_lease(
    *,
    interval_id: str,
    started_at: str,
    ocpus: float,
    memory_gb: float,
    session_id: str | None = None,
) -> dict[str, Any]:
    now = to_iso(utc_now())
    return {
        "version": 1,
        "active": True,
        "session_id": session_id or str(uuid.uuid4()),
        "interval_id": interval_id,
        "started_at": started_at,
        "last_heartbeat_at": now,
        "ocpus": float(ocpus),
        "memory_gb": float(memory_gb),
        "updated_at": now,
        "cleared_at": None,
        "clear_reason": None,
    }


def touch_heartbeat(lease: dict[str, Any]) -> dict[str, Any]:
    if not lease.get("active"):
        return lease
    now = to_iso(utc_now())
    lease["last_heartbeat_at"] = now
    lease["updated_at"] = now
    return lease


def clear_lease(lease: dict[str, Any] | None = None, *, reason: str = "stop") -> dict[str, Any]:
    out = empty_lease()
    now = to_iso(utc_now())
    out["updated_at"] = now
    out["cleared_at"] = now
    out["clear_reason"] = reason
    if isinstance(lease, dict):
        # Keep last known shape/session for diagnostics; mark inactive.
        for key in ("session_id", "interval_id", "started_at", "last_heartbeat_at", "ocpus", "memory_gb"):
            if lease.get(key) is not None:
                out[key] = lease.get(key)
        out["active"] = False
    return out


def age_seconds(lease: dict[str, Any], now: datetime | None = None) -> float | None:
    """Seconds since last_heartbeat_at, or None if missing."""
    now = now or utc_now()
    hb = parse_iso(lease.get("last_heartbeat_at") if lease else None)
    if hb is None:
        return None
    return max(0.0, (now - hb).total_seconds())


def is_stale(
    lease: dict[str, Any] | None,
    *,
    grace_seconds: float,
    now: datetime | None = None,
) -> bool:
    """True if inactive/missing or heartbeat older than grace."""
    if not isinstance(lease, dict) or not lease.get("active"):
        return True
    age = age_seconds(lease, now)
    if age is None:
        return True
    return age > float(grace_seconds)


def effective_stop_iso(lease: dict[str, Any] | None) -> str | None:
    """Best stop estimate from lease heartbeat (not wall clock)."""
    if not isinstance(lease, dict):
        return None
    return lease.get("last_heartbeat_at") or lease.get("started_at")
