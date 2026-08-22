#!/usr/bin/env python3
"""Fabric meta.fabricmc.net v2 helper for the on-box installer (blueprint §18).

CLI:
  resolve <installer.json> <loader-for-game.json> <minecraft_version>
          [--loader-version V] [--installer-version V]
  self-test --fixtures <dir>

All three axes (game, loader, installer) are required for the /server/jar URL.
No checksum is published — hash_algorithm is always none_published.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any
from urllib.parse import quote

META_BASE = "https://meta.fabricmc.net"
HASH_ALGORITHM = "none_published"
ARTIFACT_KIND = "launcher_jar"


class FabricError(Exception):
    """CLI and self-test use this instead of SystemExit so tests can catch quietly."""


def _fail(msg: str) -> None:
    raise FabricError(msg)


def _load_json(path: str) -> Any:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def _enc(value: str) -> str:
    return quote(value.strip(), safe=".-_~")


def java_major_for_minecraft(minecraft_version: str) -> int:
    """Static Minecraft Java floor. Do not use launcherMeta.min_java_version (often 8)."""
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
    if (
        starts("1.12")
        or starts("1.13")
        or starts("1.14")
        or starts("1.15")
        or starts("1.16")
    ):
        return 8
    return 21


def launcher_filename(mc: str, loader: str, installer: str) -> str:
    return f"fabric-server-mc.{mc}-loader.{loader}-launcher.{installer}.jar"


def server_jar_url(mc: str, loader: str, installer: str) -> str:
    if not mc.strip() or not loader.strip() or not installer.strip():
        _fail("Fabric server jar URL requires game, loader, and installer versions.")
    return (
        f"{META_BASE}/v2/versions/loader/{_enc(mc)}/{_enc(loader)}/{_enc(installer)}/server/jar"
    )


def count_version_axes(url: str) -> int:
    try:
        path = url.split("://", 1)[-1]
        path = path.split("/", 1)[1] if "/" in path else ""
    except IndexError:
        return 0
    parts = [p for p in path.split("/") if p]
    try:
        loader_idx = parts.index("loader")
        server_idx = parts.index("server")
    except ValueError:
        return 0
    if server_idx != len(parts) - 2:
        return 0
    return server_idx - loader_idx - 1


def select_stable_installer(installers: list[dict]) -> dict | None:
    for item in installers:
        if not isinstance(item, dict):
            continue
        if item.get("stable") is True and str(item.get("version") or "").strip():
            return item
    return None


def select_stable_loader(loaders: list[dict]) -> dict | None:
    for item in loaders:
        if not isinstance(item, dict):
            continue
        loader = item.get("loader") or {}
        if not isinstance(loader, dict):
            continue
        if loader.get("stable") is True and str(loader.get("version") or "").strip():
            return item
    return None


def resolve(
    minecraft_version: str,
    loaders_data: Any,
    installers_data: Any,
    loader_version: str | None = None,
    installer_version: str | None = None,
) -> dict:
    mc = (minecraft_version or "").strip()
    if not mc:
        _fail("Minecraft version is required.")
    if not isinstance(loaders_data, list):
        _fail("unexpected Fabric loader-for-game JSON (expected array).")
    if not isinstance(installers_data, list):
        _fail("unexpected Fabric installer JSON (expected array).")

    loader_pin = (loader_version or "").strip() or None
    if loader_pin is None:
        stable = select_stable_loader(loaders_data)
        if stable is None:
            _fail(
                f"No stable Fabric loader for Minecraft {mc}. "
                "Unstable loaders are not installed automatically."
            )
        loader = str(stable["loader"]["version"]).strip()
    else:
        match = None
        for item in loaders_data:
            if not isinstance(item, dict):
                continue
            info = item.get("loader") or {}
            if isinstance(info, dict) and str(info.get("version") or "").strip() == loader_pin:
                match = item
                break
        if match is None:
            _fail(f"Fabric loader {loader_pin} is not valid for Minecraft {mc}.")
        loader = loader_pin

    installer_pin = (installer_version or "").strip() or None
    if installer_pin is None:
        stable_i = select_stable_installer(installers_data)
        if stable_i is None:
            _fail("No stable Fabric installer version is published.")
        installer = str(stable_i["version"]).strip()
    else:
        match_i = None
        for item in installers_data:
            if not isinstance(item, dict):
                continue
            if str(item.get("version") or "").strip() == installer_pin:
                match_i = item
                break
        if match_i is None:
            _fail(
                f"Fabric installer {installer_pin} was not found in "
                "meta.fabricmc.net installer list."
            )
        installer = installer_pin

    url = server_jar_url(mc, loader, installer)
    if not url.endswith("/server/jar") or count_version_axes(url) != 3:
        _fail("Refusing Fabric download URL that omits game, loader, or installer.")

    return {
        "minecraft_version": mc,
        "loader": "fabric",
        "loader_version": loader,
        "installer_version": installer,
        "filename": launcher_filename(mc, loader, installer),
        "download_url": url,
        "java_major": java_major_for_minecraft(mc),
        "hash_algorithm": HASH_ALGORITHM,
        "artifact_kind": ARTIFACT_KIND,
    }


def cmd_resolve(
    installer_path: str,
    loader_path: str,
    minecraft_version: str,
    loader_version: str | None,
    installer_version: str | None,
) -> None:
    doc = resolve(
        minecraft_version,
        _load_json(loader_path),
        _load_json(installer_path),
        loader_version,
        installer_version,
    )
    json.dump(doc, sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")


def cmd_self_test(fixtures_dir: str) -> None:
    fx = Path(fixtures_dir)
    if not fx.is_dir():
        _fail(f"fixtures dir missing: {fx}")

    installers = _load_json(str(fx / "fabric-meta-installer.json"))
    loaders = _load_json(str(fx / "fabric-meta-loader-1.21.8.json"))
    resolved = resolve("1.21.8", loaders, installers)
    assert resolved["loader_version"] == "0.17.2", resolved
    assert resolved["installer_version"] == "1.1.0", resolved
    assert resolved["filename"] == (
        "fabric-server-mc.1.21.8-loader.0.17.2-launcher.1.1.0.jar"
    )
    assert resolved["download_url"] == (
        "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/1.1.0/server/jar"
    )
    assert resolved["hash_algorithm"] == "none_published"
    assert resolved["artifact_kind"] == "launcher_jar"
    assert resolved["java_major"] == 21
    assert java_major_for_minecraft("26.1") == 25
    assert java_major_for_minecraft("26.2") == 25
    assert java_major_for_minecraft("1.20.1") == 17
    assert count_version_axes(resolved["download_url"]) == 3

    pinned = resolve("1.21.8", loaders, installers, loader_version="0.19.3")
    assert pinned["loader_version"] == "0.19.3"

    try:
        resolve("1.21.8", loaders, installers, loader_version="0.99.0")
        _fail("expected invalid loader pin to fail")
    except FabricError:
        pass

    empty = _load_json(str(fx / "fabric-meta-loader-unknown.json"))
    try:
        resolve("not-a-version", empty, installers)
        _fail("expected empty loader list to fail")
    except FabricError:
        pass

    two_axis = "https://meta.fabricmc.net/v2/versions/loader/1.21.8/0.17.2/server/jar"
    assert count_version_axes(two_axis) == 2

    print("fabric_meta self-test: ok", file=sys.stderr)


def main(argv: list[str] | None = None) -> None:
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(newline="\n")
        except (OSError, ValueError):
            pass
    p = argparse.ArgumentParser(prog="fabric_meta.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("resolve")
    r.add_argument("installer_json")
    r.add_argument("loader_json")
    r.add_argument("minecraft_version")
    r.add_argument("--loader-version", default="")
    r.add_argument("--installer-version", default="")

    t = sub.add_parser("self-test")
    t.add_argument("--fixtures", required=True)

    args = p.parse_args(argv)
    if args.cmd == "resolve":
        cmd_resolve(
            args.installer_json,
            args.loader_json,
            args.minecraft_version,
            args.loader_version or None,
            args.installer_version or None,
        )
    elif args.cmd == "self-test":
        cmd_self_test(args.fixtures)
    else:
        _fail(f"unknown command {args.cmd}")


if __name__ == "__main__":
    try:
        main()
    except FabricError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc
