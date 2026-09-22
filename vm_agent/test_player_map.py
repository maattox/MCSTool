"""Player-map region detect, cap, extra-dim skip, publish omit processed."""

from __future__ import annotations

import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from types import SimpleNamespace

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import player_map as pm  # noqa: E402


def _touch_mca(region: Path, name: str = "r.0.0.mca") -> None:
    region.mkdir(parents=True, exist_ok=True)
    (region / name).write_bytes(b"mca")


class DetectRegionTests(unittest.TestCase):
    def test_paper_26_layout(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            world = Path(raw) / "world"
            _touch_mca(world / "dimensions" / "minecraft" / "overworld" / "region")
            _touch_mca(world / "dimensions" / "minecraft" / "the_nether" / "region")
            _touch_mca(world / "dimensions" / "minecraft" / "the_end" / "region")
            self.assertEqual(
                world / "dimensions" / "minecraft" / "overworld" / "region",
                pm.detect_region_dir(world, "overworld"),
            )
            self.assertEqual(
                world / "dimensions" / "minecraft" / "the_nether" / "region",
                pm.detect_region_dir(world, "nether"),
            )
            self.assertEqual(
                world / "dimensions" / "minecraft" / "the_end" / "region",
                pm.detect_region_dir(world, "end"),
            )

    def test_legacy_split_folders(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            server = Path(raw)
            world = server / "world"
            _touch_mca(world / "region")
            _touch_mca(server / "world_nether" / "region")
            _touch_mca(server / "world_the_end" / "DIM1" / "region")
            self.assertEqual(world / "region", pm.detect_region_dir(world, "overworld"))
            self.assertEqual(
                server / "world_nether" / "region",
                pm.detect_region_dir(world, "nether"),
            )
            self.assertEqual(
                server / "world_the_end" / "DIM1" / "region",
                pm.detect_region_dir(world, "end"),
            )


class ExtraDimensionTests(unittest.TestCase):
    def test_skips_vanilla_three_and_lists_modded(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            world = Path(raw) / "world"
            _touch_mca(world / "dimensions" / "minecraft" / "overworld" / "region")
            _touch_mca(world / "dimensions" / "minecraft" / "the_nether" / "region")
            _touch_mca(world / "dimensions" / "minecraft" / "the_end" / "region")
            _touch_mca(world / "dimensions" / "twilightforest" / "twilight_forest" / "region")
            self.assertEqual(
                ["twilightforest/twilight_forest"],
                pm.extra_dimension_ids(world),
            )


class CapAndPublishTests(unittest.TestCase):
    def test_copy_omits_processed(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            src = Path(raw) / "render"
            (src / "map").mkdir(parents=True)
            (src / "processed").mkdir()
            (src / "map" / "r.0.0.webp").write_bytes(b"w")
            (src / "processed" / "chunk.bin").write_bytes(b"p")
            (src / "info.json").write_text("{}", encoding="utf-8")
            dest = Path(raw) / "data"
            pm.copy_tiles_without_processed(src, dest)
            self.assertTrue((dest / "map" / "r.0.0.webp").is_file())
            self.assertTrue((dest / "info.json").is_file())
            self.assertFalse((dest / "processed").exists())

    def test_tick_refuses_over_cap(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            world = root / "world"
            _touch_mca(world / "region")
            (world / "level.dat").write_bytes(b"dat")
            binary = root / "minedmap"
            binary.write_text("#!/bin/sh\nexit 0\n", encoding="utf-8")
            binary.chmod(0o755)
            map_root = root / "map"
            render = map_root / "render" / "overworld"
            render.mkdir(parents=True)
            huge = render / "pad.bin"
            # Pretend the tree is already over cap without writing 2 GiB.
            orig = pm.tree_bytes

            def fake_tree(path: Path) -> int:
                if path == map_root / "render" or str(path).endswith("render"):
                    return pm.CAP_BYTES + 1
                return orig(path)

            pm.tree_bytes = fake_tree  # type: ignore[method-assign]
            try:
                def runner(*_a, **_k):
                    return SimpleNamespace(returncode=0, stdout="", stderr="")

                msg = pm.tick(
                    {
                        "world_path": str(world),
                        "minedmap_path": str(binary),
                        "player_map_root": str(map_root),
                    },
                    game_up=True,
                    save_flush=lambda: None,
                    runner=runner,
                )
            finally:
                pm.tree_bytes = orig  # type: ignore[method-assign]
            self.assertIn("over 2 GiB cap", msg)

    def test_tick_skip_when_game_down(self) -> None:
        msg = pm.tick({}, game_up=False, save_flush=lambda: None)
        self.assertEqual("player-map skip (minecraft down)", msg)

    def test_publish_switcher_and_dims(self) -> None:
        with tempfile.TemporaryDirectory() as raw:
            root = Path(raw)
            viewer = root / "viewer"
            viewer.mkdir()
            (viewer / "index.html").write_text("<html>v</html>", encoding="utf-8")
            (viewer / "app.js").write_text("js", encoding="utf-8")
            for dim in pm.DIMS:
                d = root / "render" / dim
                d.mkdir(parents=True)
                (d / "processed").mkdir()
                (d / "processed" / "x").write_text("nope", encoding="utf-8")
                (d / "info.json").write_text("{}", encoding="utf-8")
            pm.publish_from_render(root)
            root_html = (root / "publish" / "index.html").read_text(encoding="utf-8")
            ow_html = (root / "publish" / "overworld" / "index.html").read_text(encoding="utf-8")
            self.assertIn("<title>Map</title>", root_html)
            self.assertNotIn("<title>MinedMap</title>", root_html)
            self.assertIn("Overworld", root_html)
            self.assertIn("Nether", root_html)
            self.assertIn("End", root_html)
            self.assertIn("Explored terrain only", root_html)
            self.assertIn('aria-current="page"', root_html)
            self.assertIn("/overworld/map.html", root_html)
            self.assertIn('href="/nether/"', root_html)
            self.assertIn('href="/chrome.css"', root_html)
            self.assertIn("class=\"slot\"", root_html)
            self.assertTrue((root / "publish" / "chrome.css").is_file())
            self.assertTrue((root / "publish" / "chrome.js").is_file())
            self.assertTrue((root / "publish" / "overworld" / "index.html").is_file())
            self.assertTrue((root / "publish" / "overworld" / "map.html").is_file())
            self.assertIn("<html>v</html>", (root / "publish" / "overworld" / "map.html").read_text(encoding="utf-8"))
            self.assertIn('data-dim="nether"', ow_html)
            self.assertIn('href="/nether/"', ow_html)
            self.assertIn("/nether/map.html", ow_html)
            self.assertTrue((root / "publish" / "nether" / "data" / "info.json").is_file())
            self.assertFalse((root / "publish" / "overworld" / "data" / "processed").exists())
            for html_path in (root / "publish").rglob("*.html"):
                text = html_path.read_text(encoding="utf-8")
                self.assertNotIn("friend", text)
                if html_path.name == "index.html":
                    self.assertIn("<title>Map</title>", text)
            css = (root / "publish" / "chrome.css").read_text(encoding="utf-8")
            js = (root / "publish" / "chrome.js").read_text(encoding="utf-8")
            self.assertNotIn("friend", css)
            self.assertNotIn("friend", js)

    def test_door_stub_matches_chrome_voice(self) -> None:
        stub = Path(_HERE).resolve().parent / "door_vm" / "player-map" / "index.html"
        text = stub.read_text(encoding="utf-8")
        self.assertIn("<title>Map</title>", text)
        self.assertIn("not ready yet", text.lower())
        self.assertIn("Overworld", text)
        self.assertIn("Explored terrain only", text)
        self.assertNotIn("friend", text)
        self.assertNotIn("MinedMap", text)


class MinedMapCmdTests(unittest.TestCase):
    def test_nice_webp_j1(self) -> None:
        seen: list[list[str]] = []

        def runner(cmd, **_k):
            seen.append(list(cmd))
            return subprocess.CompletedProcess(cmd, 0, "", "")

        with tempfile.TemporaryDirectory() as raw:
            out = Path(raw) / "out"
            pm.run_minedmap(Path("/opt/mcmgr/bin/minedmap"), Path("/world"), out, runner=runner)
        self.assertEqual(["nice", "-n", "19"], seen[0][:3])
        self.assertIn("--image-format", seen[0])
        self.assertIn("webp", seen[0])
        self.assertEqual("1", seen[0][seen[0].index("-j") + 1])


if __name__ == "__main__":
    raise SystemExit(unittest.main())
