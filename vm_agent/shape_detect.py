"""Detect live VM1 shape (OCPUs + memory) from the guest OS.

Used so ledger intervals match the instance after Console / Manager resize,
instead of trusting a stale ``shape_ocpus`` / ``shape_memory_gb`` in config.
"""

from __future__ import annotations

import json
import os
from typing import Any


def detect_ocpus() -> float:
    """Online CPU count (matches Ampere A1 Flex OCPU count on Ubuntu)."""
    n = os.cpu_count()
    if n is not None and n > 0:
        return float(n)
    # Fallback: count processor entries in /proc/cpuinfo
    try:
        with open("/proc/cpuinfo", "r", encoding="utf-8") as f:
            cpus = sum(1 for line in f if line.startswith("processor"))
        if cpus > 0:
            return float(cpus)
    except OSError:
        pass
    return 0.0


def detect_memory_gb() -> float:
    """Advertised-ish memory in GB from MemTotal.

    Linux MemTotal is slightly under the OCI shape size. Apply a small upward
    bias then round to the nearest 0.5 GB so 2/12 and 4/24 land correctly.
    """
    kib = 0
    try:
        with open("/proc/meminfo", "r", encoding="utf-8") as f:
            for line in f:
                if line.startswith("MemTotal:"):
                    kib = int(line.split()[1])
                    break
    except (OSError, ValueError, IndexError):
        return 0.0
    if kib <= 0:
        return 0.0
    gib = kib / 1024.0 / 1024.0
    # Bias: e.g. 23.4 GiB observed → ~23.9 → rounds to 24.0
    adjusted = gib * 1.025
    half_steps = round(adjusted * 2.0) / 2.0
    return float(max(0.5, half_steps))


def detect_shape(
    *,
    fallback_ocpus: float = 4.0,
    fallback_memory_gb: float = 24.0,
) -> tuple[float, float, str]:
    """Return ``(ocpus, memory_gb, source)``.

    ``source`` is ``proc`` when both probes succeed, else ``config_fallback``
    (or mixed note) when one/both fall back.
    """
    ocpus = detect_ocpus()
    memory_gb = detect_memory_gb()
    used_fallback = False
    if ocpus <= 0:
        ocpus = float(fallback_ocpus)
        used_fallback = True
    if memory_gb <= 0:
        memory_gb = float(fallback_memory_gb)
        used_fallback = True
    source = "config_fallback" if used_fallback else "proc"
    return float(ocpus), float(memory_gb), source


def shapes_differ(
    a_ocpus: float,
    a_mem: float,
    b_ocpus: float,
    b_mem: float,
    *,
    ocpu_eps: float = 0.01,
    mem_eps: float = 0.25,
) -> bool:
    return abs(float(a_ocpus) - float(b_ocpus)) > ocpu_eps or abs(
        float(a_mem) - float(b_mem)
    ) > mem_eps


def apply_shape_to_local_config(
    config_path: str,
    ocpus: float,
    memory_gb: float,
) -> tuple[bool, str]:
    """Update ``shape_*`` in agent config on disk. Returns (changed, message)."""
    try:
        with open(config_path, "r", encoding="utf-8") as f:
            cfg = json.load(f)
    except (OSError, json.JSONDecodeError) as exc:
        return False, f"local config read failed: {exc}"
    if not isinstance(cfg, dict):
        return False, "local config is not a JSON object"

    prev_o = float(cfg.get("shape_ocpus") or 0)
    prev_m = float(cfg.get("shape_memory_gb") or 0)
    if not shapes_differ(prev_o, prev_m, ocpus, memory_gb):
        return False, (
            f"local config already shape_ocpus={ocpus} shape_memory_gb={memory_gb}"
        )

    cfg["shape_ocpus"] = float(ocpus)
    cfg["shape_memory_gb"] = float(memory_gb)
    tmp = config_path + ".tmp"
    try:
        with open(tmp, "w", encoding="utf-8") as f:
            json.dump(cfg, f, indent=2)
            f.write("\n")
        os.replace(tmp, config_path)
    except OSError as exc:
        try:
            if os.path.exists(tmp):
                os.remove(tmp)
        except OSError:
            pass
        return False, f"local config write failed: {exc}"
    return True, (
        f"updated local config shape {prev_o}/{prev_m} → {ocpus}/{memory_gb}"
    )


def shape_from_cfg(cfg: dict[str, Any]) -> tuple[float, float]:
    return (
        float(cfg.get("shape_ocpus", 4) or 4),
        float(cfg.get("shape_memory_gb", 24) or 24),
    )
