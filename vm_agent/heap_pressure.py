"""Detect Minecraft memory pressure and persist meta/heap-pressure.json.

Called from idle_watch once per timer tick. Watches latest.log / gc logs for
OutOfMemoryError, GC overhead, or repeated Full GC. Does not treat G1 occupancy
percent as pressure. Never auto-grows server memory.
"""

from __future__ import annotations

import json
import os
import re
from datetime import datetime, timezone
from typing import Any, Callable

import heap_clamp as heap_clamp_mod
import shape_detect as shape_mod

OBJ_HEAP_PRESSURE = "meta/heap-pressure.json"
STATUS_PRESSURE = "pressure"
REASON_OOM = "oom"
REASON_GC_OVERHEAD = "gc_overhead"
REASON_REPEATED_FULL_GC = "repeated_full_gc"
PUT_MIN_INTERVAL_SEC = 30 * 60
FULL_GC_MIN_HITS = 3
LOG_TAIL_BYTES = 512 * 1024
DEFAULT_SERVER_DIR = "/opt/mcmgr/server"

_OOM_RE = re.compile(r"OutOfMemoryError", re.IGNORECASE)
_GC_OVERHEAD_RE = re.compile(r"GC overhead limit", re.IGNORECASE)
# Unified logging / classic GC: Pause Full, Full GC, G1 Full. Occupancy % is ignored.
_FULL_GC_RE = re.compile(
    r"(?:Pause Full(?:\s*\(.*?\)|\s+\S+)?|\bFull GC\b|G1 Full)",
    re.IGNORECASE,
)
_OCCUPANCY_RE = re.compile(r"\bOccupancy\b|\bHeap:\s*\d", re.IGNORECASE)


def utc_iso(now: datetime | None = None) -> str:
    stamp = now or datetime.now(timezone.utc)
    if stamp.tzinfo is None:
        stamp = stamp.replace(tzinfo=timezone.utc)
    return stamp.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_iso(value: Any) -> datetime | None:
    text = str(value or "").strip()
    if not text:
        return None
    try:
        if text.endswith("Z"):
            text = text[:-1] + "+00:00"
        return datetime.fromisoformat(text)
    except ValueError:
        return None


def next_larger(token: str | None, host_memory_gb: float) -> str | None:
    current_gb = heap_clamp_mod.gigabytes(token)
    cap = heap_clamp_mod.max_for_host_memory_gb(host_memory_gb)
    cap_gb = heap_clamp_mod.gigabytes(cap)
    for preset in heap_clamp_mod.ALLOWED:
        gb = heap_clamp_mod.gigabytes(preset)
        if gb > current_gb and gb <= cap_gb:
            return preset
    return None


def classify_log_text(text: str) -> str | None:
    """Return a reason token or None. Occupancy-only lines are not pressure."""
    if not text:
        return None
    if _GC_OVERHEAD_RE.search(text):
        return REASON_GC_OVERHEAD
    if _OOM_RE.search(text):
        return REASON_OOM
    hits = 0
    for line in text.splitlines():
        if _OCCUPANCY_RE.search(line) and not _FULL_GC_RE.search(line):
            continue
        if _FULL_GC_RE.search(line):
            hits += 1
            if hits >= FULL_GC_MIN_HITS:
                return REASON_REPEATED_FULL_GC
    return None


def server_dir(cfg: dict[str, Any] | None) -> str:
    cfg = cfg or {}
    explicit = str(cfg.get("server_dir") or "").strip()
    if explicit:
        return explicit.rstrip("/\\")
    world = str(cfg.get("world_path") or "").strip()
    if world:
        return os.path.dirname(world.rstrip("/\\"))
    return DEFAULT_SERVER_DIR


def _read_tail(path: str, max_bytes: int = LOG_TAIL_BYTES) -> str:
    try:
        size = os.path.getsize(path)
        with open(path, "rb") as f:
            if size > max_bytes:
                f.seek(size - max_bytes)
            raw = f.read()
    except OSError:
        return ""
    return raw.decode("utf-8", errors="replace")


def collect_log_text(cfg: dict[str, Any] | None, *, root: str | None = None) -> str:
    base = root or server_dir(cfg)
    logs = os.path.join(base, "logs")
    chunks: list[str] = []
    chunks.append(_read_tail(os.path.join(logs, "latest.log")))
    chunks.append(_read_tail(os.path.join(logs, "gc.log")))
    chunks.append(_read_tail(os.path.join(logs, "gc.log.0")))
    try:
        names = os.listdir(base)
    except OSError:
        names = []
    for name in names:
        if not name.startswith("hs_err_pid") or not name.endswith(".log"):
            continue
        chunks.append(_read_tail(os.path.join(base, name)))
    return "\n".join(part for part in chunks if part)


def build_document(
    *,
    current_heap: str,
    host_memory_gb: float,
    reason: str,
    now: datetime | None = None,
    detected_at: str | None = None,
) -> dict[str, Any]:
    host = heap_clamp_mod.product_host_memory_gb(host_memory_gb)
    current = heap_clamp_mod.normalize(current_heap)
    suggested = next_larger(current, host)
    stamp = utc_iso(now)
    return {
        "version": 1,
        "status": STATUS_PRESSURE,
        "detected_at": detected_at or stamp,
        "updated_at": stamp,
        "reason": reason,
        "current_heap": current,
        "suggested_heap": suggested,
        "host_memory_gb": host,
        "at_host_max": suggested is None,
    }


def should_skip_put(existing: dict[str, Any] | None, new_doc: dict[str, Any], now: datetime) -> bool:
    """Rate-limit identical PUTs so the idle timer does not rewrite every minute."""
    if not isinstance(existing, dict):
        return False
    if str(existing.get("status") or "").strip().lower() != STATUS_PRESSURE:
        return False
    if str(existing.get("current_heap") or "").upper() != str(new_doc.get("current_heap") or "").upper():
        return False
    if str(existing.get("suggested_heap") or "") != str(new_doc.get("suggested_heap") or ""):
        return False
    if str(existing.get("reason") or "") != str(new_doc.get("reason") or ""):
        return False
    prev = parse_iso(existing.get("updated_at") or existing.get("detected_at"))
    if prev is None:
        return False
    if prev.tzinfo is None:
        prev = prev.replace(tzinfo=timezone.utc)
    return (now - prev).total_seconds() < PUT_MIN_INTERVAL_SEC


def _ns_bucket(cfg: dict[str, Any]) -> tuple[str, str] | None:
    namespace = str(cfg.get("object_storage_namespace") or "").strip()
    bucket = str(cfg.get("object_storage_bucket") or "").strip()
    if not namespace or not bucket:
        return None
    return namespace, bucket


def _client():
    import oci

    signer = oci.auth.signers.InstancePrincipalsSecurityTokenSigner()
    return oci.object_storage.ObjectStorageClient(config={}, signer=signer)


def _get_json(client, namespace: str, bucket: str, name: str) -> Any | None:
    try:
        resp = client.get_object(namespace, bucket, name)
        return json.loads(resp.data.content.decode("utf-8"))
    except Exception as exc:  # noqa: BLE001
        status = getattr(exc, "status", None)
        if status == 404:
            return None
        text = str(exc)
        if "404" in text or "NotFound" in text or "not found" in text.lower():
            return None
        raise


def _put_json(client, namespace: str, bucket: str, name: str, doc: dict[str, Any]) -> None:
    body = (json.dumps(doc, indent=2) + "\n").encode("utf-8")
    client.put_object(
        namespace_name=namespace,
        bucket_name=bucket,
        object_name=name,
        put_object_body=body,
        content_type="application/json",
    )


def _delete_object(client, namespace: str, bucket: str, name: str) -> None:
    try:
        client.delete_object(namespace, bucket, name)
    except Exception as exc:  # noqa: BLE001
        status = getattr(exc, "status", None)
        text = str(exc)
        if status == 404 or "404" in text or "NotFound" in text or "not found" in text.lower():
            return
        raise


OsGet = Callable[[], dict[str, Any] | None]
OsPut = Callable[[dict[str, Any]], None]
OsDelete = Callable[[], None]


def tick(
    cfg: dict[str, Any],
    *,
    game_up: bool,
    now: datetime | None = None,
    host_memory_gb: float | None = None,
    current_heap: str | None = None,
    log_text: str | None = None,
    get_flag: OsGet | None = None,
    put_flag: OsPut | None = None,
    delete_flag: OsDelete | None = None,
) -> str:
    """Scan logs and PUT/DELETE the OS flag. Returns a short status line."""
    stamp = now or datetime.now(timezone.utc)
    if stamp.tzinfo is None:
        stamp = stamp.replace(tzinfo=timezone.utc)

    text = collect_log_text(cfg) if log_text is None else log_text
    reason = classify_log_text(text)

    host = (
        float(host_memory_gb)
        if host_memory_gb is not None
        else shape_mod.detect_memory_gb()
    )
    heap = current_heap if current_heap is not None else heap_clamp_mod.read_current_heap()

    ns_bn = _ns_bucket(cfg)

    def _live_get() -> dict[str, Any] | None:
        if ns_bn is None:
            return None
        namespace, bucket = ns_bn
        return _get_json(_client(), namespace, bucket, OBJ_HEAP_PRESSURE)

    def _live_put(doc: dict[str, Any]) -> None:
        if ns_bn is None:
            raise RuntimeError("object storage namespace/bucket missing")
        namespace, bucket = ns_bn
        _put_json(_client(), namespace, bucket, OBJ_HEAP_PRESSURE, doc)

    def _live_delete() -> None:
        if ns_bn is None:
            return
        namespace, bucket = ns_bn
        _delete_object(_client(), namespace, bucket, OBJ_HEAP_PRESSURE)

    getter = get_flag or _live_get
    putter = put_flag or _live_put
    deleter = delete_flag or _live_delete

    if reason is None:
        existing = getter()
        if isinstance(existing, dict) and str(existing.get("status") or "").strip():
            deleter()
            return f"Cleared {OBJ_HEAP_PRESSURE} (no pressure in logs; game_up={game_up})."
        return f"No memory pressure in logs (game_up={game_up})."

    new_doc = build_document(
        current_heap=heap,
        host_memory_gb=host,
        reason=reason,
        now=stamp,
    )
    existing = getter()
    if should_skip_put(existing if isinstance(existing, dict) else None, new_doc, stamp):
        return (
            f"Pressure still {reason}; skipped PUT of {OBJ_HEAP_PRESSURE} "
            f"(rate-limit {PUT_MIN_INTERVAL_SEC}s)."
        )
    if isinstance(existing, dict) and existing.get("detected_at"):
        new_doc["detected_at"] = str(existing.get("detected_at"))
    putter(new_doc)
    suggested = new_doc.get("suggested_heap") or "none"
    return (
        f"Wrote {OBJ_HEAP_PRESSURE} reason={reason} current={new_doc['current_heap']} "
        f"suggested={suggested}."
    )
