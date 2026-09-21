#!/usr/bin/env bash
# Pull the player-map tile tar from VM1 over the VCN (primary private IP).
# No-op when VM1 is down or the tree is unchanged. Never uses Object Storage.
set -euo pipefail

export HOME="${HOME:-/home/ubuntu}"
export PATH="/home/ubuntu/bin:/usr/local/bin:/usr/bin:/bin:${PATH:-}"

VM1_PRIVATE_IP="${VM1_PRIVATE_IP-}"
VM1_PRIVATE_IP="${VM1_PRIVATE_IP//$'\r'/}"
VM1_TILE_PORT="${VM1_TILE_PORT:-8765}"
VM1_TILE_PORT="${VM1_TILE_PORT//$'\r'/}"
MAP_ROOT="${PLAYER_MAP_ROOT:-/var/lib/mc-player-map}"
MAP_ROOT="${MAP_ROOT//$'\r'/}"
STAMP_DIR="${PLAYER_MAP_STAMP_DIR:-/var/lib/mccontrol}"
CAP_BYTES=$((2 * 1024 * 1024 * 1024))

: "${VM1_PRIVATE_IP:?VM1_PRIVATE_IP must be set}"

URL="http://${VM1_PRIVATE_IP}:${VM1_TILE_PORT}"
STAMP="${STAMP_DIR}/player-map.sha256"
mkdir -p "$STAMP_DIR" "$MAP_ROOT"

manifest="$(curl -fsS --max-time 8 "${URL}/manifest.json" || true)"
if [[ -z "$manifest" ]]; then
  echo "player-map pull: VM1 :${VM1_TILE_PORT} not reachable; keeping last tiles"
  exit 0
fi

sha="$(python3 -c 'import json,sys; print(json.load(sys.stdin).get("sha256") or "")' <<<"$manifest")"
bytes="$(python3 -c 'import json,sys; print(int(json.load(sys.stdin).get("bytes") or 0))' <<<"$manifest")"
if [[ -z "$sha" || "$bytes" -le 0 ]]; then
  echo "player-map pull: empty manifest; skip"
  exit 0
fi
if [[ "$bytes" -gt "$CAP_BYTES" ]]; then
  echo "player-map pull: refuse tiles.tar ${bytes} bytes (2 GiB cap)" >&2
  exit 0
fi
if [[ -f "$STAMP" ]] && [[ "$(tr -d '[:space:]' < "$STAMP")" == "$sha" ]]; then
  echo "player-map pull: unchanged ${sha:0:12}…"
  exit 0
fi

work="$(mktemp -d /tmp/mc-player-map.XXXXXX)"
cleanup() { rm -rf "$work"; }
trap cleanup EXIT

if ! curl -fsS --max-time 120 -o "${work}/tiles.tar" "${URL}/tiles.tar"; then
  echo "player-map pull: tiles.tar download failed; keeping last tiles" >&2
  exit 0
fi
got_bytes="$(wc -c < "${work}/tiles.tar" | tr -d ' ')"
if [[ "$got_bytes" -gt "$CAP_BYTES" ]]; then
  echo "player-map pull: refuse downloaded ${got_bytes} bytes (2 GiB cap)" >&2
  exit 0
fi
got_sha="$(sha256sum "${work}/tiles.tar" | awk '{print $1}')"
if [[ "$got_sha" != "$sha" ]]; then
  echo "player-map pull: sha256 mismatch (manifest ${sha} vs ${got_sha})" >&2
  exit 0
fi

mkdir -p "${work}/tree"
if ! tar -C "${work}/tree" -xf "${work}/tiles.tar"; then
  echo "player-map pull: tar extract failed; keeping last tiles" >&2
  exit 0
fi
# Never serve MinedMap incremental cache.
rm -rf "${work}/tree/processed" "${work}/tree/"*/data/processed
tree_bytes="$(du -sb "${work}/tree" | awk '{print $1}')"
if [[ "$tree_bytes" -gt "$CAP_BYTES" ]]; then
  echo "player-map pull: refuse extracted ${tree_bytes} bytes (2 GiB cap)" >&2
  exit 0
fi
if [[ ! -f "${work}/tree/index.html" ]]; then
  echo "player-map pull: tar has no index.html; keeping last tiles" >&2
  exit 0
fi

next="${MAP_ROOT}.next"
prev="${MAP_ROOT}.prev"
rm -rf "$next" "$prev"
mv "${work}/tree" "$next"
if [[ -d "$MAP_ROOT" ]]; then
  mv "$MAP_ROOT" "$prev"
fi
mv "$next" "$MAP_ROOT"
rm -rf "$prev"
printf '%s\n' "$sha" > "$STAMP"
echo "player-map pull: installed ${sha:0:12}… (${tree_bytes} bytes)"
exit 0
