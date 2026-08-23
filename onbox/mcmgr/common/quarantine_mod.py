#!/usr/bin/env python3
"""Layer 3 crash quarantine (blueprint §24.3). Never deletes jars. Never folds
into excluded_client_only_files. Paths default to live /opt/mcmgr; MCMGR_ROOT
or --server-dir/--manifest for dry-run.
"""
from __future__ import annotations

import argparse
import datetime
import json
import os
import re
import subprocess
import sys
import zipfile
from pathlib import Path

REASON = "crash_attributed_by_loader_report"
UNIT = os.environ.get("MINECRAFT_UNIT", "minecraft")


def _now() -> str:
    return datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def _fail(msg: str, code: int = 1) -> int:
    print(json.dumps({"ok": False, "error": msg}), flush=True)
    return code


def _ok(payload: dict) -> int:
    payload = dict(payload)
    payload["ok"] = True
    print(json.dumps(payload), flush=True)
    return 0


def _is_alpha(ch: str) -> bool:
    return "a" <= ch <= "z" or "A" <= ch <= "Z"


def _contains_as_token(haystack: str, needle: str) -> bool:
    if not haystack or not needle or len(needle) > len(haystack):
        return False
    start = 0
    while start <= len(haystack) - len(needle):
        idx = haystack.find(needle, start)
        if idx < 0:
            return False
        if idx == 0 or not _is_alpha(haystack[idx - 1]):
            return True
        start = idx + 1
    return False


def _collapse(value: str) -> str:
    return value.replace(" ", "").replace("-", "").replace("_", "")


def _term_matches(term: str, file_name: str) -> bool:
    needle = term.strip().lower()
    if not needle:
        return False
    path_lower = ("mods/" + file_name).lower()
    file_lower = file_name.lower()
    if _contains_as_token(path_lower, needle) or _contains_as_token(file_lower, needle):
        return True
    collapsed_needle = _collapse(needle)
    if not collapsed_needle:
        return False
    return _contains_as_token(_collapse(path_lower), collapsed_needle) or _contains_as_token(
        _collapse(file_lower), collapsed_needle
    )


def _safe_name(name: str) -> bool:
    if not name or len(name) > 240:
        return False
    if "/" in name or "\\" in name or ".." in name:
        return False
    if any(ch in name for ch in (";", "|", "&", "$", "`", "\n", "\r", "\0")):
        return False
    return True


def _find_unique_jar(mods_dir: Path, mod_id: str, jar_name: str | None) -> Path | None:
    names = [p.name for p in mods_dir.iterdir() if p.is_file() and p.suffix.lower() == ".jar"]
    if jar_name:
        if not _safe_name(jar_name):
            return None
        for name in names:
            if name.lower() == jar_name.lower():
                return mods_dir / name
    hits = [name for name in names if _safe_name(name) and _term_matches(mod_id, name)]
    if len(hits) != 1:
        return None
    return mods_dir / hits[0]


def _likely_client_only(jar: Path) -> bool:
    try:
        with zipfile.ZipFile(jar) as zf:
            names = zf.namelist()
            for name in names:
                lower = name.replace("\\", "/").lower()
                if lower.endswith("fabric.mod.json"):
                    data = json.loads(zf.read(name).decode("utf-8", "replace"))
                    env = data.get("environment")
                    if isinstance(env, str) and env.strip().lower() == "client":
                        return True
                    eps = data.get("entrypoints")
                    if isinstance(eps, dict) and eps:
                        keys = {k.lower() for k in eps}
                        if keys and keys.issubset({"client", "client_init"}) and "main" not in keys and "server" not in keys:
                            return True
                if lower.endswith("mods.toml") or lower.endswith("neoforge.mods.toml"):
                    text = zf.read(name).decode("utf-8", "replace")
                    compact = re.sub(r"\s+", "", text)
                    if "clientSideOnly=true" in compact or 'side="CLIENT"' in text or "side='CLIENT'" in text:
                        return True
                    if re.search(r"(?im)^side\s*=\s*CLIENT\b", text):
                        return True
    except (OSError, zipfile.BadZipFile, json.JSONDecodeError, UnicodeError):
        return False
    return False


def _load_manifest(path: Path) -> dict:
    if not path.is_file():
        raise FileNotFoundError(f"missing manifest {path}")
    with path.open(encoding="utf-8") as f:
        doc = json.load(f)
    if not isinstance(doc, dict):
        raise ValueError("manifest is not an object")
    return doc


def _ensure_modpack(doc: dict) -> dict:
    pack = doc.get("modpack")
    if not isinstance(pack, dict):
        pack = {
            "source": "manual_upload",
            "project_id": None,
            "version_id": None,
            "pack_name": "",
            "pack_version_label": None,
            "client_pack_required": True,
            "excluded_client_only_files": [],
            "quarantined_files": [],
            "imported_at": _now(),
        }
        doc["modpack"] = pack
    pack.setdefault("quarantined_files", [])
    if not isinstance(pack["quarantined_files"], list):
        pack["quarantined_files"] = []
    # Never silently copy into excluded_client_only_files.
    pack.setdefault("excluded_client_only_files", pack.get("excluded_client_only_files") or [])
    return pack


def _write_manifest(path: Path, doc: dict) -> None:
    tmp = path.with_suffix(path.suffix + ".tmp")
    with tmp.open("w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    tmp.replace(path)
    if os.environ.get("DRY_RUN") != "1":
        try:
            os.chmod(path, 0o640)
        except OSError:
            pass


def _entry_path(rel: str) -> str:
    n = rel.replace("\\", "/").lstrip("/")
    if n.startswith("mods.quarantined/"):
        n = "mods/" + n[len("mods.quarantined/") :]
    if not n.startswith("mods/") and n.endswith(".jar") and "/" not in n:
        n = "mods/" + n
    return n


def _upsert_entry(pack: dict, rel_path: str, retry_succeeded: bool | None = None, acknowledged: bool | None = None) -> dict:
    rel = _entry_path(rel_path)
    entries = pack["quarantined_files"]
    for item in entries:
        if isinstance(item, dict) and _entry_path(str(item.get("path", ""))) == rel:
            if retry_succeeded is not None:
                item["retry_succeeded"] = bool(retry_succeeded)
            if acknowledged is not None:
                item["operator_acknowledged"] = bool(acknowledged)
            return item
    entry = {
        "path": rel,
        "reason": REASON,
        "detected_at": _now(),
        "retry_succeeded": bool(retry_succeeded) if retry_succeeded is not None else False,
        "operator_acknowledged": bool(acknowledged) if acknowledged is not None else False,
    }
    entries.append(entry)
    return entry


def _remove_entry(pack: dict, rel_path: str) -> None:
    rel = _entry_path(rel_path)
    pack["quarantined_files"] = [
        item
        for item in pack["quarantined_files"]
        if not (isinstance(item, dict) and _entry_path(str(item.get("path", ""))) == rel)
    ]


def _systemctl(action: str) -> None:
    if os.environ.get("DRY_RUN") == "1":
        return
    subprocess.run(["systemctl", action, UNIT], check=False, capture_output=True)
    if action == "stop":
        subprocess.run(["systemctl", "reset-failed", UNIT], check=False, capture_output=True)


def _chown_mcmgr(path: Path, mode: int) -> None:
    if os.environ.get("DRY_RUN") == "1":
        return
    try:
        os.chmod(path, mode)
    except OSError:
        pass
    try:
        import pwd
        import grp

        uid = pwd.getpwnam("mcmgr").pw_uid
        gid = grp.getgrnam("mcmgr").gr_gid
        os.chown(path, uid, gid)
    except (KeyError, ImportError, OSError, PermissionError):
        pass


def cmd_move(args: argparse.Namespace) -> int:
    mods = args.server_dir / "mods"
    if not mods.is_dir():
        return _fail(f"mods directory missing: {mods}")
    jar = _find_unique_jar(mods, args.mod_id or "", args.jar_name)
    if jar is None:
        return _fail("Could not match exactly one jar for the blamed mod.")
    likely = _likely_client_only(jar)
    quarantined = args.server_dir / "mods.quarantined"
    quarantined.mkdir(parents=True, exist_ok=True)
    _chown_mcmgr(quarantined, 0o750)
    dest = quarantined / jar.name
    if args.restart:
        _systemctl("stop")
    jar.replace(dest)
    _chown_mcmgr(dest, 0o640)
    rel = "mods/" + jar.name
    doc = _load_manifest(args.manifest)
    pack = _ensure_modpack(doc)
    _upsert_entry(pack, rel, retry_succeeded=False, acknowledged=False)
    _write_manifest(args.manifest, doc)
    if args.restart:
        _systemctl("start")
    return _ok(
        {
            "mod_id": args.mod_id or jar.stem,
            "path": rel,
            "moved_to": "mods.quarantined/" + dest.name,
            "likely_client_only": likely,
        }
    )


def cmd_restore(args: argparse.Namespace) -> int:
    rel = _entry_path(args.path or "")
    name = Path(rel).name
    if not _safe_name(name):
        return _fail("Unsafe jar path.")
    src = args.server_dir / "mods.quarantined" / name
    dest_dir = args.server_dir / "mods"
    dest_dir.mkdir(parents=True, exist_ok=True)
    dest = dest_dir / name
    if not src.is_file():
        return _fail(f"Quarantined jar not found: {src}")
    if args.restart:
        _systemctl("stop")
    src.replace(dest)
    _chown_mcmgr(dest, 0o640)
    doc = _load_manifest(args.manifest)
    pack = _ensure_modpack(doc)
    _remove_entry(pack, rel)
    _write_manifest(args.manifest, doc)
    if args.restart:
        _systemctl("start")
    return _ok({"path": rel, "moved_to": "mods/" + name})


def cmd_ack(args: argparse.Namespace) -> int:
    rel = _entry_path(args.path or "")
    doc = _load_manifest(args.manifest)
    pack = _ensure_modpack(doc)
    _upsert_entry(pack, rel, acknowledged=True)
    _write_manifest(args.manifest, doc)
    return _ok({"path": rel, "operator_acknowledged": True})


def cmd_set_retry(args: argparse.Namespace) -> int:
    rel = _entry_path(args.path or "")
    doc = _load_manifest(args.manifest)
    pack = _ensure_modpack(doc)
    _upsert_entry(pack, rel, retry_succeeded=bool(args.succeeded))
    _write_manifest(args.manifest, doc)
    return _ok({"path": rel, "retry_succeeded": bool(args.succeeded)})


def cmd_read_crash(args: argparse.Namespace) -> int:
    reports = args.server_dir / "crash-reports"
    files: list[Path] = []
    if reports.is_dir():
        files = sorted(reports.glob("crash-*.txt"), key=lambda p: p.stat().st_mtime, reverse=True)
    if not files:
        return _ok({"crash_report": ""})
    text = files[0].read_text(encoding="utf-8", errors="replace")
    if len(text) > 120_000:
        text = text[-120_000:]
    return _ok({"crash_report": text, "crash_report_name": files[0].name})


def cmd_self_test(args: argparse.Namespace) -> int:
    root = Path(args.self_test_root)
    server = root / "opt" / "mcmgr" / "server"
    mods = server / "mods"
    mods.mkdir(parents=True)
    (mods / "goodmod-1.0.jar").write_bytes(b"PK\x05\x06" + b"\x00" * 18)
    (mods / "badmod-1.2.3.jar").write_bytes(b"PK\x05\x06" + b"\x00" * 18)
    etc = root / "etc" / "mcmgr"
    etc.mkdir(parents=True)
    manifest = etc / "game-manifest.json"
    manifest.write_text(json.dumps({"schema_version": 1, "modpack": None}) + "\n", encoding="utf-8")
    ns = argparse.Namespace(
        server_dir=server,
        manifest=manifest,
        mod_id="badmod",
        jar_name=None,
        restart=False,
        path=None,
        succeeded=False,
    )
    os.environ["DRY_RUN"] = "1"
    rc = cmd_move(ns)
    if rc != 0:
        return rc
    if (mods / "badmod-1.2.3.jar").exists():
        return _fail("self-test: jar was not moved")
    q = server / "mods.quarantined" / "badmod-1.2.3.jar"
    if not q.is_file():
        return _fail("self-test: quarantined jar missing")
    doc = json.loads(manifest.read_text(encoding="utf-8"))
    entries = doc["modpack"]["quarantined_files"]
    if len(entries) != 1 or entries[0]["path"] != "mods/badmod-1.2.3.jar":
        return _fail("self-test: manifest entry missing")
    if "excluded_client_only_files" in doc["modpack"] and entries[0]["path"] in (
        doc["modpack"].get("excluded_client_only_files") or []
    ):
        return _fail("self-test: folded into excluded_client_only_files")
    ns.path = "mods/badmod-1.2.3.jar"
    ns.succeeded = True
    cmd_set_retry(ns)
    ns.restart = False
    cmd_restore(ns)
    if not (mods / "badmod-1.2.3.jar").is_file():
        return _fail("self-test: restore failed")
    doc = json.loads(manifest.read_text(encoding="utf-8"))
    if doc["modpack"]["quarantined_files"]:
        return _fail("self-test: entry not cleared")
    return _ok({"self_test": True})


def _paths(args: argparse.Namespace) -> None:
    root = os.environ.get("MCMGR_ROOT") or ""
    server = args.server_dir
    manifest = args.manifest
    if server is None:
        server = Path(root + "/opt/mcmgr/server") if root else Path("/opt/mcmgr/server")
    if manifest is None:
        manifest = Path(root + "/etc/mcmgr/game-manifest.json") if root else Path("/etc/mcmgr/game-manifest.json")
    args.server_dir = Path(server)
    args.manifest = Path(manifest)


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Layer 3 crash quarantine")
    parser.add_argument("action", choices=["move", "restore", "ack", "set-retry", "read-crash", "self-test"])
    parser.add_argument("--mod-id", default="")
    parser.add_argument("--jar-name", default="")
    parser.add_argument("--path", default="")
    parser.add_argument("--server-dir", default=None)
    parser.add_argument("--manifest", default=None)
    parser.add_argument("--restart", action="store_true")
    parser.add_argument("--succeeded", action="store_true")
    parser.add_argument("--self-test-root", default="")
    args = parser.parse_args(argv)
    if args.action == "self-test":
        root = args.self_test_root or os.environ.get("MCMGR_SELF_TEST_ROOT") or ""
        if not root:
            import tempfile

            with tempfile.TemporaryDirectory(prefix="mcmgr-q-") as tmp:
                args.self_test_root = tmp
                return cmd_self_test(args)
        args.self_test_root = root
        return cmd_self_test(args)
    _paths(args)
    if args.action == "move":
        if not (args.mod_id or args.jar_name):
            return _fail("move needs --mod-id or --jar-name")
        return cmd_move(args)
    if args.action == "restore":
        if not args.path:
            return _fail("restore needs --path")
        return cmd_restore(args)
    if args.action == "ack":
        if not args.path:
            return _fail("ack needs --path")
        return cmd_ack(args)
    if args.action == "set-retry":
        if not args.path:
            return _fail("set-retry needs --path")
        return cmd_set_retry(args)
    if args.action == "read-crash":
        return cmd_read_crash(args)
    return _fail(f"unknown action {args.action}")


if __name__ == "__main__":
    sys.exit(main())
