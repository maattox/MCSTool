"""World folder backup to Object Storage.

MVP callers (SoftStop / graceful stop) usually stop Minecraft first, then zip
(cold path). This module also supports **live** backups while the server is up:

  save-off → save-all flush → zip world → save-on → upload → delete local zip

Local archives are temporary only (``backup_work_dir``); they are removed after
upload (or on failure). Soft-caps total Standard Object Storage at ~9.5 GiB by
deleting the oldest ``backups/*.zip`` objects *before* upload.

World path is config-driven (``world_path``). Today's operator layout is
``/home/ubuntu/minecraft/server/world``; Setup / Vanilla vs modded may change
that later — keep the path in config, not hard-coded call sites.
"""

from __future__ import annotations

import json
import os
import shutil
import subprocess
import sys
import time
import zipfile
from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Any, Iterator, Literal

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

from rcon_client import RconClient, RconError  # noqa: E402

# Soft cap under Always Free ~10 GB Standard (leave headroom).
DEFAULT_SOFT_CAP_BYTES = int(9.5 * 1024**3)
DEFAULT_WORLD_PATH = "/home/ubuntu/minecraft/server/world"
DEFAULT_BACKUP_PREFIX = "backups/"
DEFAULT_WORK_DIR = "/var/tmp/mc-manager-backup"
# Large modded / Distant Horizons worlds can take a while to flush.
DEFAULT_RCON_TIMEOUT_SEC = 180.0
DEFAULT_FLUSH_SETTLE_SEC = 2.0
# Durable Object Storage block flag (Contracts-Object-Storage.md).
OBJ_OVERSIZED_WORLD = "meta/oversized-world-backup.json"

BackupMode = Literal["auto", "live", "cold"]


def _utc_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _utc_stamp() -> str:
    return datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")


def soft_cap_bytes(cfg: dict[str, Any]) -> int:
    raw = cfg.get("object_storage_soft_cap_bytes")
    if raw is not None:
        try:
            return max(0, int(raw))
        except (TypeError, ValueError):
            pass
    gb = cfg.get("object_storage_soft_cap_gb")
    if gb is not None:
        try:
            return max(0, int(float(gb) * 1024**3))
        except (TypeError, ValueError):
            pass
    return DEFAULT_SOFT_CAP_BYTES


def world_path(cfg: dict[str, Any]) -> str:
    return str(cfg.get("world_path") or DEFAULT_WORLD_PATH).strip()


def backup_prefix(cfg: dict[str, Any]) -> str:
    p = str(cfg.get("backup_prefix") or DEFAULT_BACKUP_PREFIX).strip()
    if not p.endswith("/"):
        p += "/"
    return p


def work_dir(cfg: dict[str, Any]) -> str:
    return str(cfg.get("backup_work_dir") or DEFAULT_WORK_DIR).strip()


def backups_enabled(cfg: dict[str, Any]) -> bool:
    if not cfg.get("object_storage_enabled", True):
        return False
    return bool(cfg.get("backup_enabled", True))


def minecraft_unit_active(cfg: dict[str, Any]) -> bool:
    unit = str(cfg.get("minecraft_unit") or "minecraft")
    r = subprocess.run(
        ["systemctl", "is-active", unit],
        capture_output=True,
        text=True,
        check=False,
    )
    return r.returncode == 0 and (r.stdout or "").strip() == "active"


def _rcon_timeout(cfg: dict[str, Any]) -> float:
    try:
        return max(5.0, float(cfg.get("backup_rcon_timeout_seconds", DEFAULT_RCON_TIMEOUT_SEC)))
    except (TypeError, ValueError):
        return DEFAULT_RCON_TIMEOUT_SEC


def _flush_settle(cfg: dict[str, Any]) -> float:
    try:
        return max(0.0, float(cfg.get("backup_flush_settle_seconds", DEFAULT_FLUSH_SETTLE_SEC)))
    except (TypeError, ValueError):
        return DEFAULT_FLUSH_SETTLE_SEC


def _open_rcon(cfg: dict[str, Any]) -> RconClient:
    return RconClient(
        str(cfg.get("rcon_host") or "127.0.0.1"),
        int(cfg.get("rcon_port") or 25575),
        str(cfg.get("rcon_password") or ""),
        timeout=_rcon_timeout(cfg),
    )


def _flush_looks_done(response: str) -> bool:
    lower = (response or "").lower()
    return (
        "saved the game" in lower
        or "saved the world" in lower
        or ("saving the game" in lower and "failed" not in lower)
    )


@contextmanager
def live_world_quiesce(cfg: dict[str, Any]) -> Iterator[str]:
    """Pause autosave, flush to disk, yield; always ``save-on`` afterward.

    Yields a short status string from the flush step. Raises if RCON fails
    before the world is safely quiesced (caller should not zip in that case).
    """
    save_off_ok = False
    rcon = _open_rcon(cfg)
    try:
        try:
            rcon.connect()
        except (OSError, RconError) as exc:
            raise RconError(f"RCON connect/auth failed: {exc}") from exc
        try:
            off_resp = rcon.command("save-off")
        except (OSError, RconError) as exc:
            raise RconError(f"RCON save-off failed: {exc}") from exc
        print(f"RCON save-off: {(off_resp or '').strip()[:200]}")
        save_off_ok = True
        try:
            try:
                flush_resp = rcon.command("save-all flush")
            except (OSError, RconError) as exc:
                raise RconError(f"RCON save-all flush failed: {exc}") from exc
            print(f"RCON save-all flush: {(flush_resp or '').strip()[:400]}")
            settle = _flush_settle(cfg)
            if settle:
                time.sleep(settle)
            if flush_resp and not _flush_looks_done(flush_resp):
                # Extra short wait; flush is typically blocking on modern Java.
                time.sleep(min(5.0, settle + 3.0))
            yield (flush_resp or "").strip() or "save-all flush completed"
        finally:
            if save_off_ok:
                try:
                    on_resp = rcon.command("save-on")
                    print(f"RCON save-on: {(on_resp or '').strip()[:200]}")
                except Exception as exc:  # noqa: BLE001
                    print(
                        f"CRITICAL: RCON save-on failed after live backup "
                        f"({exc}). Autosave may still be OFF until restart.",
                        file=sys.stderr,
                    )
    finally:
        rcon.close()


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


def _get_json_object(client, namespace: str, bucket: str, name: str) -> Any | None:
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


def _put_json_object(
    client, namespace: str, bucket: str, name: str, doc: dict[str, Any]
) -> None:
    body = (json.dumps(doc, indent=2) + "\n").encode("utf-8")
    client.put_object(
        namespace_name=namespace,
        bucket_name=bucket,
        object_name=name,
        put_object_body=body,
        content_type="application/json",
    )


def oversized_flag_blocked(cfg: dict[str, Any]) -> bool:
    """True when meta/oversized-world-backup.json exists with status=blocked."""
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return False
    namespace, bucket = ns_bn
    try:
        doc = _get_json_object(_client(), namespace, bucket, OBJ_OVERSIZED_WORLD)
    except Exception as exc:  # noqa: BLE001
        print(f"oversized-flag GET warning: {exc}", file=sys.stderr)
        return False
    if not isinstance(doc, dict):
        return False
    return str(doc.get("status") or "").strip().lower() == "blocked"


def set_oversized_world_flag(
    cfg: dict[str, Any],
    *,
    archive_size_bytes: int,
    soft_cap: int,
) -> None:
    """Write/replace meta/oversized-world-backup.json (blocked)."""
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return
    namespace, bucket = ns_bn
    now = _utc_iso()
    doc = {
        "version": 1,
        "status": "blocked",
        "detected_at": now,
        "updated_at": now,
        "archive_size_bytes": int(archive_size_bytes),
        "soft_cap_bytes": int(soft_cap),
        "reason": "archive_exceeds_soft_cap",
        "backup_prefix": backup_prefix(cfg),
    }
    _put_json_object(_client(), namespace, bucket, OBJ_OVERSIZED_WORLD, doc)
    print(
        f"Wrote {OBJ_OVERSIZED_WORLD} (blocked; zip={archive_size_bytes} "
        f"cap={soft_cap}). Automatic OS world backups will skip until cleared."
    )


def _list_all_objects(client, namespace: str, bucket: str) -> list[Any]:
    """List every object (name, size, time_created) with pagination."""
    out: list[Any] = []
    start: str | None = None
    while True:
        kwargs: dict[str, Any] = {
            "namespace_name": namespace,
            "bucket_name": bucket,
            "fields": "name,size,timeCreated",
        }
        if start:
            kwargs["start"] = start
        resp = client.list_objects(**kwargs)
        data = resp.data
        objs = list(getattr(data, "objects", None) or [])
        out.extend(objs)
        next_start = getattr(data, "next_start_with", None)
        if not next_start:
            break
        start = next_start
    return out


def _obj_size(obj: Any) -> int:
    try:
        return int(getattr(obj, "size", 0) or 0)
    except (TypeError, ValueError):
        return 0


def _obj_name(obj: Any) -> str:
    return str(getattr(obj, "name", "") or "")


def _obj_created(obj: Any) -> datetime:
    tc = getattr(obj, "time_created", None)
    if isinstance(tc, datetime):
        if tc.tzinfo is None:
            return tc.replace(tzinfo=timezone.utc)
        return tc
    return datetime.min.replace(tzinfo=timezone.utc)


def total_bucket_bytes(client, namespace: str, bucket: str) -> tuple[int, list[Any]]:
    objs = _list_all_objects(client, namespace, bucket)
    return sum(_obj_size(o) for o in objs), objs


def _is_backup_zip(name: str, prefix: str) -> bool:
    if not name.startswith(prefix):
        return False
    if name == prefix or name.endswith("/"):
        return False
    base = name.rsplit("/", 1)[-1]
    if base in (".keep", "index.json"):
        return False
    return base.endswith(".zip")


def evict_oldest_backups(
    client,
    namespace: str,
    bucket: str,
    *,
    prefix: str,
    need_free_bytes: int,
    soft_cap: int,
    objects: list[Any] | None = None,
) -> tuple[int, list[str]]:
    """Delete oldest backup zips until ``current_total + need_free <= soft_cap``.

    Returns ``(bytes_freed, deleted_names)``. Non-backup objects are never deleted.
    """
    if objects is None:
        objects = _list_all_objects(client, namespace, bucket)
    total = sum(_obj_size(o) for o in objects)
    target_max = max(0, soft_cap - need_free_bytes)
    if total <= target_max:
        return 0, []

    backups = [o for o in objects if _is_backup_zip(_obj_name(o), prefix)]
    backups.sort(key=lambda o: (_obj_created(o), _obj_name(o)))

    freed = 0
    deleted: list[str] = []
    for obj in backups:
        if total - freed <= target_max:
            break
        name = _obj_name(obj)
        size = _obj_size(obj)
        client.delete_object(namespace, bucket, name)
        freed += size
        deleted.append(name)
    return freed, deleted


def cleanup_work_dir(wdir: str) -> int:
    """Remove leftover ``.zip`` / ``.partial`` files under the work dir. Returns count."""
    if not os.path.isdir(wdir):
        return 0
    n = 0
    try:
        for name in os.listdir(wdir):
            if not (name.endswith(".zip") or name.endswith(".partial")):
                continue
            path = os.path.join(wdir, name)
            try:
                os.remove(path)
                n += 1
            except OSError:
                pass
    except OSError:
        return n
    return n


def ensure_work_dir_space(wdir: str, src_dir: str) -> None:
    """Refuse to zip if free space looks smaller than ~world size + 512 MiB headroom."""
    try:
        usage = shutil.disk_usage(wdir if os.path.isdir(wdir) else os.path.dirname(wdir) or "/")
    except OSError as exc:
        print(f"Disk free-space check skipped: {exc}", file=sys.stderr)
        return
    world_bytes = 0
    for root, _dirs, files in os.walk(src_dir):
        for name in files:
            try:
                world_bytes += os.path.getsize(os.path.join(root, name))
            except OSError:
                pass
    need = world_bytes + 512 * 1024 * 1024
    if usage.free < need:
        raise RuntimeError(
            f"Not enough free disk for world zip "
            f"(free={usage.free} bytes, need~={need} for world+headroom). "
            f"Clean {wdir} or free space on the root volume."
        )


def zip_world_folder(src_dir: str, dest_zip: str) -> int:
    """Zip ``src_dir`` contents into ``dest_zip``. Returns archive size in bytes."""
    parent = os.path.dirname(dest_zip)
    if parent:
        os.makedirs(parent, exist_ok=True)
    tmp = dest_zip + ".partial"
    if os.path.exists(tmp):
        os.remove(tmp)
    # compresslevel=1: Distant Horizons worlds are large; favor speed.
    with zipfile.ZipFile(
        tmp, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=1
    ) as zf:
        for root, _dirs, files in os.walk(src_dir):
            for name in files:
                full = os.path.join(root, name)
                if name.endswith(".lock") or name.endswith(".tmp"):
                    continue
                arc = os.path.relpath(full, start=src_dir)
                zf.write(full, arcname=arc.replace("\\", "/"))
    os.replace(tmp, dest_zip)
    return int(os.path.getsize(dest_zip))


def upload_file(
    client,
    namespace: str,
    bucket: str,
    object_name: str,
    file_path: str,
) -> None:
    """Multipart-capable upload for multi-GB archives."""
    from oci.object_storage import UploadManager

    upload_manager = UploadManager(
        client, allow_parallel_uploads=True, parallel_process_count=3
    )
    upload_manager.upload_file(
        namespace_name=namespace,
        bucket_name=bucket,
        object_name=object_name,
        file_path=file_path,
        part_size=64 * 1024 * 1024,
    )


def resolve_backup_mode(cfg: dict[str, Any], mode: BackupMode) -> BackupMode:
    """Resolve ``auto`` to ``live`` or ``cold`` based on systemd unit state."""
    if mode in ("live", "cold"):
        return mode
    if minecraft_unit_active(cfg):
        return "live"
    return "cold"


def _upload_local_zip(
    cfg: dict[str, Any],
    *,
    local_zip: str,
    object_name: str,
    zip_bytes: int,
    wdir: str,
) -> str:
    ns_bn = _ns_bucket(cfg)
    assert ns_bn is not None
    namespace, bucket = ns_bn
    prefix = backup_prefix(cfg)
    cap = soft_cap_bytes(cfg)

    if zip_bytes > cap:
        try:
            set_oversized_world_flag(
                cfg, archive_size_bytes=zip_bytes, soft_cap=cap
            )
        except Exception as exc:  # noqa: BLE001
            print(f"oversized-flag PUT warning: {exc}", file=sys.stderr)
        return (
            f"World backup skipped: zip ({zip_bytes} bytes) exceeds soft cap "
            f"({cap} bytes / {cap / (1024**3):.1f} GiB); "
            f"set {OBJ_OVERSIZED_WORLD}."
        )

    client = _client()
    total, objects = total_bucket_bytes(client, namespace, bucket)
    print(
        f"Object Storage usage before eviction: {total} bytes "
        f"({total / (1024**3):.2f} GiB); soft cap {cap / (1024**3):.1f} GiB"
    )

    freed, deleted = evict_oldest_backups(
        client,
        namespace,
        bucket,
        prefix=prefix,
        need_free_bytes=zip_bytes,
        soft_cap=cap,
        objects=objects,
    )
    if deleted:
        print(
            f"Evicted {len(deleted)} old backup(s), freed {freed} bytes: "
            + ", ".join(deleted)
        )

    total_after = total - freed
    if total_after + zip_bytes > cap:
        raise RuntimeError(
            f"Not enough Object Storage headroom after eviction "
            f"(usage={total_after}, zip={zip_bytes}, cap={cap}). "
            "Non-backup objects may be consuming the soft cap."
        )

    print(f"Uploading {object_name} ...")
    upload_file(client, namespace, bucket, object_name, local_zip)
    new_total = total_after + zip_bytes
    return (
        f"Uploaded {object_name} ({zip_bytes} bytes). "
        f"Estimated bucket usage ~{new_total} bytes "
        f"({new_total / (1024**3):.2f} GiB / {cap / (1024**3):.1f} GiB soft cap)."
    )


def backup_world_to_object_storage(
    cfg: dict[str, Any],
    *,
    mode: BackupMode = "auto",
) -> str:
    """
    Zip configured world folder and upload under ``backups/``.

    ``mode``:
      - ``auto`` (default): live RCON quiesce if ``minecraft`` unit is active,
        otherwise cold zip (server already stopped).
      - ``live``: always use save-off / save-all flush / save-on (fails if RCON down).
      - ``cold``: zip without RCON (for SoftStop after ``systemctl stop``).

    Local zip is deleted after upload. Evicts oldest OS backup zips under soft cap.
    """
    if not backups_enabled(cfg):
        return "World backup skipped (backup_enabled or Object Storage off)."

    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return "World backup skipped (namespace/bucket unset)."

    if oversized_flag_blocked(cfg):
        return (
            f"World backup skipped: {OBJ_OVERSIZED_WORLD} status=blocked "
            "(clear after resolving oversized archive; Manager UX is v1)."
        )

    src = world_path(cfg)
    if not os.path.isdir(src):
        raise FileNotFoundError(
            f"World path not found: {src} "
            "(set world_path in /etc/mc-manager/config.json; path may change "
            "for Vanilla/modded Setup layouts)"
        )

    resolved = resolve_backup_mode(cfg, mode)
    wdir = work_dir(cfg)
    os.makedirs(wdir, exist_ok=True)
    cleaned = cleanup_work_dir(wdir)
    if cleaned:
        print(f"Removed {cleaned} leftover file(s) from {wdir}")
    ensure_work_dir_space(wdir, src)

    stamp = _utc_stamp()
    local_zip = os.path.join(wdir, f"world-{stamp}.zip")
    object_name = f"{backup_prefix(cfg)}world-{stamp}.zip"

    def _do_zip() -> int:
        print(f"Zipping world {src} -> {local_zip} (mode={resolved}) ...")
        return zip_world_folder(src, local_zip)

    zip_bytes = 0
    try:
        if resolved == "live":
            with live_world_quiesce(cfg):
                zip_bytes = _do_zip()
        else:
            zip_bytes = _do_zip()
        print(f"World zip size: {zip_bytes} bytes ({zip_bytes / (1024**3):.2f} GiB)")
        msg = _upload_local_zip(
            cfg,
            local_zip=local_zip,
            object_name=object_name,
            zip_bytes=zip_bytes,
            wdir=wdir,
        )
        return f"{msg} mode={resolved}."
    except RconError:
        raise
    finally:
        try:
            if os.path.exists(local_zip):
                os.remove(local_zip)
        except OSError:
            pass
        cleanup_work_dir(wdir)


def backup_from_config(
    config_path: str | None = None,
    *,
    mode: BackupMode = "auto",
) -> str:
    path = config_path or os.environ.get(
        "MC_MANAGER_CONFIG", "/etc/mc-manager/config.json"
    )
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    return backup_world_to_object_storage(cfg, mode=mode)


def main() -> int:
    mode: BackupMode = "auto"
    if len(sys.argv) > 1:
        arg = sys.argv[1].strip().lower()
        if arg in ("auto", "live", "cold"):
            mode = arg  # type: ignore[assignment]
        elif arg in ("-h", "--help"):
            print(
                "Usage: world_backup.py [auto|live|cold]\n"
                "  auto  - live if minecraft unit active, else cold (default)\n"
                "  live  - save-off / save-all flush / zip / save-on / upload\n"
                "  cold  - zip without RCON (server should be stopped)"
            )
            return 0
        else:
            print(f"Unknown mode {arg!r}; use auto|live|cold", file=sys.stderr)
            return 2
    try:
        print(backup_from_config(mode=mode))
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"World backup failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
