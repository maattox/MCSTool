#!/usr/bin/env python3
"""Local dry-run for apply-jvm-heap.py (no SSH)."""
from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import json
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

        custom = ["-XX:+UseG1GC", "-XX:CustomFlag=1"]
        flags.write_text(json.dumps(custom) + "\n", encoding="utf-8")
        extras = t / "extras.json"
        extras.write_text(json.dumps(custom) + "\n", encoding="utf-8")
        r2 = subprocess.run(
            [sys.executable, str(SCRIPT), "set-extras", str(extras)],
            env=env,
            check=False,
            capture_output=True,
            text=True,
        )
        if r2.returncode != 0:
            print(r2.stdout, r2.stderr)
            return r2.returncode
        assert "OK extras_set=" in r2.stdout, r2.stdout
        unit_custom = unit.read_text(encoding="utf-8")
        assert "-XX:CustomFlag=1" in unit_custom, unit_custom
        r3 = subprocess.run(
            [sys.executable, str(SCRIPT), "8G"],
            env=env,
            check=False,
            capture_output=True,
            text=True,
        )
        if r3.returncode != 0:
            print(r3.stdout, r3.stderr)
            return r3.returncode
        unit_after = unit.read_text(encoding="utf-8")
        assert "OK heap=8G" in r3.stdout, r3.stdout
        assert "-Xms8G" in unit_after and "-Xmx8G" in unit_after, unit_after
        assert "-XX:CustomFlag=1" in unit_after, unit_after
        assert "-XX:+UseG1GC" in unit_after, unit_after
        r4 = subprocess.run(
            [sys.executable, str(SCRIPT), "dump-extras"],
            env=env,
            check=False,
            capture_output=True,
            text=True,
        )
        if r4.returncode != 0:
            print(r4.stdout, r4.stderr)
            return r4.returncode
        assert "OK extras=" in r4.stdout, r4.stdout
        assert "CustomFlag=1" in r4.stdout, r4.stdout
        print("apply-jvm-heap dry-run: ok")
        return 0


if __name__ == "__main__":
    raise SystemExit(main())
