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


if __name__ == "__main__":
    unittest.main()
