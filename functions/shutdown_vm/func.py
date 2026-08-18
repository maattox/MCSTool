"""$1 budget emergency Function (product v1 source).

On a real threshold alert (not RESET): SoftStop VM1, then PUT
``meta/spend-brake-triggered.json``. Do not SoftStop the Always Free door Micro.
Do not DELETE the lock (Manager is the only clearer).

Tracked placeholders only — live OCIDs stay in Function config / the private file.
Do not ``fn push`` unless the operator authorizes it (and preferably after door
honor of this flag — V1 Step 2.3).
"""

from __future__ import annotations

import datetime
import io
import json
import logging
import os

# Live OCIDs belong only in data/Infrastructure-Deployment-Private.md and
# Function application config. Product v1 SoftStops VM1 only.
INSTANCE_OCIDS = [
    "<VM1_INSTANCE_OCID>",
]

LOCK_OBJECT_DEFAULT = "meta/spend-brake-triggered.json"
LOCK_SOURCE = "budget_function"
LOCK_REASON = "compartment_budget_threshold"
LOCK_VERSION = 1


def parse_triggered_alert_type(raw_bytes):
    """Return Events ``triggeredAlertType`` or None if missing/unparseable."""
    if not raw_bytes:
        return None
    body = json.loads(raw_bytes)
    if not isinstance(body, dict):
        return None
    return (
        body.get("data", {})
        .get("stateChange", {})
        .get("current", {})
        .get("triggeredAlertType")
    )


def is_reset_alert(alert_type):
    return alert_type == "RESET"


def format_utc(now_utc=None):
    stamp = now_utc or datetime.datetime.now(datetime.timezone.utc)
    if stamp.tzinfo is None:
        stamp = stamp.replace(tzinfo=datetime.timezone.utc)
    return stamp.astimezone(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def build_lock_document(alert_type, now_utc=None):
    """JSON shape frozen in product Contracts-Object-Storage.md (v1)."""
    stamp = format_utc(now_utc)
    doc = {
        "version": LOCK_VERSION,
        "triggered_at": stamp,
        "updated_at": stamp,
        "source": LOCK_SOURCE,
        "reason": LOCK_REASON,
    }
    if alert_type:
        doc["alert_type"] = str(alert_type)
    return doc


def _strip_placeholders(values):
    out = []
    for item in values:
        text = str(item).strip()
        if text and not text.startswith("<"):
            out.append(text)
    return out


def resolve_instance_ocids(env=None, baked=None):
    env = os.environ if env is None else env
    raw = (env.get("INSTANCE_OCIDS") or "").strip()
    from_env = _strip_placeholders(raw.split(","))
    if from_env:
        return from_env
    baked = INSTANCE_OCIDS if baked is None else baked
    return _strip_placeholders(baked)


def resolve_os_config(env=None):
    env = os.environ if env is None else env
    namespace = (env.get("OS_NAMESPACE") or "").strip()
    bucket = (env.get("OS_BUCKET") or "").strip()
    object_name = (env.get("OS_LOCK_OBJECT") or LOCK_OBJECT_DEFAULT).strip()
    if namespace.startswith("<"):
        namespace = ""
    if bucket.startswith("<"):
        bucket = ""
    if not object_name or object_name.startswith("<"):
        object_name = LOCK_OBJECT_DEFAULT
    return namespace, bucket, object_name


def _softstop(compute_client, instance_id, logger):
    """Issue SOFTSTOP; treat already-stopped as success."""
    import oci

    try:
        resp = compute_client.instance_action(
            instance_id=instance_id,
            action="SOFTSTOP",
        )
        logger.info(
            "SOFTSTOP issued for %s (HTTP %s)",
            instance_id,
            resp.status,
        )
        return {"instanceId": instance_id, "status": "SUCCESS", "httpStatus": resp.status}
    except oci.exceptions.ServiceError as ex:
        msg = (ex.message or "").lower()
        if ex.status in (409, 400) and (
            "not running" in msg
            or "invalid state" in msg
            or "stopped" in msg
            or "stopping" in msg
        ):
            logger.info(
                "SOFTSTOP skipped for %s (already stopped/stopping): %s",
                instance_id,
                ex.message,
            )
            return {
                "instanceId": instance_id,
                "status": "SKIPPED",
                "reason": ex.message,
            }
        logger.error("SOFTSTOP failed for %s: %s", instance_id, ex)
        return {
            "instanceId": instance_id,
            "status": "ERROR",
            "message": str(ex),
        }
    except Exception as ex:
        logger.error("SOFTSTOP failed for %s: %s", instance_id, ex)
        return {
            "instanceId": instance_id,
            "status": "ERROR",
            "message": str(ex),
        }


def _put_lock(os_client, namespace, bucket, object_name, alert_type, logger, now_utc=None):
    """Idempotent PUT of the spend-brake lock. Never DELETE (Manager-only)."""
    import oci

    body = json.dumps(build_lock_document(alert_type, now_utc=now_utc), indent=2) + "\n"
    try:
        resp = os_client.put_object(
            namespace_name=namespace,
            bucket_name=bucket,
            object_name=object_name,
            put_object_body=body.encode("utf-8"),
            content_type="application/json",
            retry_strategy=oci.retry.DEFAULT_RETRY_STRATEGY,
        )
        logger.info(
            "PUT %s (HTTP %s)",
            object_name,
            getattr(resp, "status", None),
        )
        return {
            "object": object_name,
            "status": "SUCCESS",
            "httpStatus": getattr(resp, "status", None),
        }
    except Exception as ex:
        logger.error("PUT %s failed: %s", object_name, ex)
        return {"object": object_name, "status": "ERROR", "message": str(ex)}


def handler(ctx, data: io.BytesIO = None):
    from fdk import response
    import oci

    logger = logging.getLogger()
    logger.info("Budget alert function invoked.")

    alert_type = None
    try:
        raw = data.getvalue() if data is not None else None
        alert_type = parse_triggered_alert_type(raw)
        logger.info("Parsed triggeredAlertType: %s", alert_type)
    except Exception as parse_err:
        logger.warning(
            "Could not parse event JSON (proceeding with caution): %s",
            parse_err,
        )

    if is_reset_alert(alert_type):
        logger.info(
            "Received monthly budget RESET event. Skipping SoftStop and lock PUT."
        )
        return response.Response(
            ctx,
            response_data=json.dumps(
                {"status": "SKIPPED", "reason": "Monthly budget reset event"}
            ),
            headers={"Content-Type": "application/json"},
        )

    instance_ids = resolve_instance_ocids()
    namespace, bucket, lock_object = resolve_os_config()

    try:
        signer = oci.auth.signers.get_resource_principals_signer()
        compute_client = oci.core.ComputeClient(config={}, signer=signer)
        compute_client.base_client.retry_strategy = oci.retry.DEFAULT_RETRY_STRATEGY

        if not instance_ids:
            logger.warning("No INSTANCE_OCIDS resolved; skipping SoftStop.")
            results = []
        else:
            results = [
                _softstop(compute_client, ocid, logger) for ocid in instance_ids
            ]

        lock_result = None
        if not namespace or not bucket:
            logger.error(
                "OS_NAMESPACE/OS_BUCKET not set; cannot write spend-brake lock."
            )
            lock_result = {
                "object": lock_object,
                "status": "ERROR",
                "message": "OS_NAMESPACE or OS_BUCKET missing",
            }
        else:
            os_client = oci.object_storage.ObjectStorageClient(
                config={}, signer=signer
            )
            os_client.base_client.retry_strategy = oci.retry.DEFAULT_RETRY_STRATEGY
            lock_result = _put_lock(
                os_client, namespace, bucket, lock_object, alert_type, logger
            )

        any_error = any(r.get("status") == "ERROR" for r in results) or (
            lock_result or {}
        ).get("status") == "ERROR"
        overall = "ERROR" if any_error else "SUCCESS"

        logger.info(
            "Budget SoftStop+lock complete: overall=%s results=%s lock=%s",
            overall,
            results,
            lock_result,
        )

        return response.Response(
            ctx,
            response_data=json.dumps(
                {
                    "status": overall,
                    "action": "SOFTSTOP",
                    "alertType": alert_type,
                    "results": results,
                    "lock": lock_result,
                }
            ),
            headers={"Content-Type": "application/json"},
        )
    except Exception as ex:
        logger.error("Failed to issue SoftStop/lock: %s", ex)
        return response.Response(
            ctx,
            response_data=json.dumps({"status": "ERROR", "message": str(ex)}),
            headers={"Content-Type": "application/json"},
        )
