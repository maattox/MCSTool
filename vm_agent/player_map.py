"""Niced MinedMap render + publish tree for the door VCN pull.

Sibling of idle_watch. Invoked by mc-player-map.timer (not dumped into idle_watch).
Explored chunks only. Overworld / Nether / End. Extra dimensions/ namespaces are
detected and skipped (no extra product UI).
"""

from __future__ import annotations

import hashlib
import json
import os
import shutil
import subprocess
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Callable

LIB = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "lib")
HERE = os.path.dirname(os.path.abspath(__file__))
if LIB not in sys.path:
    sys.path.insert(0, LIB)
if HERE not in sys.path:
    sys.path.insert(0, HERE)

from rcon_client import RconClient, RconError  # noqa: E402

CONFIG_PATH = os.environ.get("MC_MANAGER_CONFIG", "/etc/mc-manager/config.json")
DEFAULT_WORLD = "/opt/mcmgr/server/world"
DEFAULT_MINEDMAP = "/opt/mcmgr/bin/minedmap"
DEFAULT_MAP_ROOT = "/var/lib/mcmgr-map"
CAP_BYTES = 2 * 1024 * 1024 * 1024
RENDER_TIMEOUT_SEC = 15 * 60
DIMS = ("overworld", "nether", "end")
SKIP_PUBLISH_NAMES = {"processed"}

SWITCHER_HTML = """<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Map</title>
  <style>
    body { font-family: sans-serif; background: #222; color: #eee; margin: 1.5rem; }
    a { color: #8cf; margin-right: 1.25rem; }
    p { max-width: 40rem; line-height: 1.45; }
  </style>
</head>
<body>
  <p>
    <a href="overworld/">Overworld</a>
    <a href="nether/">Nether</a>
    <a href="end/">End</a>
  </p>
  <p>Explored terrain only. The last render stays available when the game server is stopped.</p>
</body>
</html>
"""


def utc_iso() -> str:
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def load_config(path: str | None = None) -> dict:
    cfg_path = path or CONFIG_PATH
    with open(cfg_path, "r", encoding="utf-8") as f:
        return json.load(f)


def map_root(cfg: dict) -> Path:
    return Path(cfg.get("player_map_root") or DEFAULT_MAP_ROOT)


def world_path(cfg: dict) -> Path:
    return Path(cfg.get("world_path") or DEFAULT_WORLD)


def minedmap_bin(cfg: dict) -> Path:
    return Path(cfg.get("minedmap_path") or DEFAULT_MINEDMAP)


def tree_bytes(path: Path) -> int:
    total = 0
    if not path.exists():
        return 0
    if path.is_file():
        return path.stat().st_size
    for root, _dirs, files in os.walk(path):
        for name in files:
            fp = Path(root) / name
            try:
                total += fp.stat().st_size
            except OSError:
                pass
    return total


def _has_region_files(region: Path) -> bool:
    try:
        next(region.glob("*.mca"))
        return True
    except StopIteration:
        return False


def detect_region_dir(world: Path, dim: str) -> Path | None:
    """Return the live region/ directory for a dimension, or None."""
    if dim == "overworld":
        candidates = [
            world / "dimensions" / "minecraft" / "overworld" / "region",
            world / "region",
        ]
    elif dim == "nether":
        candidates = [
            world / "dimensions" / "minecraft" / "the_nether" / "region",
            world.parent / "world_nether" / "region",
            world.parent / "world_nether" / "DIM-1" / "region",
            world / "DIM-1" / "region",
        ]
    elif dim == "end":
        candidates = [
            world / "dimensions" / "minecraft" / "the_end" / "region",
            world.parent / "world_the_end" / "region",
            world.parent / "world_the_end" / "DIM1" / "region",
            world / "DIM1" / "region",
        ]
    else:
        return None

    for cand in candidates:
        if cand.is_dir() and _has_region_files(cand):
            return cand
    for cand in candidates:
        if cand.is_dir():
            return cand
    return None


def extra_dimension_ids(world: Path) -> list[str]:
    """Namespaced dimensions beyond Overworld/Nether/End. Not rendered (no extra UI)."""
    root = world / "dimensions"
    skip = {("minecraft", "overworld"), ("minecraft", "the_nether"), ("minecraft", "the_end")}
    found: list[str] = []
    if not root.is_dir():
        return found
    for ns in sorted(root.iterdir()):
        if not ns.is_dir():
            continue
        for name in sorted(ns.iterdir()):
            if not name.is_dir():
                continue
            if (ns.name, name.name) in skip:
                continue
            region = name / "region"
            if region.is_dir():
                found.append(f"{ns.name}/{name.name}")
    return found


def prepare_shim(world: Path, region: Path, shim_root: Path) -> Path:
    """MinedMap 2.8.0 has no --dimension flag. Nether/End need a fake world dir."""
    shim_root.mkdir(parents=True, exist_ok=True)
    level = world / "level.dat"
    link_level = shim_root / "level.dat"
    link_region = shim_root / "region"
    if link_level.exists() or link_level.is_symlink():
        link_level.unlink()
    if link_region.exists() or link_region.is_symlink():
        if link_region.is_dir() and not link_region.is_symlink():
            shutil.rmtree(link_region)
        else:
            link_region.unlink()
    if level.is_file():
        os.symlink(level, link_level)
    os.symlink(region, link_region)
    return shim_root


def overworld_input(world: Path) -> Path:
    return world


def copy_viewer_into(dest: Path, viewer_src: Path) -> None:
    if dest.exists():
        shutil.rmtree(dest)
    dest.mkdir(parents=True, exist_ok=True)
    if not viewer_src.is_dir():
        raise FileNotFoundError(f"viewer template missing: {viewer_src}")
    for item in viewer_src.iterdir():
        if item.name == "data":
            continue
        target = dest / item.name
        if item.is_dir():
            shutil.copytree(item, target, symlinks=False)
        else:
            shutil.copy2(item, target)


def copy_tiles_without_processed(src: Path, dest_data: Path) -> None:
    dest_data.mkdir(parents=True, exist_ok=True)
    if not src.is_dir():
        return
    for item in src.iterdir():
        if item.name in SKIP_PUBLISH_NAMES:
            continue
        target = dest_data / item.name
        if item.is_dir():
            shutil.copytree(item, target, dirs_exist_ok=True)
        else:
            shutil.copy2(item, target)


def write_switcher(publish: Path) -> None:
    (publish / "index.html").write_text(SWITCHER_HTML, encoding="utf-8")


def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def build_tar_and_manifest(publish: Path, http_root: Path) -> dict:
    http_root.mkdir(parents=True, exist_ok=True)
    tar_path = http_root / "tiles.tar"
    tmp_tar = http_root / "tiles.tar.tmp"
    if tmp_tar.exists():
        tmp_tar.unlink()
    subprocess.run(
        ["tar", "-C", str(publish), "-cf", str(tmp_tar), "."],
        check=True,
    )
    tmp_tar.replace(tar_path)
    digest = sha256_file(tar_path)
    size = tar_path.stat().st_size
    doc = {
        "sha256": digest,
        "bytes": size,
        "rendered_at": utc_iso(),
    }
    (http_root / "manifest.json").write_text(json.dumps(doc) + "\n", encoding="utf-8")
    return doc


def minecraft_active(unit: str) -> bool:
    r = subprocess.run(
        ["systemctl", "is-active", unit],
        capture_output=True,
        text=True,
        check=False,
    )
    return r.returncode == 0 and r.stdout.strip() == "active"


def rcon_save_flush(cfg: dict) -> None:
    with RconClient(
        cfg.get("rcon_host", "127.0.0.1"),
        int(cfg.get("rcon_port", 25575)),
        cfg.get("rcon_password", ""),
    ) as r:
        r.command("save-all flush")


def run_minedmap(
    binary: Path,
    input_dir: Path,
    output_dir: Path,
    *,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> subprocess.CompletedProcess:
    output_dir.mkdir(parents=True, exist_ok=True)
    cmd = [
        "nice",
        "-n",
        "19",
        str(binary),
        "--image-format",
        "webp",
        "-j",
        "1",
        str(input_dir),
        str(output_dir),
    ]
    run = runner or subprocess.run
    return run(
        cmd,
        check=False,
        capture_output=True,
        text=True,
        timeout=RENDER_TIMEOUT_SEC,
    )


def publish_from_render(root: Path) -> None:
    viewer = root / "viewer"
    render = root / "render"
    publish = root / "publish"
    staging = root / "publish.next"
    if staging.exists():
        shutil.rmtree(staging)
    staging.mkdir(parents=True, exist_ok=True)
    write_switcher(staging)
    for dim in DIMS:
        copy_viewer_into(staging / dim, viewer)
        copy_tiles_without_processed(render / dim, staging / dim / "data")
    if publish.exists():
        shutil.rmtree(publish)
    staging.replace(publish)


def tick(
    cfg: dict,
    *,
    game_up: bool | None = None,
    save_flush: Callable[[], None] | None = None,
    runner: Callable[..., subprocess.CompletedProcess] | None = None,
) -> str:
    unit = cfg.get("minecraft_unit", "minecraft")
    if game_up is None:
        game_up = minecraft_active(unit)
    if not game_up:
        return "player-map skip (minecraft down)"

    binary = minedmap_bin(cfg)
    if not binary.is_file():
        return f"player-map skip (minedmap missing: {binary})"

    world = world_path(cfg)
    if not world.is_dir():
        return f"player-map skip (world missing: {world})"

    root = map_root(cfg)
    root.mkdir(parents=True, exist_ok=True)
    extras = extra_dimension_ids(world)
    extra_note = (
        f"; skipped extra dimensions {', '.join(extras)}" if extras else ""
    )

    flush = save_flush or (lambda: rcon_save_flush(cfg))
    try:
        flush()
    except (RconError, OSError, TimeoutError) as exc:
        return f"player-map skip (save-all flush failed: {exc})"

    rendered: list[str] = []
    for dim in DIMS:
        region = detect_region_dir(world, dim)
        if region is None:
            continue
        if dim == "overworld":
            input_dir = overworld_input(world)
        else:
            input_dir = prepare_shim(world, region, root / "shim" / dim)
        out_dir = root / "render" / dim
        try:
            proc = run_minedmap(binary, input_dir, out_dir, runner=runner)
        except subprocess.TimeoutExpired:
            return f"player-map fail ({dim} render timed out)"
        if proc.returncode != 0:
            err = (proc.stderr or proc.stdout or "").strip().splitlines()
            tail = err[-1] if err else f"exit {proc.returncode}"
            return f"player-map fail ({dim}: {tail})"
        rendered.append(dim)

    if not rendered:
        return "player-map skip (no region dirs)" + extra_note

    render_bytes = tree_bytes(root / "render")
    if render_bytes > CAP_BYTES:
        return f"player-map skip (render tree {render_bytes} bytes over 2 GiB cap)"

    publish_from_render(root)
    pub_bytes = tree_bytes(root / "publish")
    if pub_bytes > CAP_BYTES:
        return f"player-map skip (publish tree {pub_bytes} bytes over 2 GiB cap)"

    http_root = root / "http"
    build_tar_and_manifest(root / "publish", http_root)
    tar_bytes = tree_bytes(http_root / "tiles.tar")
    if tar_bytes > CAP_BYTES:
        return f"player-map skip (tiles.tar {tar_bytes} bytes over 2 GiB cap)"

    return f"player-map rendered {', '.join(rendered)}{extra_note}"


def main() -> int:
    cfg = load_config()
    try:
        print(tick(cfg))
    except Exception as exc:  # noqa: BLE001
        print(f"player-map tick failed: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
