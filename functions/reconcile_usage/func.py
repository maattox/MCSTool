"""Usage API 48h ledger reconcile Function (product v1 source).

For UTC ledger days whose end is older than ~48 hours, read OCI Usage API
Ampere A1 OCPU-h / GB-h, write matching ``daily_overrides`` on
``ledger/usage.json``, bump ``revision``, and dirty ledger consumers.

Tracked placeholders only — live OCIDs stay in Function config / the private
file. Do not ``fn push`` / OCIR unless the operator authorizes it. Do not run
this against the live Forge lab or as an ad-hoc Usage API query from an agent
session (V1 Step 7.7 is code + mocked tests only).

Does **not** modify the $1 ``shutdown_vm`` Function.
"""

from __future__ import annotations

import copy
import datetime
import io
import json
import logging
import os

LEDGER_OBJECT_DEFAULT = "ledger/usage.json"
FLAGS_OBJECT_DEFAULT = "meta/flags.json"
LEDGER_VERSION = 2
OVERRIDE_NOTE = "usage_api_reconcile"
AGE_HOURS_DEFAULT = 48
EPSILON_HOURS = 0.01
LOOKBACK_DAYS = 62
SKU_A1_OCPU_PARTS = frozenset({"B93113"})
SKU_A1_MEMORY_PARTS = frozenset({"B93114"})
CONSUMERS = ("manager", "door", "vm1")
FLAG_CATEGORIES = ("ledger", "budget", "meta", "ip", "messages")


def format_utc(now_utc=None):
    stamp = now_utc or datetime.datetime.now(datetime.timezone.utc)
    if stamp.tzinfo is None:
        stamp = stamp.replace(tzinfo=datetime.timezone.utc)
    return stamp.astimezone(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def parse_iso(value):
    if not value:
        return None
    if isinstance(value, datetime.datetime):
        stamp = value
        if stamp.tzinfo is None:
            stamp = stamp.replace(tzinfo=datetime.timezone.utc)
        return stamp.astimezone(datetime.timezone.utc)
    text = str(value).strip()
    if text.endswith("Z"):
        text = text[:-1] + "+00:00"
    try:
        stamp = datetime.datetime.fromisoformat(text)
    except ValueError:
        return None
    if stamp.tzinfo is None:
        stamp = stamp.replace(tzinfo=datetime.timezone.utc)
    return stamp.astimezone(datetime.timezone.utc)


def _strip_placeholder(value):
    text = (value or "").strip()
    if not text or text.startswith("<"):
        return ""
    return text


def resolve_os_config(env=None):
    env = os.environ if env is None else env
    namespace = _strip_placeholder(env.get("OS_NAMESPACE"))
    bucket = _strip_placeholder(env.get("OS_BUCKET"))
    ledger = _strip_placeholder(env.get("OS_LEDGER_OBJECT")) or LEDGER_OBJECT_DEFAULT
    flags = _strip_placeholder(env.get("OS_FLAGS_OBJECT")) or FLAGS_OBJECT_DEFAULT
    return namespace, bucket, ledger, flags


def resolve_usage_config(env=None):
    env = os.environ if env is None else env
    tenancy = _strip_placeholder(env.get("TENANCY_OCID") or env.get("TENANT_ID"))
    compartment = _strip_placeholder(env.get("COMPARTMENT_OCID"))
    vm1 = _strip_placeholder(env.get("VM1_INSTANCE_OCID"))
    try:
        age_hours = float(env.get("AGE_HOURS") or AGE_HOURS_DEFAULT)
    except (TypeError, ValueError):
        age_hours = float(AGE_HOURS_DEFAULT)
    if age_hours <= 0:
        age_hours = float(AGE_HOURS_DEFAULT)
    return tenancy, compartment, vm1, age_hours


def parse_invoke_body(raw_bytes):
    """Return ``{dry_run: bool}`` from an optional JSON body."""
    if not raw_bytes:
        return {"dry_run": False}
    body = json.loads(raw_bytes)
    if not isinstance(body, dict):
        return {"dry_run": False}
    return {"dry_run": bool(body.get("dry_run"))}


def classify_usage_row(item):
    """Return ``ocpu``, ``memory``, or None. Ampere A1 only — never AMD Micro."""
    row = _as_row(item)
    part = str(row.get("sku_part_number") or "").strip().upper()
    sku = str(row.get("sku_name") or "").lower()
    unit = str(row.get("unit") or "").lower()
    shape = str(row.get("shape") or "").lower()
    if part in SKU_A1_OCPU_PARTS:
        return "ocpu"
    if part in SKU_A1_MEMORY_PARTS:
        return "memory"
    if "ampere a1" in sku and "ocpu" in sku:
        return "ocpu"
    if "ampere a1" in sku and "memory" in sku:
        return "memory"
    if "a1.flex" in shape and "ocpu" in unit:
        return "ocpu"
    if "a1.flex" in shape and ("gb" in unit or "memory" in unit):
        return "memory"
    return None


def last_eligible_day_end(now_utc, age_hours=AGE_HOURS_DEFAULT):
    """Exclusive end of the last UTC day that is fully older than ``age_hours``."""
    now = parse_iso(now_utc) or datetime.datetime.now(datetime.timezone.utc)
    cutoff = now - datetime.timedelta(hours=float(age_hours))
    if cutoff.hour == 0 and cutoff.minute == 0 and cutoff.second == 0 and cutoff.microsecond == 0:
        return cutoff
    return datetime.datetime(cutoff.year, cutoff.month, cutoff.day, tzinfo=datetime.timezone.utc)


def eligible_day_keys(now_utc, age_hours=AGE_HOURS_DEFAULT, lookback_days=LOOKBACK_DAYS):
    last_end = last_eligible_day_end(now_utc, age_hours)
    start = last_end - datetime.timedelta(days=int(lookback_days))
    keys = []
    cursor = start
    while cursor + datetime.timedelta(days=1) <= last_end:
        keys.append(cursor.date().isoformat())
        cursor += datetime.timedelta(days=1)
    return keys, start, last_end


def _as_row(item):
    if isinstance(item, dict):
        return item
    return {
        "time_usage_started": getattr(item, "time_usage_started", None),
        "time_usage_ended": getattr(item, "time_usage_ended", None),
        "sku_name": getattr(item, "sku_name", None),
        "sku_part_number": getattr(item, "sku_part_number", None),
        "unit": getattr(item, "unit", None),
        "shape": getattr(item, "shape", None),
        "resource_id": getattr(item, "resource_id", None),
        "computed_quantity": getattr(item, "computed_quantity", None),
        "is_forecast": getattr(item, "is_forecast", False),
    }


def fold_usage_items(items, vm1_instance_ocid=""):
    """Sum Ampere A1 quantities by UTC day. Skip forecasts and non-A1 SKUs."""
    wanted = (vm1_instance_ocid or "").strip()
    by_day = {}
    for item in items or []:
        row = _as_row(item)
        if row.get("is_forecast"):
            continue
        kind = classify_usage_row(row)
        if kind is None:
            continue
        resource = str(row.get("resource_id") or "").strip()
        if wanted and resource and resource != wanted:
            continue
        started = parse_iso(row.get("time_usage_started"))
        if started is None:
            continue
        key = started.date().isoformat()
        try:
            qty = float(row.get("computed_quantity") or 0)
        except (TypeError, ValueError):
            qty = 0.0
        if qty <= 0:
            continue
        bucket = by_day.setdefault(key, {"ocpu_hours": 0.0, "gb_hours": 0.0})
        if kind == "ocpu":
            bucket["ocpu_hours"] += qty
        else:
            bucket["gb_hours"] += qty
    return by_day


def _interval_hours(item, window_start, window_end, now):
    start = parse_iso(item.get("started_at"))
    if start is None:
        return 0.0
    end = parse_iso(item.get("stopped_at")) or now
    start = max(start, window_start)
    end = min(end, window_end)
    if end <= start:
        return 0.0
    return (end - start).total_seconds() / 3600.0


def interval_day_totals(ledger, day_key, now_utc):
    """Interval-derived totals for one UTC day (ignores existing overrides)."""
    now = parse_iso(now_utc) or datetime.datetime.now(datetime.timezone.utc)
    day = datetime.date.fromisoformat(day_key)
    start = datetime.datetime(day.year, day.month, day.day, tzinfo=datetime.timezone.utc)
    end = start + datetime.timedelta(days=1)
    uptime = ocpu = gb = 0.0
    shape_ocpus = 0.0
    shape_gb = 0.0
    shape_hours = 0.0
    for item in ledger.get("intervals") or []:
        if not isinstance(item, dict):
            continue
        hours = _interval_hours(item, start, end, now)
        if hours <= 0:
            continue
        uptime += hours
        ocpus = float(item.get("ocpus") or 0)
        memory = float(item.get("memory_gb") or 0)
        ocpu += hours * ocpus
        gb += hours * memory
        shape_ocpus += hours * ocpus
        shape_gb += hours * memory
        shape_hours += hours
    avg_ocpus = (shape_ocpus / shape_hours) if shape_hours > 0 else 0.0
    avg_gb = (shape_gb / shape_hours) if shape_hours > 0 else 0.0
    return {
        "uptime_hours": uptime,
        "ocpu_hours": ocpu,
        "gb_hours": gb,
        "ocpus": avg_ocpus,
        "memory_gb": avg_gb,
    }


def _close(a, b, eps=EPSILON_HOURS):
    return abs(float(a) - float(b)) <= float(eps)


def _is_manual_override(override):
    if not isinstance(override, dict):
        return False
    note = str(override.get("note") or "").strip()
    return not note.startswith(OVERRIDE_NOTE)


def _derive_uptime(api_ocpu, api_gb, interval_totals):
    ocpus = float(interval_totals.get("ocpus") or 0)
    memory = float(interval_totals.get("memory_gb") or 0)
    if ocpus > 0:
        return float(api_ocpu) / ocpus
    if memory > 0:
        return float(api_gb) / memory
    return float(interval_totals.get("uptime_hours") or 0)


def apply_reconcile(ledger, api_by_day, now_utc, age_hours=AGE_HOURS_DEFAULT):
    """Write Usage API daily_overrides for eligible days. Preserve intervals.

    Never plants a zero-API override over interval hours (Always Free rows can
    be missing from Usage API). Manual overrides (note not
    ``usage_api_reconcile``) are left untouched.
    """
    out = copy.deepcopy(ledger) if isinstance(ledger, dict) else {
        "version": LEDGER_VERSION,
        "revision": 0,
        "intervals": [],
        "daily_overrides": {},
        "idle_since": None,
        "last_budget_warn_at": None,
    }
    out.setdefault("daily_overrides", {})
    if not isinstance(out["daily_overrides"], dict):
        out["daily_overrides"] = {}
    eligible, _, _ = eligible_day_keys(now_utc, age_hours)
    eligible_set = set(eligible)
    changes = []
    overrides = out["daily_overrides"]
    now_stamp = format_utc(now_utc)

    for day_key in sorted(api_by_day or {}):
        if day_key not in eligible_set:
            changes.append({"day": day_key, "action": "skipped_too_recent"})
            continue
        api = api_by_day[day_key] or {}
        api_ocpu = float(api.get("ocpu_hours") or 0)
        api_gb = float(api.get("gb_hours") or 0)
        if api_ocpu <= EPSILON_HOURS and api_gb <= EPSILON_HOURS:
            changes.append({"day": day_key, "action": "skipped_no_api"})
            continue
        existing = overrides.get(day_key)
        if _is_manual_override(existing):
            changes.append({"day": day_key, "action": "preserved_manual"})
            continue
        interval = interval_day_totals(out, day_key, now_utc)
        uptime = _derive_uptime(api_ocpu, api_gb, interval)
        proposed = {
            "uptime_hours": round(uptime, 6),
            "ocpu_hours": round(api_ocpu, 6),
            "gb_hours": round(api_gb, 6),
            "note": OVERRIDE_NOTE,
            "updated_at": now_stamp,
        }
        if isinstance(existing, dict) and (
            _close(existing.get("uptime_hours") or 0, proposed["uptime_hours"])
            and _close(existing.get("ocpu_hours") or 0, proposed["ocpu_hours"])
            and _close(existing.get("gb_hours") or 0, proposed["gb_hours"])
        ):
            changes.append({"day": day_key, "action": "unchanged"})
            continue
        if (
            not isinstance(existing, dict)
            and _close(interval["ocpu_hours"], api_ocpu)
            and _close(interval["gb_hours"], api_gb)
        ):
            changes.append({"day": day_key, "action": "matched_intervals"})
            continue
        overrides[day_key] = proposed
        changes.append(
            {
                "day": day_key,
                "action": "wrote",
                "ocpu_hours": proposed["ocpu_hours"],
                "gb_hours": proposed["gb_hours"],
            }
        )

    wrote = [c for c in changes if c.get("action") == "wrote"]
    if wrote:
        try:
            rev = int(out.get("revision") or 0) + 1
        except (TypeError, ValueError):
            rev = 1
        out["revision"] = rev
        out["version"] = max(int(out.get("version") or 1), LEDGER_VERSION)
    return out, changes, bool(wrote)


def dirty_ledger_consumers(flags, now_utc=None):
    """Set ledger.manager/door/vm1 true. Preserve other categories."""
    out = copy.deepcopy(flags) if isinstance(flags, dict) else {}
    out.setdefault("version", 1)
    cats = out.get("categories")
    if not isinstance(cats, dict):
        cats = {}
        out["categories"] = cats
    for cat in FLAG_CATEGORIES:
        src = cats.get(cat) if isinstance(cats.get(cat), dict) else {}
        cats[cat] = {c: bool(src[c]) if c in src else False for c in CONSUMERS}
    ledger = cats.setdefault("ledger", {c: False for c in CONSUMERS})
    for consumer in CONSUMERS:
        ledger[consumer] = True
    out["updated_at"] = format_utc(now_utc)
    return out


def empty_flags(now_utc=None):
    return {
        "version": 1,
        "updated_at": format_utc(now_utc),
        "categories": {
            cat: {c: False for c in CONSUMERS} for cat in FLAG_CATEGORIES
        },
        "help": "Writer sets consumers dirty; consumer clears only its own bit after a successful pull.",
    }


def _get_json_with_etag(os_client, namespace, bucket, object_name):
    import oci

    try:
        resp = os_client.get_object(
            namespace_name=namespace,
            bucket_name=bucket,
            object_name=object_name,
            retry_strategy=oci.retry.DEFAULT_RETRY_STRATEGY,
        )
        etag = None
        headers = getattr(resp, "headers", None)
        if headers:
            etag = headers.get("etag")
        if etag is None:
            etag = getattr(resp, "etag", None)
        body = json.loads(resp.data.content.decode("utf-8"))
        return body, etag
    except Exception as ex:
        status = getattr(ex, "status", None)
        text = str(ex).lower()
        if status == 404 or "404" in text or "notfound" in text or "not found" in text:
            return None, None
        raise


def _put_json(os_client, namespace, bucket, object_name, data, if_match=None):
    import oci

    body = json.dumps(data, indent=2) + "\n"
    kwargs = {
        "namespace_name": namespace,
        "bucket_name": bucket,
        "object_name": object_name,
        "put_object_body": body.encode("utf-8"),
        "content_type": "application/json",
        "retry_strategy": oci.retry.DEFAULT_RETRY_STRATEGY,
    }
    if if_match:
        kwargs["if_match"] = if_match
    return os_client.put_object(**kwargs)


def fetch_usage_items(usage_client, tenancy_id, compartment_id, time_start, time_end):
    """Paginated Usage API USAGE rows. Caller supplies a client (not used in unit tests)."""
    import oci
    from oci.usage_api.models import Dimension, Filter, RequestSummarizedUsagesDetails

    details = RequestSummarizedUsagesDetails(
        tenant_id=tenancy_id,
        time_usage_started=time_start,
        time_usage_ended=time_end,
        granularity=RequestSummarizedUsagesDetails.GRANULARITY_DAILY,
        query_type="USAGE",
        group_by=["skuName", "skuPartNumber", "unit", "resourceId", "shape"],
        compartment_depth=1.0,
        filter=Filter(
            operator="AND",
            dimensions=[Dimension(key="compartmentId", value=compartment_id)],
        )
        if compartment_id
        else None,
    )
    items = []
    page = None
    while True:
        kwargs = {"retry_strategy": oci.retry.DEFAULT_RETRY_STRATEGY}
        if page:
            kwargs["page"] = page
        resp = usage_client.request_summarized_usages(details, **kwargs)
        data = getattr(resp, "data", None)
        chunk = getattr(data, "items", None) if data is not None else None
        if chunk:
            items.extend(chunk)
        page = getattr(resp, "next_page", None)
        if not page:
            headers = getattr(resp, "headers", None) or {}
            page = headers.get("opc-next-page")
        if not page:
            break
    return items


def handler(ctx, data: io.BytesIO = None):
    from fdk import response
    import oci

    logger = logging.getLogger()
    logger.info("Usage API ledger reconcile invoked.")

    try:
        raw = data.getvalue() if data is not None else None
        invoke = parse_invoke_body(raw) if raw else {"dry_run": False}
    except Exception as parse_err:
        logger.warning("Could not parse invoke JSON: %s", parse_err)
        invoke = {"dry_run": False}

    namespace, bucket, ledger_object, flags_object = resolve_os_config()
    tenancy, compartment, vm1, age_hours = resolve_usage_config()
    now = datetime.datetime.now(datetime.timezone.utc)
    _, query_start, query_end = eligible_day_keys(now, age_hours)

    if not namespace or not bucket:
        return response.Response(
            ctx,
            response_data=json.dumps(
                {
                    "status": "ERROR",
                    "message": "OS_NAMESPACE or OS_BUCKET missing",
                }
            ),
            headers={"Content-Type": "application/json"},
        )
    if not tenancy:
        return response.Response(
            ctx,
            response_data=json.dumps(
                {"status": "ERROR", "message": "TENANCY_OCID missing"}
            ),
            headers={"Content-Type": "application/json"},
        )
    if query_end <= query_start:
        return response.Response(
            ctx,
            response_data=json.dumps(
                {
                    "status": "SKIPPED",
                    "reason": "no UTC days older than the Usage API lag window",
                }
            ),
            headers={"Content-Type": "application/json"},
        )

    try:
        signer = oci.auth.signers.get_resource_principals_signer()
        os_client = oci.object_storage.ObjectStorageClient(config={}, signer=signer)
        os_client.base_client.retry_strategy = oci.retry.DEFAULT_RETRY_STRATEGY
        usage_client = oci.usage_api.UsageapiClient(config={}, signer=signer)
        usage_client.base_client.retry_strategy = oci.retry.DEFAULT_RETRY_STRATEGY

        ledger, etag = _get_json_with_etag(
            os_client, namespace, bucket, ledger_object
        )
        if not isinstance(ledger, dict):
            return response.Response(
                ctx,
                response_data=json.dumps(
                    {
                        "status": "SKIPPED",
                        "reason": "ledger/usage.json missing",
                    }
                ),
                headers={"Content-Type": "application/json"},
            )

        items = fetch_usage_items(
            usage_client, tenancy, compartment, query_start, query_end
        )
        api_by_day = fold_usage_items(items, vm1_instance_ocid=vm1)
        updated, changes, wrote = apply_reconcile(
            ledger, api_by_day, now, age_hours=age_hours
        )
        wrote_days = [c["day"] for c in changes if c.get("action") == "wrote"]

        if invoke.get("dry_run") or not wrote:
            return response.Response(
                ctx,
                response_data=json.dumps(
                    {
                        "status": "SKIPPED" if not wrote else "DRY_RUN",
                        "reason": "dry_run" if invoke.get("dry_run") else "no ledger days to write",
                        "wroteDays": wrote_days,
                        "changes": changes,
                        "revision": updated.get("revision"),
                    }
                ),
                headers={"Content-Type": "application/json"},
            )

        try:
            _put_json(
                os_client,
                namespace,
                bucket,
                ledger_object,
                updated,
                if_match=etag,
            )
        except Exception as put_err:
            text = str(put_err).lower()
            if etag and (
                "412" in text or "precondition" in text or "if-match" in text
            ):
                logger.warning("Ledger If-Match 412; refresh and retry once.")
                latest, etag2 = _get_json_with_etag(
                    os_client, namespace, bucket, ledger_object
                )
                if not isinstance(latest, dict):
                    raise
                updated, changes, wrote = apply_reconcile(
                    latest, api_by_day, now, age_hours=age_hours
                )
                wrote_days = [c["day"] for c in changes if c.get("action") == "wrote"]
                if not wrote:
                    return response.Response(
                        ctx,
                        response_data=json.dumps(
                            {
                                "status": "SKIPPED",
                                "reason": "no ledger days to write after 412 refresh",
                                "changes": changes,
                            }
                        ),
                        headers={"Content-Type": "application/json"},
                    )
                _put_json(
                    os_client,
                    namespace,
                    bucket,
                    ledger_object,
                    updated,
                    if_match=etag2,
                )
            else:
                raise

        flags, flags_etag = _get_json_with_etag(
            os_client, namespace, bucket, flags_object
        )
        if not isinstance(flags, dict):
            flags = empty_flags(now)
            flags_etag = None
        flags = dirty_ledger_consumers(flags, now)
        _put_json(
            os_client,
            namespace,
            bucket,
            flags_object,
            flags,
            if_match=flags_etag,
        )

        logger.info(
            "Usage reconcile wrote days=%s revision=%s",
            wrote_days,
            updated.get("revision"),
        )
        return response.Response(
            ctx,
            response_data=json.dumps(
                {
                    "status": "SUCCESS",
                    "action": "RECONCILE",
                    "wroteDays": wrote_days,
                    "changes": changes,
                    "revision": updated.get("revision"),
                }
            ),
            headers={"Content-Type": "application/json"},
        )
    except Exception as ex:
        logger.error("Usage reconcile failed: %s", ex)
        return response.Response(
            ctx,
            response_data=json.dumps({"status": "ERROR", "message": str(ex)}),
            headers={"Content-Type": "application/json"},
        )
