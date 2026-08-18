"""Usage ledger helpers for the on-VM idle agent (stdlib only).

Interval rows always carry ``ocpus`` and ``memory_gb`` so OCPU-h / GB-h totals
stay correct when the instance shape changes across sessions (future scale-up /
scale-down). Totals multiply window hours by each row's own shape fields.
"""

from __future__ import annotations

import calendar
import json
import os
import re
import subprocess
import uuid
from datetime import date, datetime, timezone
from typing import Any

# Document schema version. ``revision`` is a separate monotonic publish counter.
LEDGER_VERSION = 2


def utc_now() -> datetime:
    return datetime.now(timezone.utc)


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


def to_iso(dt: datetime) -> str:
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def empty_ledger() -> dict[str, Any]:
    return {
        "version": LEDGER_VERSION,
        "revision": 0,
        "intervals": [],
        "daily_overrides": {},
        "idle_since": None,
        "last_budget_warn_at": None,
    }


def load_ledger(path: str) -> dict[str, Any]:
    if not os.path.exists(path):
        return empty_ledger()
    with open(path, "r", encoding="utf-8") as f:
        data = json.load(f)
    if not isinstance(data, dict):
        return empty_ledger()
    data.setdefault("version", LEDGER_VERSION)
    data.setdefault("revision", 0)
    data.setdefault("intervals", [])
    data.setdefault("daily_overrides", {})
    data.setdefault("idle_since", None)
    data.setdefault("last_budget_warn_at", None)
    return data


def save_ledger(path: str, ledger: dict[str, Any]) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(ledger, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def bump_revision(ledger: dict[str, Any]) -> int:
    """Increment publish revision (optimistic concurrency helper)."""
    try:
        rev = int(ledger.get("revision") or 0) + 1
    except (TypeError, ValueError):
        rev = 1
    ledger["revision"] = rev
    ledger["version"] = max(int(ledger.get("version") or 1), LEDGER_VERSION)
    return rev


def record_start(
    ledger: dict[str, Any],
    *,
    ocpus: float,
    memory_gb: float,
    source: str,
) -> dict[str, Any]:
    now = utc_now()
    record_stop(ledger, source=f"{source}_preclose")
    interval = {
        "id": str(uuid.uuid4()),
        "started_at": to_iso(now),
        "stopped_at": None,
        # Shape at interval open — required for future scale-up/down sessions.
        "ocpus": float(ocpus),
        "memory_gb": float(memory_gb),
        "source": source,
    }
    ledger.setdefault("intervals", []).append(interval)
    ledger["idle_since"] = None
    return interval


def record_stop(
    ledger: dict[str, Any],
    *,
    source: str = "stop",
    stop_uncertain: bool = False,
    uncertain_reason: str | None = None,
    stopped_at: str | datetime | None = None,
) -> dict[str, Any] | None:
    """Close all open intervals (corruption-safe). Returns the last one closed."""
    if isinstance(stopped_at, datetime):
        stamp = to_iso(stopped_at)
    elif isinstance(stopped_at, str) and stopped_at.strip():
        stamp = stopped_at.strip()
    else:
        stamp = to_iso(utc_now())
    closed: dict[str, Any] | None = None
    for item in ledger.get("intervals") or []:
        if not isinstance(item, dict) or item.get("stopped_at"):
            continue
        started = parse_iso(item.get("started_at"))
        stop_dt = parse_iso(stamp) or utc_now()
        if started and stop_dt < started:
            stop_dt = started
            stamp = to_iso(stop_dt)
        item["stopped_at"] = stamp
        item["stop_source"] = source
        if stop_uncertain:
            item["stop_uncertain"] = True
            if uncertain_reason:
                item["uncertain_reason"] = uncertain_reason
        else:
            item.pop("stop_uncertain", None)
        closed = item
    if closed is not None:
        ledger["idle_since"] = None
    return closed


def normalize_open_intervals(ledger: dict[str, Any]) -> int:
    """Ensure at most one open interval; close older opens at the next start."""
    opens = [
        item
        for item in (ledger.get("intervals") or [])
        if isinstance(item, dict) and not item.get("stopped_at")
    ]
    if len(opens) <= 1:
        return 0
    opens.sort(key=lambda item: str(item.get("started_at") or ""))
    closed = 0
    for prev, nxt in zip(opens, opens[1:]):
        stop_at = nxt.get("started_at") or to_iso(utc_now())
        prev_start = parse_iso(prev.get("started_at"))
        stop_dt = parse_iso(stop_at) or utc_now()
        if prev_start and stop_dt <= prev_start:
            stop_at = to_iso(prev_start)
        prev["stopped_at"] = stop_at
        prev["stop_source"] = prev.get("stop_source") or "normalize_open"
        closed += 1
    return closed


def merge_ledgers_for_boot(
    remote: dict[str, Any],
    local: dict[str, Any],
) -> dict[str, Any]:
    """Merge OS pull with on-disk local knowledge after an unclean SoftStop.

    Remote (Object Storage / door heal) is the base. Local intervals are unioned
    by id so a successful local ``idle_or_budget_stop`` / ``boot_preclose`` is not
    discarded when OS still has an open or door-approximate close. When both
    sides have ``stopped_at``, keep the **earlier** time (never extend uptime).
    Prefer a definitive local stop over ``stop_uncertain`` when times tie or
    local is earlier.
    """
    by_id: dict[str, dict[str, Any]] = {}
    for src in (remote.get("intervals") or [], local.get("intervals") or []):
        for item in src:
            if not isinstance(item, dict):
                continue
            iid = str(item.get("id") or "")
            if not iid:
                continue
            prev = by_id.get(iid)
            if prev is None:
                by_id[iid] = dict(item)
                continue
            merged = dict(prev)
            for key, val in item.items():
                if val is not None:
                    merged[key] = val
            prev_stop = parse_iso(prev.get("stopped_at"))
            new_stop = parse_iso(item.get("stopped_at"))
            if prev_stop and new_stop:
                if new_stop < prev_stop:
                    merged["stopped_at"] = item["stopped_at"]
                    if item.get("stop_source"):
                        merged["stop_source"] = item["stop_source"]
                    if "stop_uncertain" in item:
                        merged["stop_uncertain"] = item["stop_uncertain"]
                    if item.get("uncertain_reason") is not None:
                        merged["uncertain_reason"] = item["uncertain_reason"]
                else:
                    merged["stopped_at"] = prev["stopped_at"]
                    if prev.get("stop_source"):
                        merged["stop_source"] = prev["stop_source"]
                    if "stop_uncertain" in prev:
                        merged["stop_uncertain"] = prev["stop_uncertain"]
            elif prev_stop and not new_stop:
                merged["stopped_at"] = prev["stopped_at"]
                if prev.get("stop_source"):
                    merged["stop_source"] = prev["stop_source"]
            elif new_stop and not prev_stop:
                merged["stopped_at"] = item["stopped_at"]
                if item.get("stop_source"):
                    merged["stop_source"] = item["stop_source"]

            # Prefer definitive stop over uncertain when we kept a closed row.
            if merged.get("stopped_at"):
                local_def = (
                    bool(item.get("stopped_at"))
                    and not item.get("stop_uncertain")
                )
                prev_def = (
                    bool(prev.get("stopped_at"))
                    and not prev.get("stop_uncertain")
                )
                if local_def or prev_def:
                    merged["stop_uncertain"] = False
                    if local_def and item.get("stop_source"):
                        merged["stop_source"] = item.get("stop_source") or merged.get(
                            "stop_source"
                        )
                    elif prev_def and prev.get("stop_source"):
                        merged["stop_source"] = prev.get("stop_source") or merged.get(
                            "stop_source"
                        )
            by_id[iid] = merged

    out = empty_ledger()
    out["intervals"] = sorted(
        by_id.values(), key=lambda x: str(x.get("started_at") or "")
    )
    overrides: dict[str, Any] = {}
    overrides.update(local.get("daily_overrides") or {})
    overrides.update(remote.get("daily_overrides") or {})
    out["daily_overrides"] = overrides
    out["idle_since"] = remote.get("idle_since")
    out["last_budget_warn_at"] = remote.get("last_budget_warn_at") or local.get(
        "last_budget_warn_at"
    )
    try:
        out["revision"] = max(
            int(remote.get("revision") or 0),
            int(local.get("revision") or 0),
        )
    except (TypeError, ValueError):
        out["revision"] = int(remote.get("revision") or 0) or 0
    out["version"] = max(
        int(remote.get("version") or 1),
        int(local.get("version") or 1),
        LEDGER_VERSION,
    )
    normalize_open_intervals(out)
    return out


def list_boots() -> list[dict[str, Any]]:
    """Parse ``journalctl --list-boots`` into oldest→newest boot windows.

    Each entry: idx (int), boot_id (str), first (datetime), last (datetime|None).
    Current boot usually has idx 0. Overlapping ``last`` values from crashes are
    repaired by callers using the next boot's ``first`` as the end bound.
    """
    try:
        out = subprocess.check_output(
            ["journalctl", "--list-boots", "--no-pager"],
            stderr=subprocess.DEVNULL,
            text=True,
            timeout=45,
        )
    except (OSError, subprocess.SubprocessError):
        return []

    boots: list[dict[str, Any]] = []
    # Examples:
    #  -1 6f5c... Mon 2026-08-10 05:24:05 UTC–Mon 2026-08-10 18:04:21 UTC
    #   0 abc... Mon 2026-08-10 18:04:21 UTC–Mon 2026-08-10 19:00:00 UTC
    # Do NOT split on ASCII '-' — dates contain hyphens (broke as "Tue 2026").
    row_re = re.compile(r"^\s*(-?\d+)\s+([0-9a-fA-F-]{8,})\s+(.+)$")
    time_re = re.compile(
        r"[A-Za-z]{3}\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}:\d{2}\s+UTC"
    )
    for line in out.splitlines():
        m = row_re.match(line.strip())
        if not m:
            continue
        idx = int(m.group(1))
        boot_id = m.group(2)
        rest = m.group(3)
        stamps = time_re.findall(rest)
        first = _parse_journal_list_time(stamps[0]) if stamps else None
        last = _parse_journal_list_time(stamps[1]) if len(stamps) > 1 else None
        if first is None:
            continue
        boots.append(
            {
                "idx": idx,
                "boot_id": boot_id,
                "first": first,
                "last": last,
            }
        )
    boots.sort(key=lambda b: b["first"])
    return boots


def _parse_journal_list_time(text: str) -> datetime | None:
    text = (text or "").strip()
    if not text or text.lower() in {"n/a", "none"}:
        return None
    m = re.search(
        r"(\d{4}-\d{2}-\d{2})\s+(\d{2}:\d{2}:\d{2})",
        text,
    )
    if not m:
        return None
    try:
        return datetime(
            int(m.group(1)[0:4]),
            int(m.group(1)[5:7]),
            int(m.group(1)[8:10]),
            int(m.group(2)[0:2]),
            int(m.group(2)[3:5]),
            int(m.group(2)[6:8]),
            tzinfo=timezone.utc,
        )
    except ValueError:
        return None


def verification_horizon(boots: list[dict[str, Any]] | None = None) -> datetime | None:
    """Earliest boot start present in local journals — do not invent history before this."""
    boots = boots if boots is not None else list_boots()
    if not boots:
        return None
    return boots[0]["first"]


def _boot_end(boots: list[dict[str, Any]], index: int) -> datetime | None:
    """Credible end for boots[index]: next boot start (crash-safe) or listed last."""
    cur = boots[index]
    if index + 1 < len(boots):
        nxt = boots[index + 1]["first"]
        last = cur.get("last")
        if isinstance(last, datetime) and last <= nxt:
            return last
        return nxt
    last = cur.get("last")
    return last if isinstance(last, datetime) else None


def close_opens_from_evidence(
    ledger: dict[str, Any],
    *,
    lease: dict[str, Any] | None = None,
    current_boot_start: datetime | None = None,
) -> int:
    """Close open intervals that belong to a prior boot using lease / list-boots.

    Never touches intervals that start at/after ``current_boot_start``.
    """
    boots = list_boots()
    if current_boot_start is None:
        for b in boots:
            if int(b.get("idx", -999)) == 0:
                current_boot_start = b["first"]
                break
        if current_boot_start is None and boots:
            current_boot_start = boots[-1]["first"]

    lease_stop = None
    if isinstance(lease, dict):
        lease_stop = parse_iso(lease.get("last_heartbeat_at"))

    closed = 0
    for item in ledger.get("intervals") or []:
        if not isinstance(item, dict) or item.get("stopped_at"):
            continue
        started = parse_iso(item.get("started_at"))
        if started is None:
            continue
        if current_boot_start and started >= current_boot_start:
            continue

        candidates: list[datetime] = []
        if lease_stop and lease_stop >= started:
            if not current_boot_start or lease_stop <= current_boot_start:
                candidates.append(lease_stop)
        # End of the boot window that contained this start.
        for i, b in enumerate(boots):
            end = _boot_end(boots, i)
            if end is None:
                continue
            if b["first"] <= started < end:
                if not current_boot_start or end <= current_boot_start:
                    candidates.append(end)
                break
        if current_boot_start and started < current_boot_start:
            candidates.append(current_boot_start)

        if not candidates:
            continue
        chosen = min(candidates)
        item["stopped_at"] = to_iso(chosen)
        if lease_stop and chosen == lease_stop:
            item["stop_source"] = "boot_close_lease_heartbeat"
            item["stop_uncertain"] = False
            item["uncertain_reason"] = (
                "closed prior open interval at lease last_heartbeat_at"
            )
        else:
            item["stop_source"] = "boot_close_list_boots"
            item["stop_uncertain"] = False
            item["uncertain_reason"] = (
                "closed prior open interval from journalctl --list-boots"
            )
        closed += 1
    return closed


def fill_missing_boot_intervals(
    ledger: dict[str, Any],
    *,
    ocpus: float,
    memory_gb: float,
    match_tolerance_sec: float = 120.0,
) -> int:
    """Add closed intervals for completed boots that have no overlapping ledger row.

    Skips the current boot (idx 0 / last). Preserves any ledger history that
    predates the journal verification horizon (those rows are left untouched;
    we only *add* verifiable boots missing from the ledger).
    """
    boots = list_boots()
    if len(boots) < 2:
        return 0
    intervals = ledger.setdefault("intervals", [])
    added = 0
    # Exclude current boot (highest first, or idx==0).
    completed = [b for b in boots if int(b.get("idx", -999)) != 0]
    if not completed and len(boots) >= 2:
        completed = boots[:-1]

    for i, b in enumerate(boots):
        if b not in completed:
            continue
        start = b["first"]
        end = _boot_end(boots, i)
        if end is None or end <= start:
            continue
        # Skip if any interval starts near this boot or overlaps the window.
        overlap = False
        for item in intervals:
            if not isinstance(item, dict):
                continue
            s = parse_iso(item.get("started_at"))
            if s is None:
                continue
            e = parse_iso(item.get("stopped_at")) or utc_now()
            if abs((s - start).total_seconds()) <= match_tolerance_sec:
                overlap = True
                break
            if s < end and e > start:
                overlap = True
                break
        if overlap:
            continue
        intervals.append(
            {
                "id": str(uuid.uuid4()),
                "started_at": to_iso(start),
                "stopped_at": to_iso(end),
                "ocpus": float(ocpus),
                "memory_gb": float(memory_gb),
                "source": "boot_reconcile_list_boots",
                "stop_source": "boot_reconcile_list_boots",
            }
        )
        added += 1
    if added:
        intervals.sort(key=lambda x: str(x.get("started_at") or ""))
    return added


def _journal_stop_candidates(
    started: datetime,
    upper_bound: datetime,
) -> list[datetime]:
    """Stop times from minecraft.service journals in (started, upper_bound].

    Only 'Stopped'/'Stopping' lines count. upper_bound is normally the door
    heal estimate — never search past it (avoids current-boot start noise).
    """
    since = started.strftime("%Y-%m-%d %H:%M:%S UTC")
    until = upper_bound.strftime("%Y-%m-%d %H:%M:%S UTC")
    cmds = [
        # Previous boot only (session that ended while we were off).
        [
            "journalctl",
            "-b",
            "-1",
            "-u",
            "minecraft.service",
            f"--since={since}",
            f"--until={until}",
            "--no-pager",
            "-o",
            "short-unix",
        ],
        # Bounded window across boots (still capped at door estimate).
        [
            "journalctl",
            "-u",
            "minecraft.service",
            f"--since={since}",
            f"--until={until}",
            "--no-pager",
            "-o",
            "short-unix",
        ],
    ]
    found: list[datetime] = []
    seen: set[int] = set()
    for cmd in cmds:
        try:
            out = subprocess.check_output(
                cmd, stderr=subprocess.DEVNULL, text=True, timeout=45
            )
        except (OSError, subprocess.SubprocessError):
            continue
        for line in out.splitlines():
            low = line.lower()
            if "stopped" not in low and "stopping" not in low:
                continue
            if "minecraft" not in low and "minecraft.service" not in low:
                # short-unix lines may omit unit name; keep Stopped/Stopping
                # when unit filter already scoped the query.
                if "-u" not in cmd:
                    continue
            m = re.match(r"^(\d+)(?:\.\d+)?\s", line.strip())
            if not m:
                continue
            try:
                epoch = int(m.group(1))
                ts = datetime.fromtimestamp(epoch, tz=timezone.utc)
            except (OSError, OverflowError, ValueError):
                continue
            if ts <= started or ts > upper_bound:
                continue
            if epoch in seen:
                continue
            seen.add(epoch)
            found.append(ts)
    found.sort()
    return found


def repair_uncertain_stops(
    ledger: dict[str, Any],
    *,
    lease: dict[str, Any] | None = None,
) -> int:
    """Refine or accept door/lease `stop_uncertain` intervals after VM1 boots.

    Existing ``stopped_at`` is an **upper bound**. Refine may only move the stop
    **earlier**, never later. Prefer minecraft journal, then list-boots end, then
    lease heartbeat, else keep the estimate.
    """
    repaired = 0
    boots = list_boots()
    lease_hb = parse_iso(lease.get("last_heartbeat_at")) if isinstance(lease, dict) else None

    for item in ledger.get("intervals") or []:
        if not isinstance(item, dict) or not item.get("stop_uncertain"):
            continue
        started = parse_iso(item.get("started_at"))
        upper = parse_iso(item.get("stopped_at"))
        if started is None:
            continue
        if upper is None or upper <= started:
            upper = utc_now()
            item["stopped_at"] = to_iso(upper)

        candidates: list[tuple[datetime, str]] = []
        journal_hits = _journal_stop_candidates(started, upper)
        if journal_hits:
            candidates.append((journal_hits[-1], "boot_repair_journal"))
        for i, b in enumerate(boots):
            end = _boot_end(boots, i)
            if end is None:
                continue
            if b["first"] <= started < end and started < end <= upper:
                candidates.append((end, "boot_repair_list_boots"))
                break
        if lease_hb and started < lease_hb <= upper:
            candidates.append((lease_hb, "boot_repair_lease_heartbeat"))

        if candidates:
            # Prefer earliest credible refine (never later than upper).
            chosen, source = min(candidates, key=lambda pair: pair[0])
            item["stopped_at"] = to_iso(chosen)
            item["stop_source"] = source
            item["uncertain_reason"] = (
                f"refined stop earlier via {source} (not later than prior estimate)"
            )
        else:
            item["stop_source"] = "boot_accepted_estimate"
            item["uncertain_reason"] = (
                "accepted prior estimate; no journal/list-boots/lease refine"
            )

        item["stop_uncertain"] = False
        item["uncertain_repaired_at"] = to_iso(utc_now())
        repaired += 1
    return repaired


def _interval_hours(
    item: dict[str, Any],
    window_start: datetime,
    window_end: datetime,
    now: datetime,
) -> float:
    start = parse_iso(item.get("started_at"))
    if start is None:
        return 0.0
    end = parse_iso(item.get("stopped_at")) or now
    start = max(start, window_start)
    end = min(end, window_end)
    if end <= start:
        return 0.0
    return (end - start).total_seconds() / 3600.0


def totals(
    ledger: dict[str, Any],
    window_start: datetime,
    window_end: datetime,
    now: datetime | None = None,
) -> tuple[float, float, float]:
    now = now or utc_now()
    uptime = ocpu = gb = 0.0
    for item in ledger.get("intervals") or []:
        hours = _interval_hours(item, window_start, window_end, now)
        if hours <= 0:
            continue
        uptime += hours
        ocpu += hours * float(item.get("ocpus") or 0)
        gb += hours * float(item.get("memory_gb") or 0)
    return uptime, ocpu, gb


def day_bounds(d: date) -> tuple[datetime, datetime]:
    start = datetime(d.year, d.month, d.day, tzinfo=timezone.utc)
    nd = date.fromordinal(d.toordinal() + 1)
    end = datetime(nd.year, nd.month, nd.day, tzinfo=timezone.utc)
    return start, end


def month_bounds(year: int, month: int) -> tuple[datetime, datetime]:
    start = datetime(year, month, 1, tzinfo=timezone.utc)
    if month == 12:
        end = datetime(year + 1, 1, 1, tzinfo=timezone.utc)
    else:
        end = datetime(year, month + 1, 1, tzinfo=timezone.utc)
    return start, end


def day_totals(ledger: dict[str, Any], d: date, now: datetime | None = None) -> tuple[float, float, float]:
    now = now or utc_now()
    key = d.isoformat()
    overrides = ledger.get("daily_overrides") or {}
    if key in overrides:
        ov = overrides[key] or {}
        return (
            float(ov.get("uptime_hours") or 0),
            float(ov.get("ocpu_hours") or 0),
            float(ov.get("gb_hours") or 0),
        )
    ds, de = day_bounds(d)
    return totals(ledger, ds, de, now)


def budget_snapshot(ledger: dict[str, Any], cfg: dict[str, Any]) -> dict[str, Any]:
    now = utc_now()
    year, month = now.year, now.month
    dim = calendar.monthrange(year, month)[1]
    daily_ocpu = float(cfg["monthly_ocpu_target"]) / dim
    daily_gb = float(cfg["monthly_gb_target"]) / dim

    month_ocpu = month_gb = month_uptime = 0.0
    leftover_ocpu = leftover_gb = 0.0
    today_ocpu = today_gb = 0.0
    for day_num in range(1, now.day + 1):
        d = date(year, month, day_num)
        u, o, g = day_totals(ledger, d, now)
        month_uptime += u
        month_ocpu += o
        month_gb += g
        if day_num < now.day:
            leftover_ocpu += max(0.0, daily_ocpu - o)
            leftover_gb += max(0.0, daily_gb - g)
        else:
            today_ocpu, today_gb = o, g

    return {
        "daily_ocpu": daily_ocpu,
        "daily_gb": daily_gb,
        "today_ocpu": today_ocpu,
        "today_gb": today_gb,
        "month_ocpu": month_ocpu,
        "month_gb": month_gb,
        "leftover_ocpu": leftover_ocpu,
        "leftover_gb": leftover_gb,
        "soft_ocpu_cap": float(cfg["soft_ocpu_cap"]),
        "soft_gb_cap": float(cfg["soft_gb_cap"]),
        "over_daily_ocpu": today_ocpu > daily_ocpu,
        "over_daily_gb": today_gb > daily_gb,
        "hit_soft_cap": month_ocpu >= float(cfg["soft_ocpu_cap"])
        or month_gb >= float(cfg["soft_gb_cap"]),
    }
