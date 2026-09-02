#!/usr/bin/env python3
"""Rewrite Minecraft heap (Xms=Xmx) without dropping Paper Fill/Aikar flags.

Usage: apply-jvm-heap.py 4G|6G|8G

Patches:
  /etc/mcmgr/jvm.env
  /opt/mcmgr/server/user_jvm_args.txt (if present)
  /etc/systemd/system/minecraft.service ExecStart -Xms/-Xmx tokens
  /etc/mcmgr/game-manifest.json launch_command.args heap tokens (if present)

Does not restart Minecraft. Does not invent a second Aikar flag list.
"""
from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

ALLOWED = {"4G", "6G", "8G"}
JVM_ENV = Path(os.environ.get("MCMGR_JVM_ENV", "/etc/mcmgr/jvm.env"))
USER_ARGS = Path(os.environ.get("MCMGR_USER_JVM_ARGS", "/opt/mcmgr/server/user_jvm_args.txt"))
UNIT_PATH = Path(os.environ.get("MCMGR_SYSTEMD_UNIT", "/etc/systemd/system/minecraft.service"))
MANIFEST = Path(os.environ.get("MCMGR_GAME_MANIFEST", "/etc/mcmgr/game-manifest.json"))
PAPER_FLAGS = Path(os.environ.get("MCMGR_PAPER_JVM_FLAGS", "/etc/mcmgr/paper-jvm-flags.json"))
FALLBACK_FLAGS = ["-XX:+UseG1GC"]


def _fail(msg: str, code: int = 1) -> None:
    print(f"ERROR: {msg}", file=sys.stderr)
    raise SystemExit(code)


def strip_heap(flags: list[str]) -> list[str]:
    out: list[str] = []
    for raw in flags:
        s = str(raw).strip()
        if not s or s.startswith("-Xms") or s.startswith("-Xmx"):
            continue
        out.append(s)
    return out


def load_paper_flags() -> list[str]:
    if not PAPER_FLAGS.is_file():
        return list(FALLBACK_FLAGS)
    try:
        data = json.loads(PAPER_FLAGS.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return list(FALLBACK_FLAGS)
    if not isinstance(data, list):
        return list(FALLBACK_FLAGS)
    flags = strip_heap([str(x) for x in data])
    return flags if flags else list(FALLBACK_FLAGS)


def write_jvm_env(heap: str) -> None:
    JVM_ENV.parent.mkdir(parents=True, exist_ok=True)
    JVM_ENV.write_text(f"export JVM_XMS={heap}\nexport JVM_XMX={heap}\n", encoding="utf-8")


def patch_user_args(heap: str) -> None:
    if not USER_ARGS.is_file():
        return
    lines = USER_ARGS.read_text(encoding="utf-8").splitlines()
    new: list[str] = []
    seen_xms = seen_xmx = False
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("-Xms"):
            new.append(f"-Xms{heap}")
            seen_xms = True
        elif stripped.startswith("-Xmx"):
            new.append(f"-Xmx{heap}")
            seen_xmx = True
        else:
            new.append(line)
    prefix: list[str] = []
    if not seen_xms:
        prefix.append(f"-Xms{heap}")
    if not seen_xmx:
        prefix.append(f"-Xmx{heap}")
    USER_ARGS.write_text("\n".join(prefix + new) + "\n", encoding="utf-8")


def _needs_paper_flags(exec_line: str) -> bool:
    lower = exec_line.lower()
    if "-jar" not in lower or "@user_jvm_args.txt" in lower:
        return False
    if "-xx:+useg1gc" in lower:
        return False
    return "paper" in lower


def _inject_flags(exec_line: str, flags: list[str]) -> str:
    if not flags:
        return exec_line
    # Insert after the last -Xmx token, else after -Xms, else before -jar.
    m = re.search(r"(-Xmx\S+)", exec_line)
    if m:
        idx = m.end()
        return exec_line[:idx] + " " + " ".join(flags) + exec_line[idx:]
    m = re.search(r"(-Xms\S+)", exec_line)
    if m:
        idx = m.end()
        return exec_line[:idx] + " " + " ".join(flags) + exec_line[idx:]
    m = re.search(r"(\s-jar\s)", exec_line)
    if m:
        return exec_line[: m.start()] + " " + " ".join(flags) + exec_line[m.start() :]
    return exec_line


def patch_unit(heap: str) -> bool:
    """Return True if ExecStart contained Paper-style -jar (for logging)."""
    if not UNIT_PATH.is_file():
        _fail(f"missing systemd unit {UNIT_PATH}")
    text = UNIT_PATH.read_text(encoding="utf-8")
    lines = text.splitlines()
    paperish = False
    out: list[str] = []
    for line in lines:
        if line.startswith("ExecStart="):
            line = re.sub(r"-Xms\S+", f"-Xms{heap}", line)
            line = re.sub(r"-Xmx\S+", f"-Xmx{heap}", line)
            if _needs_paper_flags(line):
                paperish = True
                line = _inject_flags(line, load_paper_flags())
            elif "-jar" in line.lower() and "paper" in line.lower():
                paperish = True
        out.append(line)
    UNIT_PATH.write_text("\n".join(out) + "\n", encoding="utf-8")
    return paperish


def patch_manifest(heap: str) -> None:
    if not MANIFEST.is_file():
        return
    try:
        doc = json.loads(MANIFEST.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return
    if not isinstance(doc, dict):
        return
    launch = doc.get("launch_command")
    if not isinstance(launch, dict):
        return
    args = launch.get("args")
    if not isinstance(args, list):
        return
    new_args: list[object] = []
    seen_xms = seen_xmx = False
    for a in args:
        s = str(a)
        if s.startswith("-Xms"):
            new_args.append(f"-Xms{heap}")
            seen_xms = True
        elif s.startswith("-Xmx"):
            new_args.append(f"-Xmx{heap}")
            seen_xmx = True
        else:
            new_args.append(a)
    if seen_xms or seen_xmx:
        launch["args"] = new_args
        MANIFEST.write_text(json.dumps(doc, indent=2) + "\n", encoding="utf-8")


def main(argv: list[str]) -> int:
    if len(argv) != 2:
        _fail("usage: apply-jvm-heap.py 4G|6G|8G")
    heap = argv[1].strip().upper()
    if heap not in ALLOWED:
        _fail(f"heap must be 4G, 6G, or 8G (got {argv[1]!r})")
    write_jvm_env(heap)
    patch_user_args(heap)
    paperish = patch_unit(heap)
    patch_manifest(heap)
    print(f"OK heap={heap} paper_unit={int(paperish)}")
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main(sys.argv))
    except SystemExit:
        raise
    except Exception as exc:  # noqa: BLE001
        _fail(str(exc))
