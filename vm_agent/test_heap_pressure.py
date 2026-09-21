"""Heap-pressure log classify + flag helpers — no OCI. python vm_agent/test_heap_pressure.py"""

from __future__ import annotations

import os
import sys
import unittest
from datetime import datetime, timedelta, timezone

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import heap_pressure as hp  # noqa: E402


class ClassifyTests(unittest.TestCase):
    def test_oom_beats_occupancy(self) -> None:
        text = "Occupancy: 95%\njava.lang.OutOfMemoryError: Java heap space\n"
        self.assertEqual(hp.REASON_OOM, hp.classify_log_text(text))

    def test_gc_overhead(self) -> None:
        self.assertEqual(
            hp.REASON_GC_OVERHEAD,
            hp.classify_log_text("java.lang.OutOfMemoryError: GC overhead limit exceeded"),
        )

    def test_repeated_full_gc(self) -> None:
        text = "\n".join(
            [
                "[0.1s][info][gc] Pause Full (System.gc()) 10M->8M",
                "[0.2s][info][gc] Pause Full (G1 Evacuation Pause)",
                "[0.3s][info][gc] Full GC (Allocation Failure)",
            ]
        )
        self.assertEqual(hp.REASON_REPEATED_FULL_GC, hp.classify_log_text(text))

    def test_occupancy_percent_is_not_pressure(self) -> None:
        text = "\n".join(
            [
                "G1 Heap: 8192M",
                "Occupancy: 97%",
                "[gc] Eden: 100M->0M",
            ]
        )
        self.assertIsNone(hp.classify_log_text(text))

    def test_one_full_gc_is_not_enough(self) -> None:
        self.assertIsNone(hp.classify_log_text("[gc] Pause Full (Metadata GC Threshold)"))


class NextLargerTests(unittest.TestCase):
    def test_skips_illegal_on_12gb(self) -> None:
        self.assertEqual("6G", hp.next_larger("4G", 24))
        self.assertEqual("10G", hp.next_larger("8G", 24))
        self.assertIsNone(hp.next_larger("8G", 12))
        self.assertIsNone(hp.next_larger("12G", 24))


class TickTests(unittest.TestCase):
    def test_put_on_oom_then_rate_limit_then_clear(self) -> None:
        store: dict[str, dict] = {}
        now = datetime(2026, 9, 21, 12, 0, tzinfo=timezone.utc)
        cfg = {
            "object_storage_namespace": "ns",
            "object_storage_bucket": "bucket",
        }

        def getter():
            return store.get(hp.OBJ_HEAP_PRESSURE)

        def putter(doc):
            store[hp.OBJ_HEAP_PRESSURE] = dict(doc)

        def deleter():
            store.pop(hp.OBJ_HEAP_PRESSURE, None)

        msg = hp.tick(
            cfg,
            game_up=True,
            now=now,
            host_memory_gb=24,
            current_heap="8G",
            log_text="java.lang.OutOfMemoryError: Java heap space",
            get_flag=getter,
            put_flag=putter,
            delete_flag=deleter,
        )
        self.assertIn("Wrote", msg)
        doc = store[hp.OBJ_HEAP_PRESSURE]
        self.assertEqual("pressure", doc["status"])
        self.assertEqual("8G", doc["current_heap"])
        self.assertEqual("10G", doc["suggested_heap"])
        self.assertEqual("oom", doc["reason"])
        self.assertFalse(doc["at_host_max"])

        skip = hp.tick(
            cfg,
            game_up=True,
            now=now + timedelta(minutes=5),
            host_memory_gb=24,
            current_heap="8G",
            log_text="java.lang.OutOfMemoryError: Java heap space",
            get_flag=getter,
            put_flag=putter,
            delete_flag=deleter,
        )
        self.assertIn("rate-limit", skip)
        self.assertEqual("2026-09-21T12:00:00Z", store[hp.OBJ_HEAP_PRESSURE]["updated_at"])

        cleared = hp.tick(
            cfg,
            game_up=True,
            now=now + timedelta(hours=1),
            host_memory_gb=24,
            current_heap="8G",
            log_text="[INFO] Starting minecraft server",
            get_flag=getter,
            put_flag=putter,
            delete_flag=deleter,
        )
        self.assertIn("Cleared", cleared)
        self.assertNotIn(hp.OBJ_HEAP_PRESSURE, store)


if __name__ == "__main__":
    unittest.main()
