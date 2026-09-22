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
DIM_LABELS = {"overworld": "Overworld", "nether": "Nether", "end": "End"}
SKIP_PUBLISH_NAMES = {"processed"}

# P1 visual lock: oak night + binding bar; hotbar-slot tabs; system UI fonts.
CHROME_CSS = """:root {
  --void: #161310;
  --binding: #2A2218;
  --item: #E8DCC0;
  --quiet: #9A8E78;
  --grass: #4A7A36;
  --netherrack: #A34B40;
  --end-stone: #C9C07A;
  --well: #1A1510;
  --bevel-dark: #0A0907;
  --bevel-light: #5C4E3C;
  --font: ui-sans-serif, system-ui, "Segoe UI", sans-serif;
}
*, *::before, *::after { box-sizing: border-box; }
html { color-scheme: dark; height: 100%; }
body {
  margin: 0;
  height: 100%;
  height: 100dvh;
  background: var(--void);
  color: var(--item);
  font-family: var(--font);
}
::selection { background: var(--grass); color: var(--item); }
.skip {
  position: absolute;
  left: 12px;
  top: 8px;
  z-index: 2;
  padding: 6px 10px;
  background: var(--binding);
  color: var(--item);
  text-decoration: none;
  transform: translateY(-120%);
}
.skip:focus { transform: none; }
.shell {
  display: grid;
  grid-template-rows: auto minmax(0, 1fr);
  height: 100%;
}
.bar {
  display: grid;
  grid-template-columns: auto minmax(0, 1fr) auto;
  grid-template-areas: "title tabs note";
  align-items: center;
  gap: 10px 16px;
  padding: 8px 14px;
  padding-top: max(8px, env(safe-area-inset-top));
  padding-left: max(14px, env(safe-area-inset-left));
  padding-right: max(14px, env(safe-area-inset-right));
  background: var(--binding);
  border-bottom: 1px solid var(--bevel-dark);
  box-shadow: inset 0 1px 0 #3D3428;
}
.brand {
  grid-area: title;
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  letter-spacing: 0.01em;
  color: var(--item);
  text-wrap: balance;
}
.dims {
  grid-area: tabs;
  display: flex;
  flex-wrap: nowrap;
  gap: 4px;
  min-width: 0;
}
.note {
  grid-area: note;
  margin: 0;
  font-size: 11px;
  font-weight: 400;
  line-height: 1.35;
  color: var(--quiet);
  text-align: right;
  min-width: 0;
}
.slot {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 0;
  padding: 0 14px;
  height: 32px;
  border-radius: 0;
  background: var(--well);
  color: var(--quiet);
  font-size: 13px;
  font-weight: 500;
  font-family: inherit;
  text-decoration: none;
  touch-action: manipulation;
  -webkit-tap-highlight-color: transparent;
  box-shadow:
    inset 2px 2px 0 var(--bevel-dark),
    inset -2px -2px 0 var(--bevel-light);
}
.slot:hover { color: var(--item); background: #211A14; }
.slot:focus { outline: none; }
.slot:focus-visible {
  outline: 2px solid var(--item);
  outline-offset: 2px;
}
.slot[data-dim="overworld"][aria-current="page"] { --slot-ink: var(--grass); }
.slot[data-dim="nether"][aria-current="page"] { --slot-ink: var(--netherrack); }
.slot[data-dim="end"][aria-current="page"] { --slot-ink: var(--end-stone); }
.slot[aria-current="page"] {
  color: var(--item);
  box-shadow:
    inset 0 0 0 3px var(--slot-ink),
    inset -2px -2px 0 var(--bevel-dark),
    inset 2px 2px 0 var(--bevel-light);
}
.slot[aria-disabled="true"] {
  pointer-events: none;
  opacity: 0.55;
}
.stage { min-height: 0; background: var(--void); }
.map-frame {
  display: block;
  width: 100%;
  height: 100%;
  border: 0;
  background: var(--void);
}
@media (max-width: 639px) {
  .bar {
    grid-template-columns: minmax(0, 1fr) auto;
    grid-template-areas:
      "title note"
      "tabs tabs";
  }
  .note { text-align: left; }
  .dims {
    display: grid;
    grid-template-columns: 1fr 1fr 1fr;
  }
  .slot { height: 44px; min-height: 44px; padding: 0 8px; }
}
@media (prefers-reduced-motion: reduce) {
  .slot { transition: none; }
}
"""

CHROME_JS = """(function () {
  var nav = document.querySelector("[data-map-nav]");
  var frame = document.getElementById("map-frame");
  if (!nav || !frame) return;

  function applyLink(a) {
    if (!a) return;
    var src = a.getAttribute("data-map");
    var label = a.textContent.trim();
    if (src) frame.src = src;
    frame.title = label + " map";
    nav.querySelectorAll("a[data-dim]").forEach(function (el) {
      if (el === a) el.setAttribute("aria-current", "page");
      else el.removeAttribute("aria-current");
    });
  }

  function linkForPath() {
    var path = (location.pathname || "/").replace(/\\/+$/, "") || "/";
    var links = Array.prototype.slice.call(nav.querySelectorAll("a[data-dim]"));
    var match = links.find(function (a) {
      try {
        var p = new URL(a.href, location.href).pathname.replace(/\\/+$/, "") || "/";
        return p === path;
      } catch (e) {
        return false;
      }
    });
    if (match) return match;
    var dim = /\\/nether$/.test(path) ? "nether" : /\\/end$/.test(path) ? "end" : "overworld";
    return nav.querySelector('a[data-dim="' + dim + '"]');
  }

  nav.addEventListener("click", function (e) {
    var a = e.target.closest("a[data-dim]");
    if (!a || e.defaultPrevented) return;
    if (e.metaKey || e.ctrlKey || e.shiftKey || e.altKey || e.button !== 0) return;
    e.preventDefault();
    applyLink(a);
    var href = a.getAttribute("href");
    if (href && history.pushState) history.pushState({ dim: a.getAttribute("data-dim") }, "", href);
  });

  window.addEventListener("popstate", function () {
    applyLink(linkForPath());
  });
})();
"""


def _chrome_hrefs() -> dict[str, tuple[str, str]]:
    """dim -> (page href, map.html src). Root-relative so in-page tab switches stay valid."""
    return {
        "overworld": ("/", "/overworld/map.html"),
        "nether": ("/nether/", "/nether/map.html"),
        "end": ("/end/", "/end/map.html"),
    }


def chrome_html(dim: str, *, at_root: bool = False) -> str:
    _ = at_root  # same root-relative assets on / and /overworld/
    hrefs = _chrome_hrefs()
    css = "/chrome.css"
    js = "/chrome.js"
    iframe_src, iframe_title = hrefs[dim][1], f"{DIM_LABELS[dim]} map"
    slots = []
    for name in DIMS:
        page_href, map_src = hrefs[name]
        current = ' aria-current="page"' if name == dim else ""
        slots.append(
            f'<a class="slot" data-dim="{name}" data-map="{map_src}" '
            f'href="{page_href}"{current}>{DIM_LABELS[name]}</a>'
        )
    slot_markup = "\n        ".join(slots)
    return f"""<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="theme-color" content="#161310">
  <title>Map</title>
  <link rel="stylesheet" href="{css}">
</head>
<body>
  <a class="skip" href="#map-frame">Skip to map</a>
  <div class="shell">
    <header class="bar">
      <h1 class="brand">Map</h1>
      <nav class="dims" data-map-nav aria-label="Dimension">
        {slot_markup}
      </nav>
      <p class="note">Explored terrain only</p>
    </header>
    <main class="stage">
      <iframe class="map-frame" id="map-frame" title="{iframe_title}" src="{iframe_src}"></iframe>
    </main>
  </div>
  <script src="{js}"></script>
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


def install_chrome(dest: Path, dim: str, *, at_root: bool = False) -> None:
    """Wrap a copied MinedMap viewer: keep stock page as map.html, write product chrome."""
    dest.mkdir(parents=True, exist_ok=True)
    if not at_root:
        stock = dest / "index.html"
        if stock.is_file():
            stock.replace(dest / "map.html")
    (dest / "index.html").write_text(chrome_html(dim, at_root=at_root), encoding="utf-8")


def write_switcher(publish: Path) -> None:
    publish.mkdir(parents=True, exist_ok=True)
    (publish / "chrome.css").write_text(CHROME_CSS, encoding="utf-8")
    (publish / "chrome.js").write_text(CHROME_JS, encoding="utf-8")
    install_chrome(publish, "overworld", at_root=True)


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
        install_chrome(staging / dim, dim, at_root=False)
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
