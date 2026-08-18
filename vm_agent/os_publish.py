"""Publish VM1 usage ledger + lease to Object Storage via instance principal."""

from __future__ import annotations

import json
import os
import sys
from datetime import datetime, timezone
from typing import Any

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import lease as lease_mod  # noqa: E402
import ledger as ledger_mod  # noqa: E402

OBJ_FLAGS = "meta/flags.json"
OBJ_LEDGER = "ledger/usage.json"
OBJ_LEASE = "ledger/lease.json"
OBJ_BUDGET = "budget/config.json"
CONSUMERS = ("manager", "door", "vm1")
CATEGORIES = ("ledger", "budget", "meta", "ip", "messages")


def _utc_now() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _empty_flags() -> dict[str, Any]:
    return {
        "version": 1,
        "updated_at": _utc_now(),
        "categories": {
            cat: {c: False for c in CONSUMERS} for cat in CATEGORIES
        },
        "help": (
            "When a writer updates a category, set that category's consumer "
            "flags to true so each side knows to pull. Consumers clear only "
            "their own flag after a successful pull."
        ),
    }


def _normalize_flags(data: Any) -> dict[str, Any]:
    base = _empty_flags()
    if not isinstance(data, dict):
        return base
    out = dict(base)
    out["version"] = int(data.get("version") or 1)
    if data.get("updated_at"):
        out["updated_at"] = data["updated_at"]
    cats_in = data.get("categories") if isinstance(data.get("categories"), dict) else {}
    cats_out: dict[str, dict[str, bool]] = {}
    for cat in CATEGORIES:
        src = cats_in.get(cat) if isinstance(cats_in.get(cat), dict) else {}
        cats_out[cat] = {c: bool(src[c]) if c in src else False for c in CONSUMERS}
    out["categories"] = cats_out
    if data.get("help"):
        out["help"] = data["help"]
    return out


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


def _get_json_with_etag(
    client, namespace: str, bucket: str, name: str
) -> tuple[Any | None, str | None]:
    try:
        resp = client.get_object(namespace, bucket, name)
        etag = getattr(resp, "headers", None) and resp.headers.get("etag")
        if etag is None:
            etag = getattr(resp, "etag", None)
        return json.loads(resp.data.content.decode("utf-8")), etag
    except Exception as exc:  # noqa: BLE001
        status = getattr(exc, "status", None)
        if status == 404:
            return None, None
        text = str(exc)
        if "404" in text or "NotFound" in text or "not found" in text.lower():
            return None, None
        raise


def _put_json(
    client,
    namespace: str,
    bucket: str,
    name: str,
    data: Any,
    *,
    if_match: str | None = None,
) -> None:
    body = (json.dumps(data, indent=2) + "\n").encode("utf-8")
    kwargs: dict[str, Any] = {
        "namespace_name": namespace,
        "bucket_name": bucket,
        "object_name": name,
        "put_object_body": body,
        "content_type": "application/json",
    }
    if if_match:
        kwargs["if_match"] = if_match
    client.put_object(**kwargs)


def _ns_bucket(cfg: dict[str, Any]) -> tuple[str, str] | None:
    if not cfg.get("object_storage_enabled", True):
        return None
    namespace = str(cfg.get("object_storage_namespace") or "").strip()
    bucket = str(cfg.get("object_storage_bucket") or "").strip()
    if not namespace or not bucket:
        return None
    return namespace, bucket


def lease_path(cfg: dict[str, Any]) -> str:
    return str(cfg.get("lease_path", "/var/lib/mc-manager/lease.json"))


def pull_ledger_if_dirty(cfg: dict[str, Any]) -> tuple[dict[str, Any], str]:
    """
    If ledger.vm1 dirty (or local ledger missing), download OS ledger, save locally,
    clear only the vm1 flag. Returns (ledger, status_message).
    """
    return _pull_ledger(cfg, force=False)


def pull_ledger_for_boot(cfg: dict[str, Any]) -> tuple[dict[str, Any], str]:
    """
    Boot path: always fetch Object Storage ledger when enabled so door heal /
    Manager edits are seen even if ledger.vm1 was cleared early. Still clears
    ledger.vm1 when it was dirty.
    """
    return _pull_ledger(cfg, force=True)


def _pull_ledger(cfg: dict[str, Any], *, force: bool) -> tuple[dict[str, Any], str]:
    path = str(cfg.get("ledger_path", "/var/lib/mc-manager/usage.json"))
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        return data, "Object Storage disabled or unset; using local ledger."

    namespace, bucket = ns_bn
    client = _client()
    flags = _normalize_flags(_get_json(client, namespace, bucket, OBJ_FLAGS))
    dirty = bool(flags["categories"]["ledger"].get("vm1"))
    missing_local = not os.path.exists(path)
    if not force and not dirty and not missing_local:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            raise RuntimeError(f"Ledger at {path} is not a JSON object")
        return data, "ledger.vm1 clear; using local ledger."

    remote = _get_json(client, namespace, bucket, OBJ_LEDGER)
    if not isinstance(remote, dict):
        raise RuntimeError("Object Storage ledger/usage.json missing or invalid")

    os.makedirs(os.path.dirname(path) or ".", exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(remote, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)

    if dirty:
        flags["categories"]["ledger"]["vm1"] = False
        flags["updated_at"] = _utc_now()
        _put_json(client, namespace, bucket, OBJ_FLAGS, flags)
    n = len(remote.get("intervals") or [])
    if force and not dirty and not missing_local:
        why = "boot force-pull"
    elif dirty:
        why = "flag dirty"
    else:
        why = "local missing"
    cleared = "; cleared ledger.vm1" if dirty else ""
    return remote, f"Pulled {OBJ_LEDGER} ({n} intervals) because {why}{cleared}."


def pull_lease(cfg: dict[str, Any]) -> tuple[dict[str, Any], str]:
    """Download ledger/lease.json when present; save locally. Missing → empty lease."""
    path = lease_path(cfg)
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        local = lease_mod.load_lease(path)
        return local, "Object Storage disabled; using local lease."
    namespace, bucket = ns_bn
    client = _client()
    remote = _get_json(client, namespace, bucket, OBJ_LEASE)
    if not isinstance(remote, dict):
        local = lease_mod.empty_lease()
        lease_mod.save_lease(path, local)
        return local, f"{OBJ_LEASE} missing; using empty lease."
    lease_mod.save_lease(path, remote)
    return remote, (
        f"Pulled {OBJ_LEASE} active={bool(remote.get('active'))} "
        f"heartbeat={remote.get('last_heartbeat_at')}"
    )


def publish_lease(cfg: dict[str, Any], lease: dict[str, Any] | None = None) -> str:
    """Upload ledger/lease.json only (no dirty flags — heartbeat traffic)."""
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return "Object Storage lease publish skipped (disabled/unset)."
    namespace, bucket = ns_bn
    path = lease_path(cfg)
    if lease is None:
        lease = lease_mod.load_lease(path)
    client = _client()
    _put_json(client, namespace, bucket, OBJ_LEASE, lease)
    lease_mod.save_lease(path, lease)
    return (
        f"Published {OBJ_LEASE} active={bool(lease.get('active'))} "
        f"heartbeat={lease.get('last_heartbeat_at')}"
    )


def publish_ledger(cfg: dict[str, Any], ledger: dict[str, Any] | None = None) -> str:
    """
    Upload ledger/usage.json and set dirty flags for manager + door.

    Uses revision + optional If-Match: if remote revision is ahead, merge remote
    into local first, then put. Returns a short status string.
    """
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return "Object Storage publish disabled or namespace/bucket unset."

    namespace, bucket = ns_bn
    path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    if ledger is None:
        with open(path, "r", encoding="utf-8") as f:
            ledger = json.load(f)
        if not isinstance(ledger, dict):
            raise RuntimeError(f"Ledger at {path} is not a JSON object")

    client = _client()
    remote, etag = _get_json_with_etag(client, namespace, bucket, OBJ_LEDGER)
    if isinstance(remote, dict):
        try:
            remote_rev = int(remote.get("revision") or 0)
        except (TypeError, ValueError):
            remote_rev = 0
        try:
            local_rev = int(ledger.get("revision") or 0)
        except (TypeError, ValueError):
            local_rev = 0
        if remote_rev > local_rev:
            ledger = ledger_mod.merge_ledgers_for_boot(remote, ledger)
            etag = None  # body changed; unconditional put after merge

    ledger_mod.bump_revision(ledger)
    try:
        _put_json(client, namespace, bucket, OBJ_LEDGER, ledger, if_match=etag)
    except Exception as exc:  # noqa: BLE001
        # Precondition failed or SDK quirk — retry once unconditional after re-merge.
        text = str(exc).lower()
        if etag and ("412" in text or "precondition" in text or "if-match" in text):
            remote2 = _get_json(client, namespace, bucket, OBJ_LEDGER)
            if isinstance(remote2, dict):
                ledger = ledger_mod.merge_ledgers_for_boot(remote2, ledger)
                ledger_mod.bump_revision(ledger)
            _put_json(client, namespace, bucket, OBJ_LEDGER, ledger)
        else:
            raise

    # Persist bumped revision locally when path is known.
    try:
        ledger_mod.save_ledger(str(path), ledger)
    except OSError:
        pass

    flags = _normalize_flags(_get_json(client, namespace, bucket, OBJ_FLAGS))
    cat = flags["categories"].setdefault("ledger", {c: False for c in CONSUMERS})
    cat["manager"] = True
    cat["door"] = True
    cat["vm1"] = False
    flags["updated_at"] = _utc_now()
    _put_json(client, namespace, bucket, OBJ_FLAGS, flags)

    n = len(ledger.get("intervals") or [])
    rev = ledger.get("revision")
    return (
        f"Published {OBJ_LEDGER} ({n} intervals, revision={rev}) to {bucket}; "
        "ledger flags manager=true door=true vm1=false."
    )


def publish_ledger_and_lease(
    cfg: dict[str, Any],
    ledger: dict[str, Any] | None = None,
    lease: dict[str, Any] | None = None,
) -> str:
    """Publish usage ledger (with flags) then lease object."""
    msg1 = publish_ledger(cfg, ledger)
    msg2 = publish_lease(cfg, lease)
    return f"{msg1}\n{msg2}"


def _dirty_budget_flags(client, namespace: str, bucket: str) -> None:
    flags = _normalize_flags(_get_json(client, namespace, bucket, OBJ_FLAGS))
    cat = flags["categories"].setdefault("budget", {c: False for c in CONSUMERS})
    cat["manager"] = True
    cat["door"] = True
    cat["vm1"] = False
    flags["updated_at"] = _utc_now()
    _put_json(client, namespace, bucket, OBJ_FLAGS, flags)


def sync_shape_to_budget(
    cfg: dict[str, Any],
    ocpus: float,
    memory_gb: float,
) -> str:
    """Patch ``shape_ocpus`` / ``shape_memory_gb`` on OS ``budget/config.json``.

    Dirties budget flags for manager + door when values change. Leaves other
    budget fields untouched. Safe no-op when Object Storage is disabled or shape
    already matches.
    """
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return "Object Storage shape sync skipped (disabled/unset)."
    namespace, bucket = ns_bn
    client = _client()
    remote = _get_json(client, namespace, bucket, OBJ_BUDGET)
    if not isinstance(remote, dict):
        remote = {
            "version": 1,
            "updated_at": _utc_now(),
            "mode": "always_free",
        }
    try:
        prev_o = float(remote.get("shape_ocpus") or 0)
    except (TypeError, ValueError):
        prev_o = 0.0
    try:
        prev_m = float(remote.get("shape_memory_gb") or 0)
    except (TypeError, ValueError):
        prev_m = 0.0
    if abs(prev_o - float(ocpus)) < 0.01 and abs(prev_m - float(memory_gb)) < 0.25:
        return (
            f"{OBJ_BUDGET} already shape_ocpus={ocpus} "
            f"shape_memory_gb={memory_gb}; no put."
        )

    remote["shape_ocpus"] = float(ocpus)
    remote["shape_memory_gb"] = float(memory_gb)
    remote["updated_at"] = _utc_now()
    remote["shape_source"] = "vm1_proc_detect"
    _put_json(client, namespace, bucket, OBJ_BUDGET, remote)
    _dirty_budget_flags(client, namespace, bucket)
    return (
        f"Updated {OBJ_BUDGET} shape {prev_o}/{prev_m} → {ocpus}/{memory_gb}; "
        "budget flags manager=true door=true vm1=false."
    )


def sync_idle_agent_enabled_to_budget(
    cfg: dict[str, Any], *, enabled: bool = True
) -> str:
    """Force ``idle_agent_enabled`` on OS ``budget/config.json`` (boot safety).

    Product rule: disabling idle is testing-only; VM1 boot rewrites shared config
    back to enabled so a forgotten disable cannot leave free-tier brakes off.
    """
    ns_bn = _ns_bucket(cfg)
    if ns_bn is None:
        return "Object Storage idle_agent_enabled sync skipped (disabled/unset)."
    namespace, bucket = ns_bn
    client = _client()
    remote = _get_json(client, namespace, bucket, OBJ_BUDGET)
    if not isinstance(remote, dict):
        remote = {
            "version": 1,
            "updated_at": _utc_now(),
            "mode": "always_free",
        }
    prev = remote.get("idle_agent_enabled")
    if bool(prev) is bool(enabled) and prev is not None:
        return f"{OBJ_BUDGET} already idle_agent_enabled={bool(enabled)}; no put."

    remote["idle_agent_enabled"] = bool(enabled)
    remote["updated_at"] = _utc_now()
    remote["idle_agent_enabled_source"] = "vm1_boot_force_enable"
    _put_json(client, namespace, bucket, OBJ_BUDGET, remote)
    _dirty_budget_flags(client, namespace, bucket)
    return (
        f"Updated {OBJ_BUDGET} idle_agent_enabled {prev!r} → {bool(enabled)}; "
        "budget flags manager=true door=true vm1=false."
    )


def publish_from_config(config_path: str | None = None) -> str:
    path = config_path or os.environ.get(
        "MC_MANAGER_CONFIG", "/etc/mc-manager/config.json"
    )
    with open(path, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    return publish_ledger(cfg)


def main() -> int:
    try:
        print(publish_from_config())
        return 0
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage publish failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
