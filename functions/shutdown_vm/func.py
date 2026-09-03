"""$1 budget emergency Function (product v1 source).

SoftStop VM1 and PUT ``meta/spend-brake-triggered.json`` only when the
compartment has actually reached the $1 threshold. Monthly RESET (and
FORECAST) Events use the same ``createtriggeredalert`` type and must be
ignored. Do not SoftStop the Always Free door Micro. Do not DELETE the
lock (Manager is the only clearer).

Tracked placeholders only — live OCIDs stay in Function config / the private file.
Do not ``fn push`` unless the operator authorizes it.
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

SKIP_ALERT_TYPES = frozenset({"RESET", "FORECAST"})
ACT_ALERT_TYPES = frozenset({"ACTUAL"})
ALERT_TYPE_KEYS = ("triggeredAlertType", "triggered_alert_type")
BUDGET_ID_KEYS = ("budgetId", "budget_id")


def _iter_dicts(node):
    if isinstance(node, dict):
        yield node
        for value in node.values():
            yield from _iter_dicts(value)
    elif isinstance(node, list):
        for item in node:
            yield from _iter_dicts(item)


def parse_event(raw_bytes):
    """Pull alert type and budget OCID from any Events / FDK envelope."""
    if not raw_bytes:
        return {"alert_type": None, "budget_id": None}
    body = json.loads(raw_bytes)
    if not isinstance(body, dict):
        return {"alert_type": None, "budget_id": None}

    alert_type = None
    budget_id = None
    for item in _iter_dicts(body):
        if alert_type is None:
            for key in ALERT_TYPE_KEYS:
                value = item.get(key)
                if value:
                    alert_type = str(value).strip().upper()
                    break
        if budget_id is None:
            for key in BUDGET_ID_KEYS:
                value = item.get(key)
                if isinstance(value, str) and "ocid1.budget." in value:
                    budget_id = value.strip()
                    break
        if alert_type and budget_id:
            break
    return {"alert_type": alert_type, "budget_id": budget_id}


def parse_triggered_alert_type(raw_bytes):
    """Return Events ``triggeredAlertType`` or None if missing/unparseable."""
    return parse_event(raw_bytes).get("alert_type")


def is_reset_alert(alert_type):
    return (alert_type or "").strip().upper() == "RESET"


def spend_reached_threshold(actual_spend, amount, threshold=None):
    """True / False when both values parse; None when spend is unknown."""
    limit = threshold if threshold is not None else amount
    if actual_spend is None or limit is None:
        return None
    try:
        return float(actual_spend) + 1e-9 >= float(limit)
    except (TypeError, ValueError):
        return None


def decide_spend_brake_action(alert_type, spend_reached):
    """Allowlist a confirmed $1 ACTUAL. Skip RESET / FORECAST / unconfirmed.

    ``spend_reached`` is True, False, or None (budget GET missing/failed).
    Official CreateTriggeredAlert JSON has no alert type, so the spend
    gate is the primary signal.
    """
    kind = (alert_type or "").strip().upper() or None
    if kind in SKIP_ALERT_TYPES:
        return "SKIP", "alert_type=%s" % kind
    if spend_reached is True:
        return "ACT", "actual_spend_reached_threshold"
    if spend_reached is False:
        return "SKIP", "actual_spend_below_threshold"
    if kind in ACT_ALERT_TYPES:
        return "ACT", "actual_alert_spend_unknown"
    return "SKIP", "unconfirmed_alert"


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


def resolve_budget_id(env=None, parsed_id=None):
    env = os.environ if env is None else env
    if parsed_id and str(parsed_id).strip().startswith("ocid1.budget."):
        return str(parsed_id).strip()
    raw = (env.get("BUDGET_ID") or "").strip()
    if raw and not raw.startswith("<"):
        return raw
    return ""


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


def _read_budget_spend(budget_client, budget_id, logger):
    """Return (actual_spend, amount) or (None, None) on failure."""
    if not budget_id:
        return None, None
    try:
        resp = budget_client.get_budget(budget_id)
        data = getattr(resp, "data", None)
        if data is None:
            return None, None
        actual = getattr(data, "actual_spend", None)
        amount = getattr(data, "amount", None)
        logger.info(
            "Budget %s actual_spend=%s amount=%s",
            budget_id,
            actual,
            amount,
        )
        return actual, amount
    except Exception as ex:
        logger.warning("get_budget failed for %s: %s", budget_id, ex)
        return None, None


def _skip_response(ctx, reason, alert_type=None, extra=None):
    from fdk import response

    payload = {"status": "SKIPPED", "reason": reason, "alertType": alert_type}
    if extra:
        payload.update(extra)
    return response.Response(
        ctx,
        response_data=json.dumps(payload),
        headers={"Content-Type": "application/json"},
    )


def handler(ctx, data: io.BytesIO = None):
    from fdk import response
    import oci

    logger = logging.getLogger()
    logger.info("Budget alert function invoked.")

    alert_type = None
    parsed_budget_id = None
    try:
        raw = data.getvalue() if data is not None else None
        parsed = parse_event(raw)
        alert_type = parsed.get("alert_type")
        parsed_budget_id = parsed.get("budget_id")
        logger.info(
            "Parsed alert_type=%s budget_id_set=%s",
            alert_type,
            bool(parsed_budget_id),
        )
    except Exception as parse_err:
        logger.warning("Could not parse event JSON: %s", parse_err)

    if is_reset_alert(alert_type):
        logger.info("Skipping: monthly budget RESET event.")
        return _skip_response(ctx, "Monthly budget reset event", alert_type)

    kind = (alert_type or "").strip().upper() or None
    if kind in SKIP_ALERT_TYPES:
        logger.info("Skipping: alert_type=%s", kind)
        return _skip_response(ctx, "alert_type=%s" % kind, alert_type)

    instance_ids = resolve_instance_ocids()
    namespace, bucket, lock_object = resolve_os_config()
    budget_id = resolve_budget_id(parsed_id=parsed_budget_id)

    try:
        signer = oci.auth.signers.get_resource_principals_signer()
        budget_client = oci.budget.BudgetClient(config={}, signer=signer)
        budget_client.base_client.retry_strategy = oci.retry.DEFAULT_RETRY_STRATEGY
        actual_spend, amount = _read_budget_spend(budget_client, budget_id, logger)
        spend_reached = spend_reached_threshold(actual_spend, amount)
        decision, reason = decide_spend_brake_action(alert_type, spend_reached)
        logger.info(
            "Decision=%s reason=%s spend_reached=%s actual=%s amount=%s",
            decision,
            reason,
            spend_reached,
            actual_spend,
            amount,
        )
        if decision == "SKIP":
            return _skip_response(
                ctx,
                reason,
                alert_type,
                extra={"actualSpend": actual_spend, "amount": amount},
            )

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
                    "decisionReason": reason,
                    "actualSpend": actual_spend,
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
