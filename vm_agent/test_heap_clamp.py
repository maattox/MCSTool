"""Heap clamp helpers — no OCI. Run from repo: python vm_agent/test_heap_clamp.py"""

from __future__ import annotations

import os
import stat
import sys
import tempfile
import unittest

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import heap_clamp as hc  # noqa: E402


class HeapClampMathTests(unittest.TestCase):
    def test_host_cap_matches_product_sizes(self) -> None:
        self.assertEqual("8G", hc.max_for_host_memory_gb(12))
        self.assertEqual("12G", hc.max_for_host_memory_gb(24))
        self.assertEqual(12, hc.product_host_memory_gb(11.5))
        self.assertEqual(12, hc.product_host_memory_gb(12))
        self.assertEqual(24, hc.product_host_memory_gb(24))
        self.assertEqual(24, hc.product_host_memory_gb(23.4))

    def test_clamp_10g_12g_on_12gb_host(self) -> None:
        self.assertEqual("8G", hc.clamp_to_host("12G", 12))
        self.assertEqual("8G", hc.clamp_to_host("10G", 12))
        self.assertEqual("8G", hc.clamp_to_host("8G", 12))
        self.assertEqual("6G", hc.clamp_to_host("6G", 12))
        self.assertEqual("8G", hc.capped_token_if_oversized("10G", 12))
        self.assertEqual("8G", hc.capped_token_if_oversized("12G", 11.6))
        self.assertIsNone(hc.capped_token_if_oversized("8G", 12))
        self.assertIsNone(hc.capped_token_if_oversized("4G", 12))

    def test_upsize_does_not_raise(self) -> None:
        self.assertEqual("8G", hc.clamp_to_host("8G", 24))
        self.assertEqual("10G", hc.clamp_to_host("10G", 24))
        self.assertIsNone(hc.capped_token_if_oversized("10G", 24))
        self.assertIsNone(hc.capped_token_if_oversized("12G", 24))


class HeapClampApplyTests(unittest.TestCase):
    def test_read_current_heap_from_jvm_env(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = os.path.join(tmp, "jvm.env")
            with open(env_path, "w", encoding="utf-8") as f:
                f.write("export JVM_XMS=12G\nexport JVM_XMX=12G\n")
            self.assertEqual("12G", hc.read_current_heap(env_path, os.path.join(tmp, "missing.service")))

    def test_ensure_fits_host_noops_when_legal(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = os.path.join(tmp, "jvm.env")
            with open(env_path, "w", encoding="utf-8") as f:
                f.write("export JVM_XMS=8G\nexport JVM_XMX=8G\n")
            old_env = os.environ.get("MCMGR_JVM_ENV")
            os.environ["MCMGR_JVM_ENV"] = env_path
            try:
                rc, msg = hc.ensure_fits_host(12, daemon_reload=False)
            finally:
                if old_env is None:
                    os.environ.pop("MCMGR_JVM_ENV", None)
                else:
                    os.environ["MCMGR_JVM_ENV"] = old_env
        self.assertEqual(0, rc)
        self.assertIn("fits", msg)
        self.assertNotIn("Clamped", msg)

    def test_ensure_fits_host_runs_apply_script(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = os.path.join(tmp, "jvm.env")
            with open(env_path, "w", encoding="utf-8") as f:
                f.write("export JVM_XMS=12G\nexport JVM_XMX=12G\n")
            script = os.path.join(tmp, "apply-jvm-heap.py")
            marker = os.path.join(tmp, "applied.txt")
            with open(script, "w", encoding="utf-8") as f:
                f.write(
                    "import sys\n"
                    f"open({marker!r}, 'w', encoding='utf-8').write(sys.argv[1])\n"
                    "print('OK heap=' + sys.argv[1])\n"
                )
            old_env = os.environ.get("MCMGR_JVM_ENV")
            os.environ["MCMGR_JVM_ENV"] = env_path
            try:
                rc, msg = hc.ensure_fits_host(
                    12,
                    apply_script=script,
                    python=sys.executable,
                    daemon_reload=False,
                )
            finally:
                if old_env is None:
                    os.environ.pop("MCMGR_JVM_ENV", None)
                else:
                    os.environ["MCMGR_JVM_ENV"] = old_env
            self.assertEqual(0, rc, msg)
            self.assertIn("12G → 8G", msg)
            with open(marker, encoding="utf-8") as f:
                self.assertEqual("8G", f.read())

    def test_ensure_fits_host_fails_closed_when_script_missing(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = os.path.join(tmp, "jvm.env")
            with open(env_path, "w", encoding="utf-8") as f:
                f.write("export JVM_XMS=10G\nexport JVM_XMX=10G\n")
            old_env = os.environ.get("MCMGR_JVM_ENV")
            os.environ["MCMGR_JVM_ENV"] = env_path
            try:
                rc, msg = hc.ensure_fits_host(
                    12,
                    apply_script=os.path.join(tmp, "missing.py"),
                    daemon_reload=False,
                )
            finally:
                if old_env is None:
                    os.environ.pop("MCMGR_JVM_ENV", None)
                else:
                    os.environ["MCMGR_JVM_ENV"] = old_env
            self.assertNotEqual(0, rc)
            self.assertIn("was not found", msg)

    def test_ensure_fits_host_fails_when_apply_returns_nonzero(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            env_path = os.path.join(tmp, "jvm.env")
            with open(env_path, "w", encoding="utf-8") as f:
                f.write("export JVM_XMX=10G\n")
            script = os.path.join(tmp, "apply-jvm-heap.py")
            with open(script, "w", encoding="utf-8") as f:
                f.write("import sys\nprint('ERROR: boom', file=sys.stderr)\nsys.exit(1)\n")
            os.chmod(script, stat.S_IRUSR | stat.S_IWUSR | stat.S_IXUSR)
            old_env = os.environ.get("MCMGR_JVM_ENV")
            os.environ["MCMGR_JVM_ENV"] = env_path
            try:
                rc, msg = hc.ensure_fits_host(
                    12,
                    apply_script=script,
                    python=sys.executable,
                    daemon_reload=False,
                )
            finally:
                if old_env is None:
                    os.environ.pop("MCMGR_JVM_ENV", None)
                else:
                    os.environ["MCMGR_JVM_ENV"] = old_env
            self.assertNotEqual(0, rc)
            self.assertIn("failed", msg)


if __name__ == "__main__":
    unittest.main()
