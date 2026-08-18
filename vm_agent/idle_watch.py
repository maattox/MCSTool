#!/usr/bin/env python3
"""Idle / budget watchdog — run once per minute via systemd timer."""

from __future__ import annotations

import json
import os
import subprocess
import sys
import time
from datetime import datetime, timezone

LIB = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib")
if LIB not in sys.path:
    sys.path.insert(0, LIB)

import lease as lease_mod  # noqa: E402
import ledger as ledger_mod  # noqa: E402
import os_publish as os_publish_mod  # noqa: E402
import shape_detect as shape_mod  # noqa: E402
import world_backup as world_backup_mod  # noqa: E402
from rcon_client import RconClient, RconError, parse_list_online_count  # noqa: E402

CONFIG_PATH = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")

DEFAULT_MESSAGES = {
    "budget_warn_leftover": (
        "Daily usage limit exceeded; using leftover hours "
        "(~{ocpu:.1f} OCPU-h / ~{gb:.1f} GB-h left)."
    ),
    "budget_final_warn": (
        "Daily + leftover usage exhausted. Server will shut down soon."
    ),
    "budget_stop": "Usage limits reached. Server shutting down.",
    "soft_cap_stop": "Monthly usage soft cap reached. Server shutting down.",
    "idle_stop": "No players for {minutes} minutes. Saving and shutting down.",
    "idle_stop_inactive": (
        "Minecraft not running for {minutes} minutes. Saving and shutting down."
    ),
    "admin_stop": "Admin requested shutdown. Saving world…",
}


def load_config() -> dict:
    with open(CONFIG_PATH, "r", encoding="utf-8") as f:
        return json.load(f)


def msg(cfg: dict, key: str, **kwargs: object) -> str:
    messages = dict(DEFAULT_MESSAGES)
    stored = cfg.get("messages") or {}
    if isinstance(stored, dict):
        messages.update({k: str(v) for k, v in stored.items() if v is not None})
    template = messages.get(key) or DEFAULT_MESSAGES.get(key) or key
    try:
        return template.format(**kwargs)
    except (KeyError, ValueError, IndexError):
        return template


def load_state(path: str) -> dict:
    if not os.path.exists(path):
        return {}
    with open(path, "r", encoding="utf-8") as f:
        return json.load(f)


def save_state(path: str, data: dict) -> None:
    os.makedirs(os.path.dirname(path), exist_ok=True)
    tmp = path + ".tmp"
    with open(tmp, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=2)
        f.write("\n")
    os.replace(tmp, path)


def minecraft_active(unit: str) -> bool:
    r = subprocess.run(
        ["systemctl", "is-active", unit],
        capture_output=True,
        text=True,
        check=False,
    )
    return r.returncode == 0 and r.stdout.strip() == "active"


def oci_stop_instance(instance_id: str) -> None:
    import oci

    signer = oci.auth.signers.InstancePrincipalsSecurityTokenSigner()
    client = oci.core.ComputeClient(config={}, signer=signer)
    client.instance_action(instance_id, "SOFTSTOP")


def rcon_cmd(cfg: dict, cmd: str) -> str:
    with RconClient(
        cfg.get("rcon_host", "127.0.0.1"),
        int(cfg.get("rcon_port", 25575)),
        cfg.get("rcon_password", ""),
    ) as r:
        return r.command(cmd)


def clear_idle_tracking(cfg: dict, *, state_path: str | None = None) -> None:
    """Clear idle countdown so it cannot survive across stop/start."""
    ledger_path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    path = state_path or cfg.get("state_path", "/var/lib/mc-manager/idle_state.json")
    st = load_state(path)
    st["idle_since"] = None
    st.pop("idle_warned", None)
    save_state(path, st)
    data = ledger_mod.load_ledger(ledger_path)
    data["idle_since"] = None
    ledger_mod.save_ledger(ledger_path, data)


def _publish_ledger_with_retries(cfg: dict, data: dict, *, attempts: int = 3) -> bool:
    """Best-effort OS publish before SoftStop. Returns True on success."""
    last_exc: Exception | None = None
    for i in range(1, attempts + 1):
        try:
            print(os_publish_mod.publish_ledger(cfg, data))
            return True
        except Exception as exc:  # noqa: BLE001
            last_exc = exc
            print(
                f"Object Storage publish attempt {i}/{attempts} failed: {exc}",
                file=sys.stderr,
            )
            time.sleep(min(5 * i, 15))
    print(
        f"Object Storage publish failed after {attempts} attempts "
        f"(door heal must close the interval): {last_exc}",
        file=sys.stderr,
    )
    return False


def _publish_stop_state(
    cfg: dict, data: dict, lease: dict, *, attempts: int = 3
) -> None:
    """Publish closed ledger + cleared lease before SoftStop."""
    ok = _publish_ledger_with_retries(cfg, data, attempts=attempts)
    for i in range(1, attempts + 1):
        try:
            print(os_publish_mod.publish_lease(cfg, lease))
            return
        except Exception as exc:  # noqa: BLE001
            print(
                f"Lease publish attempt {i}/{attempts} failed: {exc}",
                file=sys.stderr,
            )
            time.sleep(min(5 * i, 15))
    if ok:
        print("Ledger published but lease clear failed; door heal uses STOPPED+lease.", file=sys.stderr)


def maybe_reshape_session(cfg: dict, data: dict) -> dict:
    """If live shape differs from the open interval, close and reopen (simple).

    OCI A1 Flex resize normally requires STOPPED; this is a safety net if
    online CPUs/memory ever change under a running guest.
    """
    ledger_path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    lpath = os_publish_mod.lease_path(cfg)
    open_item = None
    for item in reversed(data.get("intervals") or []):
        if isinstance(item, dict) and not item.get("stopped_at"):
            open_item = item
            break
    if open_item is None:
        return data

    fallback_o, fallback_m = shape_mod.shape_from_cfg(cfg)
    ocpus, memory_gb, src = shape_mod.detect_shape(
        fallback_ocpus=fallback_o,
        fallback_memory_gb=fallback_m,
    )
    open_o = float(open_item.get("ocpus") or 0)
    open_m = float(open_item.get("memory_gb") or 0)
    if not shape_mod.shapes_differ(open_o, open_m, ocpus, memory_gb):
        return data

    print(
        f"Shape change detected via {src}: interval {open_o}/{open_m} → "
        f"live {ocpus}/{memory_gb}; splitting usage interval."
    )
    ledger_mod.record_stop(data, source="shape_change")
    interval = ledger_mod.record_start(
        data,
        ocpus=ocpus,
        memory_gb=memory_gb,
        source="shape_change",
    )
    lease = lease_mod.open_lease(
        interval_id=str(interval.get("id") or ""),
        started_at=str(interval.get("started_at")),
        ocpus=ocpus,
        memory_gb=memory_gb,
    )
    ledger_mod.save_ledger(ledger_path, data)
    lease_mod.save_lease(lpath, lease)
    cfg["shape_ocpus"] = ocpus
    cfg["shape_memory_gb"] = memory_gb
    changed, msg = shape_mod.apply_shape_to_local_config(CONFIG_PATH, ocpus, memory_gb)
    print(msg)
    try:
        print(os_publish_mod.publish_ledger_and_lease(cfg, data, lease))
    except Exception as exc:  # noqa: BLE001
        print(f"Shape-change publish warning: {exc}", file=sys.stderr)
    try:
        print(os_publish_mod.sync_shape_to_budget(cfg, ocpus, memory_gb))
    except Exception as exc:  # noqa: BLE001
        print(f"Shape-change budget sync warning: {exc}", file=sys.stderr)
    if changed:
        print(f"Local config synced to {ocpus} OCPU / {memory_gb} GB.")
    return data


def maybe_heartbeat(cfg: dict, data: dict) -> None:
    """Refresh lease heartbeat periodically while Minecraft is active."""
    data = maybe_reshape_session(cfg, data)
    lpath = os_publish_mod.lease_path(cfg)
    lease = lease_mod.load_lease(lpath)
    hb_min = max(1, int(cfg.get("lease_heartbeat_minutes", 5)))
    now = datetime.now(timezone.utc)

    # Ensure an active lease exists for the current open interval.
    open_item = None
    for item in reversed(data.get("intervals") or []):
        if isinstance(item, dict) and not item.get("stopped_at"):
            open_item = item
            break
    if open_item is None:
        return

    need_open = not lease.get("active") or str(lease.get("interval_id") or "") != str(
        open_item.get("id") or ""
    )
    if need_open:
        lease = lease_mod.open_lease(
            interval_id=str(open_item.get("id") or ""),
            started_at=str(open_item.get("started_at")),
            ocpus=float(open_item.get("ocpus") or cfg.get("shape_ocpus", 4)),
            memory_gb=float(open_item.get("memory_gb") or cfg.get("shape_memory_gb", 24)),
        )
        lease_mod.save_lease(lpath, lease)
        try:
            print(os_publish_mod.publish_lease(cfg, lease))
        except Exception as exc:  # noqa: BLE001
            print(f"Lease open publish warning: {exc}", file=sys.stderr)
        return

    age = lease_mod.age_seconds(lease, now)
    if age is not None and age < hb_min * 60:
        return

    lease_mod.touch_heartbeat(lease)
    lease_mod.save_lease(lpath, lease)
    try:
        print(os_publish_mod.publish_lease(cfg, lease))
    except Exception as exc:  # noqa: BLE001
        print(f"Lease heartbeat publish warning: {exc}", file=sys.stderr)


def graceful_stop_and_poweroff(
    cfg: dict, reason: str, *, game_was_up: bool | None = None
) -> None:
    unit = cfg.get("minecraft_unit", "minecraft")
    if game_was_up is None:
        game_was_up = minecraft_active(unit)
    ledger_path = cfg["ledger_path"]
    state_path = cfg.get("state_path", "/var/lib/mc-manager/idle_state.json")
    lpath = os_publish_mod.lease_path(cfg)
    # Reset idle timer before power-off so the next boot does not instantly re-stop.
    clear_idle_tracking(cfg, state_path=state_path)
    data = ledger_mod.load_ledger(ledger_path)
    if game_was_up:
        try:
            rcon_cmd(cfg, f"say {reason}")
            rcon_cmd(cfg, "save-all flush")
        except Exception as exc:  # noqa: BLE001
            print(f"RCON during stop: {exc}", file=sys.stderr)
        time.sleep(15)
        # Bound systemctl so a broken D-Bus cannot block forever before ledger close.
        stop = subprocess.run(
            ["timeout", "120", "systemctl", "stop", unit],
            check=False,
            capture_output=True,
            text=True,
        )
        if stop.returncode != 0:
            print(
                f"systemctl stop {unit} rc={stop.returncode} "
                f"(stdout={stop.stdout.strip()!r} stderr={stop.stderr.strip()!r}); "
                "continuing with world backup + ledger close + SoftStop",
                file=sys.stderr,
            )
    else:
        print("Minecraft already inactive; skipping RCON and systemctl stop.")
    # World → Object Storage while VM is still up (cold: Minecraft already stopped).
    try:
        print(world_backup_mod.backup_world_to_object_storage(cfg, mode="cold"))
    except Exception as exc:  # noqa: BLE001
        print(f"World backup warning (continuing SoftStop): {exc}", file=sys.stderr)
    # Close + save locally BEFORE SoftStop so a hung ACPI poweroff still leaves
    # an on-disk stop time for the next boot merge/repair.
    ledger_mod.record_stop(data, source="idle_or_budget_stop")
    data["idle_since"] = None
    ledger_mod.save_ledger(ledger_path, data)
    prev_lease = lease_mod.load_lease(lpath)
    lease = lease_mod.clear_lease(prev_lease, reason="idle_or_budget_stop")
    lease_mod.save_lease(lpath, lease)
    _publish_stop_state(cfg, data, lease)
    oci_stop_instance(cfg["instance_id"])
    print(f"Stopped instance after: {reason}")


def main() -> int:
    cfg = load_config()
    if not cfg.get("idle_agent_enabled", True):
        print("Idle agent disabled in config.")
        return 0

    unit = cfg.get("minecraft_unit", "minecraft")
    game_up = minecraft_active(unit)

    ledger_path = cfg.get("ledger_path", "/var/lib/mc-manager/usage.json")
    state_path = cfg.get("state_path", "/var/lib/mc-manager/idle_state.json")
    data = ledger_mod.load_ledger(ledger_path)
    snap = ledger_mod.budget_snapshot(data, cfg)
    st = load_state(state_path)

    if snap["hit_soft_cap"]:
        graceful_stop_and_poweroff(
            cfg, msg(cfg, "soft_cap_stop"), game_was_up=game_up
        )
        return 0

    today_over_ocpu = max(0.0, snap["today_ocpu"] - snap["daily_ocpu"])
    today_over_gb = max(0.0, snap["today_gb"] - snap["daily_gb"])
    remaining_ocpu = snap["leftover_ocpu"] - today_over_ocpu
    remaining_gb = snap["leftover_gb"] - today_over_gb
    over_daily = snap["over_daily_ocpu"] or snap["over_daily_gb"]
    has_leftover = remaining_ocpu > 0.01 or remaining_gb > 0.01

    if over_daily and has_leftover:
        st.pop("budget_final_warned", None)
        st.pop("budget_final_warn_at", None)
        last_warn = st.get("last_budget_warn_at")
        warn_every = max(1, int(cfg.get("budget_warn_minutes", 5))) * 60
        now = datetime.now(timezone.utc)
        should_warn = True
        if last_warn:
            try:
                prev = ledger_mod.parse_iso(last_warn)
                if prev and (now - prev).total_seconds() < warn_every:
                    should_warn = False
            except Exception:  # noqa: BLE001
                pass
        if should_warn:
            text = msg(cfg, "budget_warn_leftover", ocpu=remaining_ocpu, gb=remaining_gb)
            if game_up:
                try:
                    rcon_cmd(cfg, f"say {text}")
                except Exception as exc:  # noqa: BLE001
                    print(f"Budget warn RCON failed: {exc}", file=sys.stderr)
            else:
                print(f"Minecraft not active; skipping in-game leftover warn: {text}")
            st["last_budget_warn_at"] = ledger_mod.to_iso(now)
            save_state(state_path, st)
    elif over_daily and not has_leftover:
        if not st.get("budget_final_warned"):
            if game_up:
                try:
                    rcon_cmd(cfg, "say " + msg(cfg, "budget_final_warn"))
                except Exception as exc:  # noqa: BLE001
                    print(f"Final budget warn failed: {exc}", file=sys.stderr)
            else:
                print("Minecraft not active; skipping in-game budget final warn.")
            st["budget_final_warned"] = True
            st["budget_final_warn_at"] = ledger_mod.to_iso(datetime.now(timezone.utc))
            save_state(state_path, st)
            maybe_heartbeat(cfg, data)
            return 0
        warned_at = ledger_mod.parse_iso(st.get("budget_final_warn_at"))
        wait_sec = max(1, int(cfg.get("budget_warn_minutes", 5))) * 60
        if warned_at and (datetime.now(timezone.utc) - warned_at).total_seconds() >= wait_sec:
            graceful_stop_and_poweroff(
                cfg, msg(cfg, "budget_stop"), game_was_up=game_up
            )
            return 0
    else:
        st.pop("budget_final_warned", None)
        st.pop("budget_final_warn_at", None)
        save_state(state_path, st)

    if game_up:
        try:
            listing = rcon_cmd(cfg, "list")
            online = parse_list_online_count(listing)
        except (RconError, OSError) as exc:
            print(f"RCON list failed: {exc}", file=sys.stderr)
            maybe_heartbeat(cfg, data)
            return 0
    else:
        online = 0

    now = datetime.now(timezone.utc)
    if online > 0:
        st["idle_since"] = None
        st.pop("idle_warned", None)
        save_state(state_path, st)
        data["idle_since"] = None
        ledger_mod.save_ledger(ledger_path, data)
        maybe_heartbeat(cfg, data)
        print(f"Players online: {online}")
        return 0

    idle_since = st.get("idle_since")
    if not idle_since:
        st["idle_since"] = ledger_mod.to_iso(now)
        save_state(state_path, st)
        data["idle_since"] = st["idle_since"]
        ledger_mod.save_ledger(ledger_path, data)
        maybe_heartbeat(cfg, data)
        if game_up:
            print("No players; idle timer started.")
        else:
            print("Minecraft not active; idle timer started.")
        return 0

    since = ledger_mod.parse_iso(idle_since) or now
    # Discard idle_since from a previous VM/Minecraft session.
    session_start = None
    for item in reversed(data.get("intervals") or []):
        if not item.get("stopped_at"):
            session_start = ledger_mod.parse_iso(item.get("started_at"))
            break
    if session_start and since < session_start:
        st["idle_since"] = ledger_mod.to_iso(now)
        st.pop("idle_warned", None)
        save_state(state_path, st)
        data["idle_since"] = st["idle_since"]
        ledger_mod.save_ledger(ledger_path, data)
        maybe_heartbeat(cfg, data)
        print("Stale idle_since from prior session; resetting idle timer.")
        return 0

    idle_minutes = (now - since).total_seconds() / 60.0
    timeout = float(cfg.get("idle_timeout_minutes", 15))
    if game_up:
        print(f"Idle for {idle_minutes:.1f} / {timeout} minutes")
    else:
        print(
            f"Minecraft not active; idle for {idle_minutes:.1f} / {timeout} minutes"
        )
    maybe_heartbeat(cfg, data)
    if idle_minutes < timeout:
        return 0

    stop_key = "idle_stop" if game_up else "idle_stop_inactive"
    graceful_stop_and_poweroff(
        cfg, msg(cfg, stop_key, minutes=int(timeout)), game_was_up=game_up
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
