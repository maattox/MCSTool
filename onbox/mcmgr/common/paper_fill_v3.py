#!/usr/bin/env python3
"""Fill v3 Paper metadata helper for the on-box installer (blueprint §17).

CLI (stdout = JSON object unless noted):
  resolve <builds.json> [version.json] <minecraft_version>
  default-version <project.json>
  self-test --fixtures <dir>

Never constructs api.papermc.io v2 download URLs. STABLE only — no ALPHA/BETA fallback.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any

STABLE = "STABLE"
SERVER_DEFAULT = "server:default"
LEGACY_V2_HOST = "api.papermc.io"
DEFAULT_JAVA_MAJOR = 21
DEFAULT_JVM_FLAGS = ["-XX:+UseG1GC"]


class FillError(Exception):
    """CLI and self-test use this instead of SystemExit so tests can catch quietly."""


def _fail(msg: str, code: int = 1) -> None:
    raise FillError(msg)


def _load_json(path: str) -> Any:
    with open(path, encoding="utf-8") as f:
        return json.load(f)


def is_error_payload(data: Any) -> bool:
    return isinstance(data, dict) and data.get("ok") is False


def error_message(data: dict) -> str:
    for key in ("message", "error"):
        val = data.get(key)
        if isinstance(val, str) and val.strip():
            return val.strip()
    return "Fill v3 error."


def flatten_version_ids(project: dict) -> list[str]:
    ids: list[str] = []
    versions = project.get("versions") or {}
    if not isinstance(versions, dict):
        return ids
    for family in versions.values():
        if not isinstance(family, list):
            continue
        for vid in family:
            if isinstance(vid, str) and vid.strip():
                ids.append(vid.strip())
    return ids


def default_version_id(project: dict) -> str:
    ids = flatten_version_ids(project)
    return ids[0] if ids else ""


def select_stable(builds: list[dict]) -> dict | None:
    stables = []
    for b in builds:
        if not isinstance(b, dict):
            continue
        channel = str(b.get("channel") or "")
        if channel.upper() != STABLE:
            continue
        try:
            bid = int(b.get("id"))
        except (TypeError, ValueError):
            continue
        stables.append((bid, b))
    if not stables:
        return None
    stables.sort(key=lambda t: t[0], reverse=True)
    return stables[0][1]


def contains_legacy_v2_host(url: str) -> bool:
    return LEGACY_V2_HOST in (url or "").lower()


def _java_and_flags(version_doc: dict | None) -> tuple[int, list[str]]:
    java_major = DEFAULT_JAVA_MAJOR
    flags = list(DEFAULT_JVM_FLAGS)
    if not isinstance(version_doc, dict) or is_error_payload(version_doc):
        return java_major, flags
    ver = version_doc.get("version") or {}
    if not isinstance(ver, dict):
        return java_major, flags
    java = ver.get("java") or {}
    if not isinstance(java, dict):
        return java_major, flags
    vmin = (java.get("version") or {}).get("minimum") if isinstance(java.get("version"), dict) else None
    if vmin is not None:
        java_major = int(vmin)
    rec = (java.get("flags") or {}).get("recommended") if isinstance(java.get("flags"), dict) else None
    if isinstance(rec, list) and rec:
        flags = [str(x) for x in rec if str(x).strip()]
        if not flags:
            flags = list(DEFAULT_JVM_FLAGS)
    return java_major, flags


def resolve_stable(
    minecraft_version: str,
    builds_data: Any,
    version_doc: dict | None = None,
) -> dict:
    mc = (minecraft_version or "").strip()
    if not mc:
        _fail("Minecraft version is required.")
    if is_error_payload(builds_data):
        _fail(error_message(builds_data))
    if not isinstance(builds_data, list):
        _fail("unexpected Fill v3 builds JSON (expected array).")

    stable = select_stable(builds_data)
    if stable is None:
        _fail(
            f"No STABLE Paper build for Minecraft {mc} yet. "
            "Unstable channels are not installed automatically."
        )

    downloads = stable.get("downloads") or {}
    if not isinstance(downloads, dict):
        downloads = {}
    dl = downloads.get(SERVER_DEFAULT)
    if not isinstance(dl, dict):
        _fail(
            f"STABLE Paper build {stable.get('id')} for {mc} is missing "
            f"downloads[\"{SERVER_DEFAULT}\"] url or sha256."
        )
    url = str(dl.get("url") or "").strip()
    checksums = dl.get("checksums") if isinstance(dl.get("checksums"), dict) else {}
    sha256 = str((checksums or {}).get("sha256") or "").strip()
    if not url or not sha256:
        _fail(
            f"STABLE Paper build {stable.get('id')} for {mc} is missing "
            f"downloads[\"{SERVER_DEFAULT}\"] url or sha256."
        )
    if contains_legacy_v2_host(url):
        _fail("Fill v2 (api.papermc.io) download URLs are not supported; use the URL from Fill v3 JSON.")

    name = str(dl.get("name") or "").strip() or f"paper-{mc}-{stable.get('id')}.jar"
    java_major, flags = _java_and_flags(version_doc)
    return {
        "minecraft_version": mc,
        "build_id": int(stable["id"]),
        "channel": str(stable.get("channel") or STABLE),
        "filename": name,
        "download_url": url,
        "sha256": sha256,
        "java_major": java_major,
        "jvm_flags": flags,
        "hash_algorithm": "sha256",
    }


def cmd_resolve(builds_path: str, version_path: str | None, minecraft_version: str) -> None:
    builds = _load_json(builds_path)
    version_doc = None
    if version_path:
        version_doc = _load_json(version_path)
        if is_error_payload(version_doc):
            version_doc = None
    doc = resolve_stable(minecraft_version, builds, version_doc)
    json.dump(doc, sys.stdout, separators=(",", ":"))
    sys.stdout.write("\n")


def cmd_default_version(project_path: str) -> None:
    project = _load_json(project_path)
    if is_error_payload(project):
        _fail(error_message(project))
    if not isinstance(project, dict):
        _fail("unexpected Fill v3 project JSON.")
    vid = default_version_id(project)
    if not vid:
        _fail("Fill v3 project JSON listed no version ids.")
    sys.stdout.write(vid + "\n")


def cmd_self_test(fixtures_dir: str) -> None:
    fx = Path(fixtures_dir)
    if not fx.is_dir():
        _fail(f"fixtures dir missing: {fx}")

    project = _load_json(str(fx / "paper-fill-v3-project.json"))
    ids = flatten_version_ids(project)
    assert ids[0] == "26.2", ids
    assert default_version_id(project) == "26.2"

    builds = _load_json(str(fx / "paper-fill-v3-builds-1.21.10.json"))
    version = _load_json(str(fx / "paper-fill-v3-version-1.21.10.json"))
    resolved = resolve_stable("1.21.10", builds, version)
    assert resolved["build_id"] == 130, resolved
    assert resolved["filename"] == "paper-1.21.10-130.jar"
    assert resolved["sha256"] == "158703f75a26f842ea656b3dc6d75bf3d1ec176b97a2c36384d0b80b3871af53"
    assert resolved["java_major"] == 21
    assert "-XX:+UseG1GC" in resolved["jvm_flags"]
    assert "-XX:+ParallelRefProcEnabled" in resolved["jvm_flags"]
    assert not contains_legacy_v2_host(resolved["download_url"])
    assert "fill-data.papermc.io" in resolved["download_url"]

    err = _load_json(str(fx / "paper-fill-v3-error.json"))
    try:
        resolve_stable("not-a-version", err)
        _fail("expected error payload to fail resolve")
    except FillError:
        pass

    no_stable = [
        {
            "id": 9,
            "channel": "ALPHA",
            "downloads": {
                "server:default": {
                    "name": "paper-x-9.jar",
                    "url": "https://fill-data.papermc.io/v1/objects/deadbeef/paper-x-9.jar",
                    "checksums": {"sha256": "deadbeef"},
                }
            },
        }
    ]
    try:
        resolve_stable("26.2-rc-2", no_stable)
        _fail("expected no-STABLE to fail")
    except FillError:
        pass

    v2 = [
        {
            "id": 1,
            "channel": "STABLE",
            "downloads": {
                "server:default": {
                    "name": "paper-1.21.10-1.jar",
                    "url": "https://api.papermc.io/v2/projects/paper/versions/1.21.10/builds/1/downloads/paper-1.21.10-1.jar",
                    "checksums": {"sha256": "abc"},
                }
            },
        }
    ]
    try:
        resolve_stable("1.21.10", v2)
        _fail("expected v2 URL to fail")
    except FillError:
        pass

    print("paper_fill_v3 self-test: ok", file=sys.stderr)


def main(argv: list[str] | None = None) -> None:
    if hasattr(sys.stdout, "reconfigure"):
        try:
            sys.stdout.reconfigure(newline="\n")
        except (OSError, ValueError):
            pass
    p = argparse.ArgumentParser(prog="paper_fill_v3.py")
    sub = p.add_subparsers(dest="cmd", required=True)

    r = sub.add_parser("resolve")
    r.add_argument("builds_json")
    r.add_argument("minecraft_version")
    r.add_argument("--version-json", default="")

    d = sub.add_parser("default-version")
    d.add_argument("project_json")

    t = sub.add_parser("self-test")
    t.add_argument("--fixtures", required=True)

    args = p.parse_args(argv)
    if args.cmd == "resolve":
        cmd_resolve(args.builds_json, args.version_json or None, args.minecraft_version)
    elif args.cmd == "default-version":
        cmd_default_version(args.project_json)
    elif args.cmd == "self-test":
        cmd_self_test(args.fixtures)
    else:
        _fail(f"unknown command {args.cmd}")


if __name__ == "__main__":
    try:
        main()
    except FillError as exc:
        print(str(exc), file=sys.stderr)
        raise SystemExit(1) from exc
