"""MOTD apply helpers — no OCI. Run from repo: python vm_agent/test_motd.py"""

from __future__ import annotations

import os
import sys
import tempfile
import unittest

_HERE = os.path.dirname(os.path.abspath(__file__))
if _HERE not in sys.path:
    sys.path.insert(0, _HERE)

import os_publish as osp  # noqa: E402


class MotdTests(unittest.TestCase):
    def test_name_and_description(self) -> None:
        self.assertEqual(
            "Friends SMP\\nWeekend world",
            osp._build_motd("Friends SMP", "Weekend world"),
        )
        self.assertEqual(osp.DEFAULT_MOTD, osp._build_motd("", ""))
        self.assertEqual(
            "§6§l★§r§l §e§lOCI Server§r§l\u00a0§6§l★§r"
            "\\ncreated with §9§ngithub.com/maattox/MCSTool§r",
            osp.DEFAULT_MOTD,
        )

    def test_omit_name_is_ignored(self) -> None:
        self.assertEqual(
            "Friends SMP\\nWeekend world",
            osp._build_motd("Friends SMP", "Weekend world", omit_name=True),
        )

    def test_section_codes_preserved(self) -> None:
        motd = osp._build_motd("Friends SMP", "§cHello  §aWorld")
        self.assertEqual("Friends SMP\\n§cHello  §aWorld", motd)
        self.assertNotIn("\n", motd)
        self.assertNotIn("\r", motd)

    def test_extra_lines_and_overlong_are_clipped(self) -> None:
        self.assertEqual("§cHello", osp._build_motd("", "§cHello\n§aWorld"))
        self.assertEqual("x" * 59, osp._build_motd("", "x" * 80))

    def test_properties_write_keeps_section_and_escape(self) -> None:
        motd = osp._build_motd("§cHello", "§aWorld")
        self.assertEqual("§cHello\\n§aWorld", motd)
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "server.properties")
            with open(path, "w", encoding="utf-8") as f:
                f.write("max-players=8\n")
            osp._patch_properties_key(path, "motd", motd)
            with open(path, encoding="utf-8") as f:
                text = f.read()
        self.assertIn("motd=§cHello\\n§aWorld\n", text)
        self.assertIn("max-players=8\n", text)

    def test_apply_properties_map_writes_curated_and_skips_forbidden(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            path = os.path.join(tmp, "server.properties")
            with open(path, "w", encoding="utf-8") as f:
                f.write("max-players=20\nenable-rcon=true\nrcon.password=secret\nmotd=keep\n")
            applied = osp._apply_properties_map(
                path,
                {
                    "difficulty": "hard",
                    "max-players": 8,
                    "pvp": False,
                    "view-distance": "6",
                    "simulation-distance": "12",
                    "enable-rcon": "false",
                    "rcon.password": "hacked",
                    "online-mode": "false",
                    "motd": "nope",
                    "unknown-key": "x",
                },
            )
            with open(path, encoding="utf-8") as f:
                text = f.read()
        self.assertIn("difficulty", applied)
        self.assertIn("max-players", applied)
        self.assertNotIn("enable-rcon", applied)
        self.assertNotIn("motd", applied)
        self.assertIn("difficulty=hard\n", text)
        self.assertIn("max-players=8\n", text)
        self.assertIn("pvp=false\n", text)
        self.assertIn("view-distance=6\n", text)
        self.assertIn("simulation-distance=6\n", text)
        self.assertIn("enable-rcon=true\n", text)
        self.assertIn("rcon.password=secret\n", text)
        self.assertIn("motd=keep\n", text)
        self.assertNotIn("online-mode", text)
        self.assertNotIn("unknown-key", text)


if __name__ == "__main__":
    unittest.main()
