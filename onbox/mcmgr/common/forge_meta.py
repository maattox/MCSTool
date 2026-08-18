#!/usr/bin/env python3
"""Forge promotions_slim helper for the on-box installer (blueprint §20).

CLI:
  resolve <promotions_slim.json> <minecraft_version> [--forge-version V]
  self-test --fixtures <dir>

Versions come from files.minecraftforge.net promotions_slim.json (not the
ad-supported HTML page). Installer jars have no published checksum.
Not a Setup radio — packs that declare Forge only (1.12.2-era, 1.20.1).
"""
from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from urllib.parse import quote

PROMOTIONS_URL = (
    "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json"
)
MAVEN_BASE = "https://maven.minecraftforge.net/net/minecraftforge/forge"
HASH_ALGORITHM = "none_published"
LOADER_ID = "forge"
ARTIFACT_KIND_ARGFILE = "argfile_tree"
ARTIFACT_KIND_JAR = "single_jar"
_FORGE_VER_RE = re.compile(r"^\d+(\.\d+)+$")


class ForgeError(Exception):
    """CLI and self-test use this instead of SystemExit so tests can catch quietly."""


def _fail(msg: str) -> None:
    raise ForgeError(msg)


def _enc(value: str) -> str:
    return quote(value.strip(), safe=".-_~")


def combined_version(minecraft_version: str, forge_version: str) -> str:
    return f"{minecraft_version.strip()}-{forge_version.strip()}"


def installer_url(minecraft_version: str, forge_version: str) -> str:
    mc = (minecraft_version or "").strip()
    fg = (forge_version or "").strip()
    if not mc or not fg:
        _fail("Forge installer URL requires Minecraft and Forge versions.")
    token = _enc(combined_version(mc, fg))
    filename = _enc(f"forge-{combined_version(mc, fg)}-installer.jar")
    return f"{MAVEN_BASE}/{token}/{filename}"


def installer_filename(minecraft_version: str, forge_version: str) -> str:
    return f"forge-{combined_version(minecraft_version, forge_version)}-installer.jar"


def runnable_jar_filename(minecraft_version: str, forge_version: str) -> str:
    return f"forge-{combined_version(minecraft_version, forge_version)}.jar"


def unix_args_path(minecraft_version: str, forge_version: str) -> str:
    token = combined_version(minecraft_version, forge_version)
    return f"libraries/net/minecraftforge/forge/{token}/unix_args.txt"


def java_major_for_minecraft(minecraft_version: str) -> int:
    ident = (minecraft_version or "").strip()

    def starts(prefix: str) -> bool:
        if not ident.startswith(prefix):
            return False
        return len(ident) == len(prefix) or not ident[len(prefix)].isdigit()

    if ident.startswith("26.") or starts("26"):
        return 25
    if starts("1.21") or starts("1.22") or starts("1.20.5") or starts("1.20.6"):
        return 21
    if starts("1.18") or starts("1.19") or starts("1.20"):
        return 17
    if starts("1.17"):
        return 16
    return 8


def parse_minecraft(minecraft_version: str) -> tuple[int, int, int] | None:
    ident = (minecraft_version or "").strip()
    parts = [p for p in ident.split(".") if p]
    if len(parts) < 2 or len(parts) > 3:
        return None
    try:
        major = int(parts[0])
        minor = int(parts[1])
        patch = 0
        if len(parts) == 3:
            patch_token = parts[2].split("-", 1)[0]
            patch = int(patch_token)
    except ValueError:
        return None
    if major <= 0 or minor < 0 or patch < 0:
        return None
    return major, minor, patch


def is_supported_minecraft(minecraft_version: str) -> bool:
    parsed = parse_minecraft(minecraft_version)
    if parsed is None:
        return False
    major, minor, _patch = parsed
    if major >= 26:
        return True
    if major != 1:
        return False
    return minor >= 7


def uses_argfile_tree(minecraft_version: str) -> bool:
    parsed = parse_minecraft(minecraft_version)
    if parsed is None:
        return False
    major, minor, _patch = parsed
    if major >= 26:
        return True
    return major == 1 and minor >= 17


def is_forge_version_id(raw: str) -> bool:
    return bool(_FORGE_VER_RE.match((raw or "").strip()))


def parse_promos(json_text: str) -> dict[str, str] | None:
    try:
        data = json.loads(json_text)
    except json.JSONDecodeError:
        return None
    if not isinstance(data, dict):
        return None
    promos = data.get("promos")
    if not isinstance(promos, dict) or not promos:
        return None
    out: dict[str, str] = {}
    for key, val in promos.items():
        k = str(key or "").strip()
        v = str(val or "").strip()
        if k and v:
            out[k] = v
    return out or None


def load_promos(path: str) -> dict[str, str]:
    text = Path(path).read_text(encoding="utf-8")
    promos = parse_promos(text)
    if promos is None:
        _fail("Unexpected format of Forge promotions_slim.json (missing promos map).")
    return promos


def _promo(promos: dict[str, str], mc: str, channel: str) -> str | None:
    raw = (promos.get(f"{mc}-{channel}") or "").strip()
    if raw and is_forge_version_id(raw):
        return raw
    return None


def resolve(
    minecraft_version: str,
    promos: dict[str, str],
    forge_version: str | None = None,
) -> dict:
    mc = (minecraft_version or "").strip()
    if not mc:
        _fail("Minecraft version is required.")
    if not is_supported_minecraft(mc):
        _fail(
            f"Forge is not supported for Minecraft {mc}. "
            "The product floor is Minecraft 1.7 (1.12.2-era and 1.20.1 packs)."
        )

    pin = (forge_version or "").strip() or None
    if pin is None:
        rec = _promo(promos, mc, "recommended")
        latest = _promo(promos, mc, "latest")
        if rec:
            chosen, promo_used = rec, "recommended"
        elif latest:
            chosen, promo_used = latest, "latest"
        else:
            _fail(f"No Forge recommended or latest promo is published for Minecraft {mc}.")
    else:
        if not is_forge_version_id(pin):
            _fail(f"Forge version {pin} is not a valid Forge version id.")
        chosen, promo_used = pin, "pinned"

    url = installer_url(mc, chosen)
    if (
        not url.startswith(MAVEN_BASE)
        or "/forge-" not in url
        or not url.endswith("-installer.jar")
        or "files.minecraftforge.net" in url.lower()
    ):
        _fail("Refusing Forge installer URL that is not maven.minecraftforge.net installer.jar.")

    argfile = uses_argfile_tree(mc)
    return {
        "minecraft_version": mc,
        "loader": LOADER_ID,
        "loader_version": chosen,
        "installer_filename": installer_filename(mc, chosen),
        "installer_download_url": url,
        "runnable_jar_filename": runnable_jar_filename(mc, chosen),
        "unix_args_path": unix_args_path(mc, chosen) if argfile else "",
        "java_major": java_major_for_minecraft(mc),
        "hash_algorithm": HASH_ALGORITHM,
        "artifact_kind": ARTIFACT_KIND_ARGFILE if argfile else ARTIFACT_KIND_JAR,
        "promo_used": promo_used,
    }


def cmd_resolve(promos_path: str, minecraft_version: str, forge_version: str | None) -> None:
    doc = resolve(minecraft_version, load_promos(promos_path), forge_version)
    json.dump(doc, sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")


def cmd_self_test(fixtures_dir: str) -> None:
    fx = Path(fixtures_dir)
    if not fx.is_dir():
        _fail(f"fixtures dir missing: {fx}")

    promos = load_promos(str(fx / "forge-promotions-slim.json"))
    resolved = resolve("1.12.2", promos)
    assert resolved["loader_version"] == "14.23.5.2854", resolved
    assert resolved["loader_version"] != "14.23.5.2860"
    assert resolved["promo_used"] == "recommended"
    assert resolved["artifact_kind"] == "single_jar"
    assert resolved["installer_filename"] == "forge-1.12.2-14.23.5.2854-installer.jar"
    assert resolved["runnable_jar_filename"] == "forge-1.12.2-14.23.5.2854.jar"
    assert resolved["unix_args_path"] == ""
    assert resolved["installer_download_url"] == (
        "https://maven.minecraftforge.net/net/minecraftforge/forge/"
        "1.12.2-14.23.5.2854/forge-1.12.2-14.23.5.2854-installer.jar"
    )
    assert "files.minecraftforge.net" not in resolved["installer_download_url"]
    assert resolved["hash_algorithm"] == "none_published"
    assert resolved["java_major"] == 8

    modern = resolve("1.20.1", promos)
    assert modern["loader_version"] == "47.4.10", modern
    assert modern["artifact_kind"] == "argfile_tree"
    assert modern["unix_args_path"] == (
        "libraries/net/minecraftforge/forge/1.20.1-47.4.10/unix_args.txt"
    )
    assert modern["java_major"] == 17
    assert modern["promo_used"] == "recommended"

    latest_only = resolve("1.17.1", promos)
    assert latest_only["loader_version"] == "37.1.1"
    assert latest_only["promo_used"] == "latest"
    assert latest_only["artifact_kind"] == "argfile_tree"
    assert latest_only["java_major"] == 16

    pinned = resolve("1.12.2", promos, forge_version="14.23.5.2860")
    assert pinned["loader_version"] == "14.23.5.2860"
    assert pinned["promo_used"] == "pinned"

    try:
        resolve("1.6.4", promos)
        _fail("expected 1.6.4 to fail")
    except ForgeError as exc:
        assert "1.7" in str(exc)

    try:
        resolve("1.12.2", promos, forge_version="not-a-version")
        _fail("expected invalid pin to fail")
    except ForgeError:
        pass

    malformed = (fx / "forge-promotions-slim-malformed.json").read_text(encoding="utf-8")
    assert parse_promos(malformed) is None

    assert java_major_for_minecraft("1.16.5") == 8
    assert java_major_for_minecraft("1.20.1") == 17
    assert java_major_for_minecraft("26.1") == 25
    assert uses_argfile_tree("1.20.1")
    assert not uses_argfile_tree("1.12.2")

    print("forge_meta self-test: ok", file=sys.stderr)


def main(argv: list[str] | None = None) -> None:
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(newline="\n")
        except (OSError, ValueError):
            pass
    p = argparse.ArgumentParser(prog="forge_meta.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("resolve")
    r.add_argument("promotions_json")
    r.add_argument("minecraft_version")
    r.add_argument("--forge-version", default="")

    t = sub.add_parser("self-test")
    t.add_argument("--fixtures", required=True)

    args = p.parse_args(argv)
    if args.cmd == "resolve":
        cmd_resolve(args.promotions_json, args.minecraft_version, args.forge_version or None)
    elif args.cmd == "self-test":
        cmd_self_test(args.fixtures)
    else:
        _fail(f"unknown command {args.cmd}")


if __name__ == "__main__":
    try:
        main()
    except ForgeError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc
