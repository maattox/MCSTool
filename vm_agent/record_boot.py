#!/usr/bin/env python3
"""Record a START interval when Minecraft boots (systemd oneshot).

Phase 5: force-pull OS ledger + lease → merge → close prior opens from
lease/list-boots → repair uncertain → fill missing boots → detect live shape
→ open interval + lease → publish; sync shape to local config + OS budget.

Also force-enables the idle agent (timer + local/OS config) so a forgotten
Danger Zone disable cannot leave free-tier SoftStop brakes off after reboot.
"""

from __future__ import annotations

import json
import os
import subprocess
import sys

LIB = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib")
if LIB not in sys.path:
    sys.path.insert(0, LIB)

import lease as lease_mod  # noqa: E402
import ledger as ledger_mod  # noqa: E402
import os_publish as os_publish_mod  # noqa: E402
import shape_detect as shape_mod  # noqa: E402

CONFIG_PATH = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")
# Skip only if oneshot re-fires shortly after a real boot record.
RECENT_OPEN_SKIP_SEC = 180


def _publish(cfg: dict, data: dict, lease: dict | None = None) -> None:
    try:
        if lease is not None:
            print(os_publish_mod.publish_ledger_and_lease(cfg, data, lease))
        else:
            print(os_publish_mod.publish_ledger(cfg, data))
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage publish warning: {exc}", file=sys.stderr)


def _sync_shape(cfg: dict, ocpus: float, memory_gb: float) -> None:
    cfg["shape_ocpus"] = float(ocpus)
    cfg["shape_memory_gb"] = float(memory_gb)
    changed, msg = shape_mod.apply_shape_to_local_config(CONFIG_PATH, ocpus, memory_gb)
    print(msg)
    try:
        print(os_publish_mod.sync_shape_to_budget(cfg, ocpus, memory_gb))
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage budget shape sync warning: {exc}", file=sys.stderr)
    if changed:
        print(f"Live shape applied: {ocpus} OCPU / {memory_gb} GB.")


def force_enable_idle_agent(cfg: dict) -> dict:
    """Start idle timer and rewrite local + OS config to idle_agent_enabled=true.

    Disabling idle is testing-only; every Minecraft boot re-arms SoftStop brakes.
    """
    for cmd in (
        ["systemctl", "enable", "mc-idle-watch.timer"],
        ["systemctl", "start", "mc-idle-watch.timer"],
    ):
        proc = subprocess.run(cmd, check=False, capture_output=True, text=True)
        if proc.returncode != 0:
            print(
                f"{' '.join(cmd)} rc={proc.returncode} "
                f"stderr={proc.stderr.strip()!r}",
                file=sys.stderr,
            )

    if cfg.get("idle_agent_enabled", True):
        local_msg = "Local idle_agent_enabled already true."
    else:
        cfg["idle_agent_enabled"] = True
        try:
            with open(CONFIG_PATH, "r", encoding="utf-8") as f:
                on_disk = json.load(f)
            if not isinstance(on_disk, dict):
                on_disk = {}
            on_disk["idle_agent_enabled"] = True
            tmp = CONFIG_PATH + ".tmp"
            with open(tmp, "w", encoding="utf-8") as f:
                json.dump(on_disk, f, indent=2)
                f.write("\n")
            os.replace(tmp, CONFIG_PATH)
            local_msg = f"Rewrote {CONFIG_PATH} idle_agent_enabled=true."
        except OSError as exc:
            local_msg = f"Local idle_agent_enabled rewrite failed: {exc}"
    print(local_msg)

    try:
        print(os_publish_mod.sync_idle_agent_enabled_to_budget(cfg, enabled=True))
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage idle_agent_enabled sync warning: {exc}", file=sys.stderr)
    return cfg


def clear_idle_state(cfg: dict) -> None:
    """Fresh Minecraft boot must not inherit a pre-shutdown idle countdown."""
    state_path = cfg.get("state_path", "/var/lib/mc-manager/idle_state.json")
    ledger_path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    os.makedirs(os.path.dirname(state_path), exist_ok=True)
    st: dict = {}
    if os.path.exists(state_path):
        try:
            with open(state_path, encoding="utf-8") as f:
                st = json.load(f) or {}
        except (OSError, json.JSONDecodeError):
            st = {}
    st["idle_since"] = None
    st.pop("idle_warned", None)
    with open(state_path, "w", encoding="utf-8") as f:
        json.dump(st, f, indent=2)
        f.write("\n")
    data = ledger_mod.load_ledger(ledger_path)
    data["idle_since"] = None
    ledger_mod.save_ledger(ledger_path, data)


def main() -> int:
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        cfg = json.load(f)
    cfg = force_enable_idle_agent(cfg)
    path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    lpath = os_publish_mod.lease_path(cfg)
    fallback_o, fallback_m = shape_mod.shape_from_cfg(cfg)
    ocpus, memory_gb, shape_src = shape_mod.detect_shape(
        fallback_ocpus=fallback_o,
        fallback_memory_gb=fallback_m,
    )
    print(f"Detected shape via {shape_src}: {ocpus} OCPU / {memory_gb} GB")
    _sync_shape(cfg, ocpus, memory_gb)

    # Snapshot on-disk ledger BEFORE clear/pull so a local idle_or_budget_stop
    # survives an Object Storage pull that still has an open / door-approximate row.
    local_before = ledger_mod.load_ledger(path)
    clear_idle_state(cfg)

    try:
        data, pull_msg = os_publish_mod.pull_ledger_for_boot(cfg)
        print(pull_msg)
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage pull warning: {exc}", file=sys.stderr)
        data = ledger_mod.load_ledger(path)
    if not isinstance(data, dict):
        data = ledger_mod.load_ledger(path)

    try:
        lease, lease_msg = os_publish_mod.pull_lease(cfg)
        print(lease_msg)
    except Exception as exc:  # noqa: BLE001
        print(f"Object Storage lease pull warning: {exc}", file=sys.stderr)
        lease = lease_mod.load_lease(lpath)

    if isinstance(local_before, dict) and (local_before.get("intervals") or []):
        before_n = len(data.get("intervals") or [])
        data = ledger_mod.merge_ledgers_for_boot(data, local_before)
        after_n = len(data.get("intervals") or [])
        if after_n != before_n:
            print(
                f"Merged local ledger into OS pull "
                f"({before_n} → {after_n} intervals)."
            )

    try:
        closed_n = ledger_mod.close_opens_from_evidence(data, lease=lease)
        if closed_n:
            print(f"Closed {closed_n} prior open interval(s) from lease/list-boots.")
    except Exception as exc:  # noqa: BLE001
        print(f"list-boots close warning: {exc}", file=sys.stderr)

    ledger_mod.normalize_open_intervals(data)

    try:
        repaired = ledger_mod.repair_uncertain_stops(data, lease=lease)
        if repaired:
            print(f"Repaired {repaired} stop_uncertain interval(s) after OS pull/boot.")
    except Exception as exc:  # noqa: BLE001
        print(f"repair uncertain warning: {exc}", file=sys.stderr)
        repaired = 0

    try:
        filled = ledger_mod.fill_missing_boot_intervals(
            data, ocpus=ocpus, memory_gb=memory_gb
        )
        if filled:
            print(f"Filled {filled} missing boot interval(s) from list-boots.")
    except Exception as exc:  # noqa: BLE001
        print(f"fill missing boots warning: {exc}", file=sys.stderr)

    # Only skip duplicate oneshot re-runs; never keep a stale open forever.
    opens = [
        item
        for item in (data.get("intervals") or [])
        if isinstance(item, dict) and not item.get("stopped_at")
    ]
    if len(opens) == 1:
        started = ledger_mod.parse_iso(opens[0].get("started_at"))
        if started is not None:
            age = (ledger_mod.utc_now() - started).total_seconds()
            if 0 <= age < RECENT_OPEN_SKIP_SEC:
                open_o = float(opens[0].get("ocpus") or 0)
                open_m = float(opens[0].get("memory_gb") or 0)
                if shape_mod.shapes_differ(open_o, open_m, ocpus, memory_gb):
                    print(
                        f"Recent open interval has shape {open_o}/{open_m}; "
                        f"live shape is {ocpus}/{memory_gb} — closing and reopening."
                    )
                    ledger_mod.record_stop(data, source="boot_shape_correct")
                    # Fall through to record_start with live shape.
                else:
                    lease = lease_mod.open_lease(
                        interval_id=str(opens[0].get("id") or ""),
                        started_at=str(opens[0].get("started_at")),
                        ocpus=open_o or ocpus,
                        memory_gb=open_m or memory_gb,
                    )
                    ledger_mod.save_ledger(path, data)
                    lease_mod.save_lease(lpath, lease)
                    print(
                        f"Recent open interval ({age:.0f}s old); "
                        "skipping duplicate boot record; refreshing lease."
                    )
                    _publish(cfg, data, lease)
                    return 0

    interval = ledger_mod.record_start(
        data,
        ocpus=ocpus,
        memory_gb=memory_gb,
        source="boot",
    )
    data["idle_since"] = None
    lease = lease_mod.open_lease(
        interval_id=str(interval.get("id") or ""),
        started_at=str(interval.get("started_at")),
        ocpus=ocpus,
        memory_gb=memory_gb,
    )
    ledger_mod.save_ledger(path, data)
    lease_mod.save_lease(lpath, lease)
    print("Recorded boot start interval + active lease; idle timer cleared.")
    _publish(cfg, data, lease)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
