#!/usr/bin/env python3
"""Local dry-run for apply-jvm-heap.py (no SSH)."""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "common" / "apply-jvm-heap.py"


def main() -> int:
    with tempfile.TemporaryDirectory() as tmp:
        t = Path(tmp)
        jvm_env = t / "jvm.env"
        user_args = t / "user_jvm_args.txt"
        unit = t / "minecraft.service"
        flags = t / "paper-jvm-flags.json"
        user_args.write_text("-Xms2G\n-Xmx4G\n# comment\n", encoding="utf-8")
        unit.write_text(
            "[Service]\n"
            "ExecStart=/usr/bin/java -Xms2G -Xmx4G -XX:+UseG1GC -XX:G1HeapRegionSize=8M "
            "-jar paper-1.21.8-1.jar --nogui\n",
            encoding="utf-8",
        )
        flags.write_text('["-XX:+UseG1GC","-XX:G1HeapRegionSize=8M"]\n', encoding="utf-8")
        env = os.environ.copy()
        env["MCMGR_JVM_ENV"] = str(jvm_env)
        env["MCMGR_USER_JVM_ARGS"] = str(user_args)
        env["MCMGR_SYSTEMD_UNIT"] = str(unit)
        env["MCMGR_GAME_MANIFEST"] = str(t / "missing.json")
        env["MCMGR_PAPER_JVM_FLAGS"] = str(flags)
        r = subprocess.run(
            [sys.executable, str(SCRIPT), "6G"],
            env=env,
            check=False,
            capture_output=True,
            text=True,
        )
        if r.returncode != 0:
            print(r.stdout, r.stderr)
            return r.returncode
        unit_text = unit.read_text(encoding="utf-8")
        args_text = user_args.read_text(encoding="utf-8")
        env_text = jvm_env.read_text(encoding="utf-8")
        assert "OK heap=6G" in r.stdout, r.stdout
        assert "-Xms6G" in unit_text and "-Xmx6G" in unit_text, unit_text
        assert "-Xms2G" not in unit_text and "-Xmx4G" not in unit_text, unit_text
        assert "-XX:+UseG1GC" in unit_text and "-XX:G1HeapRegionSize=8M" in unit_text, unit_text
        assert "-Xms6G" in args_text and "-Xmx6G" in args_text, args_text
        assert "export JVM_XMS=6G" in env_text and "export JVM_XMX=6G" in env_text, env_text
        print("apply-jvm-heap dry-run: ok")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
