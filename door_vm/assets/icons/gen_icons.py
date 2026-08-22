#!/usr/bin/env python3
"""Generate 64x64 solid-color PNG icons for mcdoor favicons.

Greenfield defaults are the Manager-composed variants (user or default-icon.png
plus overlays) written to idle.png / starting.png / exhausted.png. This script
is a last-resort solid-color fallback when those files are missing.
"""
from __future__ import annotations

import struct
import zlib
from pathlib import Path


def write_png(path: Path, r: int, g: int, b: int, size: int = 64) -> None:
    def chunk(tag: bytes, data: bytes) -> bytes:
        return (
            struct.pack(">I", len(data))
            + tag
            + data
            + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        )

    raw = b"".join(b"\x00" + bytes([r, g, b]) * size for _ in range(size))
    compressed = zlib.compress(raw, 9)
    ihdr = struct.pack(">IIBBBBB", size, size, 8, 2, 0, 0, 0)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", ihdr) + chunk(b"IDAT", compressed) + chunk(b"IEND", b"")
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_bytes(png)


def main() -> None:
    root = Path(__file__).resolve().parent
    write_png(root / "idle.png", 46, 139, 87)
    write_png(root / "starting.png", 255, 165, 0)
    write_png(root / "exhausted.png", 178, 34, 34)


if __name__ == "__main__":
    main()
