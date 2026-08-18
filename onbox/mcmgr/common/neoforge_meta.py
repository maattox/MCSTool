#!/usr/bin/env python3
"""NeoForge Maven metadata helper for the on-box installer (blueprint §19).

CLI:
  resolve <maven-metadata.xml> <minecraft_version> [--neoforge-version V]
  self-test --fixtures <dir>

Versions come from Maven XML (not JSON). Installer jars have no published checksum.
"""
from __future__ import annotations

import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path
from urllib.parse import quote

MAVEN_BASE = "https://maven.neoforged.net/releases/net/neoforged/neoforge"
HASH_ALGORITHM = "none_published"
ARTIFACT_KIND = "argfile_tree"
LOADER_ID = "neoforge"


class NeoForgeError(Exception):
    """CLI and self-test use this instead of SystemExit so tests can catch quietly."""


def _fail(msg: str) -> None:
    raise NeoForgeError(msg)


def _enc(value: str) -> str:
    return quote(value.strip(), safe=".-_~")


def installer_url(version: str) -> str:
    v = (version or "").strip()
    if not v:
        _fail("NeoForge installer URL requires a version.")
    ev = _enc(v)
    return f"{MAVEN_BASE}/{ev}/neoforge-{ev}-installer.jar"


def installer_filename(version: str) -> str:
    return f"neoforge-{version.strip()}-installer.jar"


def unix_args_path(version: str) -> str:
    return f"libraries/net/neoforged/neoforge/{version.strip()}/unix_args.txt"


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
    if (
        starts("1.20.2")
        or starts("1.20.3")
        or starts("1.20.4")
        or starts("1.18")
        or starts("1.19")
        or starts("1.20")
    ):
        return 17
    if starts("1.17"):
        return 16
    return 21


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
    major, minor, patch = parsed
    if major >= 26:
        return True
    if major != 1:
        return False
    if minor > 20:
        return True
    if minor < 20:
        return False
    return patch >= 2


def minecraft_target(minecraft_version: str) -> tuple[int, int] | None:
    parsed = parse_minecraft(minecraft_version)
    if parsed is None:
        return None
    major, minor, patch = parsed
    if major >= 26:
        return major, minor
    if major != 1:
        return None
    return minor, patch


_NEO_VER_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$")


def parse_neoforge_version(raw: str) -> tuple[int, int, int, bool, str] | None:
    ident = (raw or "").strip()
    m = _NEO_VER_RE.match(ident)
    if not m:
        return None
    return int(m.group(1)), int(m.group(2)), int(m.group(3)), bool(m.group(4)), ident


def parse_versions(xml_text: str) -> list[str] | None:
    try:
        root = ET.fromstring(xml_text)
    except ET.ParseError:
        return None
    versions: list[str] = []
    for el in root.iter():
        tag = el.tag.split("}", 1)[-1]
        if tag != "version":
            continue
        val = (el.text or "").strip()
        if val:
            versions.append(val)
    return versions


def load_versions(path: str) -> list[str]:
    text = Path(path).read_text(encoding="utf-8")
    versions = parse_versions(text)
    if versions is None:
        _fail("Unexpected format of NeoForge maven-metadata.xml (not a Maven version list).")
    return versions


def resolve(
    minecraft_version: str,
    versions: list[str],
    neoforge_version: str | None = None,
) -> dict:
    mc = (minecraft_version or "").strip()
    if not mc:
        _fail("Minecraft version is required.")
    if not is_supported_minecraft(mc):
        _fail(
            f"NeoForge is not supported for Minecraft {mc} or older. "
            "Minecraft 1.20.2 is the NeoForge floor; use Forge for 1.20.1 packs."
        )
    target = minecraft_target(mc)
    if target is None:
        _fail(f"Cannot map Minecraft {mc} to a NeoForge version prefix.")
    mc_minor, mc_patch = target

    pin = (neoforge_version or "").strip() or None
    if pin is None:
        candidates = []
        for raw in versions:
            parsed = parse_neoforge_version(raw)
            if parsed is None:
                continue
            minor, patch, build, prerelease, ident = parsed
            if minor == mc_minor and patch == mc_patch and not prerelease:
                candidates.append((minor, patch, build, ident))
        if not candidates:
            _fail(f"No stable (non-beta) NeoForge version is published for Minecraft {mc}.")
        candidates.sort()
        chosen = candidates[-1][3]
    else:
        parsed = parse_neoforge_version(pin)
        if parsed is None:
            _fail(f"NeoForge version {pin} is not a valid Maven version id.")
        minor, patch, _build, _pre, _ident = parsed
        if minor != mc_minor or patch != mc_patch:
            _fail(f"NeoForge {pin} does not target Minecraft {mc}.")
        if pin not in [v.strip() for v in versions]:
            _fail(f"NeoForge {pin} was not found in maven.neoforged.net metadata.")
        chosen = pin

    url = installer_url(chosen)
    if (
        "/neoforge-" not in url
        or not url.endswith("-installer.jar")
        or not url.startswith(MAVEN_BASE)
    ):
        _fail("Refusing NeoForge installer URL that is not maven.neoforged.net installer.jar.")

    return {
        "minecraft_version": mc,
        "loader": LOADER_ID,
        "loader_version": chosen,
        "installer_filename": installer_filename(chosen),
        "installer_download_url": url,
        "unix_args_path": unix_args_path(chosen),
        "java_major": java_major_for_minecraft(mc),
        "hash_algorithm": HASH_ALGORITHM,
        "artifact_kind": ARTIFACT_KIND,
    }


def cmd_resolve(metadata_path: str, minecraft_version: str, neoforge_version: str | None) -> None:
    doc = resolve(minecraft_version, load_versions(metadata_path), neoforge_version)
    json.dump(doc, sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")


def cmd_self_test(fixtures_dir: str) -> None:
    fx = Path(fixtures_dir)
    if not fx.is_dir():
        _fail(f"fixtures dir missing: {fx}")

    versions = load_versions(str(fx / "neoforge-maven-metadata.xml"))
    resolved = resolve("1.21.1", versions)
    assert resolved["loader_version"] == "21.1.98", resolved
    assert resolved["installer_filename"] == "neoforge-21.1.98-installer.jar"
    assert resolved["installer_download_url"] == (
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/21.1.98/"
        "neoforge-21.1.98-installer.jar"
    )
    assert resolved["unix_args_path"] == "libraries/net/neoforged/neoforge/21.1.98/unix_args.txt"
    assert resolved["hash_algorithm"] == "none_published"
    assert resolved["artifact_kind"] == "argfile_tree"
    assert resolved["java_major"] == 21
    assert "21.10.1".startswith("21.1")
    assert not "21.10.1".startswith("21.1.")
    assert resolved["loader_version"] != "21.10.1"

    pinned = resolve("1.21.1", versions, neoforge_version="21.1.200-beta")
    assert pinned["loader_version"] == "21.1.200-beta"

    try:
        resolve("1.20.1", versions)
        _fail("expected 1.20.1 to fail")
    except NeoForgeError as exc:
        assert "1.20.2" in str(exc)

    try:
        resolve("1.21.1", versions, neoforge_version="21.8.31")
        _fail("expected wrong-game pin to fail")
    except NeoForgeError:
        pass

    malformed = (fx / "neoforge-maven-metadata-malformed.xml").read_text(encoding="utf-8")
    assert parse_versions(malformed) is None

    assert java_major_for_minecraft("1.20.4") == 17
    assert java_major_for_minecraft("26.1") == 25

    print("neoforge_meta self-test: ok", file=sys.stderr)


def main(argv: list[str] | None = None) -> None:
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(newline="\n")
        except (OSError, ValueError):
            pass
    p = argparse.ArgumentParser(prog="neoforge_meta.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("resolve")
    r.add_argument("metadata_xml")
    r.add_argument("minecraft_version")
    r.add_argument("--neoforge-version", default="")

    t = sub.add_parser("self-test")
    t.add_argument("--fixtures", required=True)

    args = p.parse_args(argv)
    if args.cmd == "resolve":
        cmd_resolve(args.metadata_xml, args.minecraft_version, args.neoforge_version or None)
    elif args.cmd == "self-test":
        cmd_self_test(args.fixtures)
    else:
        _fail(f"unknown command {args.cmd}")


if __name__ == "__main__":
    try:
        main()
    except NeoForgeError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc
