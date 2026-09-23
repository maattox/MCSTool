#!/usr/bin/env bash
# Install / update mc-manager idle agent on the A1 VM.
set -euo pipefail

OPT=/opt/mc-manager
ETC=/etc/mc-manager
VAR=/var/lib/mc-manager

mkdir -p "${OPT}/bin" "${OPT}/lib" "${ETC}" "${VAR}"

# Layout from /opt/mc-manager after deploy copies tmp files here
if [[ -f "${OPT}/idle_watch.py" ]]; then
  mv -f "${OPT}/idle_watch.py" "${OPT}/bin/idle_watch.py"
fi
if [[ -f "${OPT}/record_boot.py" ]]; then
  mv -f "${OPT}/record_boot.py" "${OPT}/bin/record_boot.py"
fi
if [[ -f "${OPT}/graceful_stop.sh" ]]; then
  mv -f "${OPT}/graceful_stop.sh" "${OPT}/bin/graceful_stop.sh"
fi
if [[ -f "${OPT}/ledger.py" ]]; then
  mv -f "${OPT}/ledger.py" "${OPT}/lib/ledger.py"
fi
if [[ -f "${OPT}/rcon_client.py" ]]; then
  mv -f "${OPT}/rcon_client.py" "${OPT}/lib/rcon_client.py"
fi
if [[ -f "${OPT}/os_publish.py" ]]; then
  mv -f "${OPT}/os_publish.py" "${OPT}/lib/os_publish.py"
fi
if [[ -f "${OPT}/lease.py" ]]; then
  mv -f "${OPT}/lease.py" "${OPT}/lib/lease.py"
fi
if [[ -f "${OPT}/shape_detect.py" ]]; then
  mv -f "${OPT}/shape_detect.py" "${OPT}/lib/shape_detect.py"
fi
if [[ -f "${OPT}/world_backup.py" ]]; then
  mv -f "${OPT}/world_backup.py" "${OPT}/lib/world_backup.py"
fi
if [[ -f "${OPT}/heap_clamp.py" ]]; then
  mv -f "${OPT}/heap_clamp.py" "${OPT}/lib/heap_clamp.py"
fi
if [[ -f "${OPT}/heap_pressure.py" ]]; then
  mv -f "${OPT}/heap_pressure.py" "${OPT}/lib/heap_pressure.py"
fi
if [[ -f "${OPT}/player_map.py" ]]; then
  mv -f "${OPT}/player_map.py" "${OPT}/bin/player_map.py"
fi
if [[ -f "${OPT}/map_http.py" ]]; then
  mv -f "${OPT}/map_http.py" "${OPT}/bin/map_http.py"
fi
if [[ -f "${OPT}/apply-jvm-heap.py" ]]; then
  mv -f "${OPT}/apply-jvm-heap.py" "${OPT}/bin/apply-jvm-heap.py"
fi

chmod 755 "${OPT}/bin/idle_watch.py" "${OPT}/bin/record_boot.py" "${OPT}/bin/graceful_stop.sh"
chmod 644 "${OPT}/lib/ledger.py" "${OPT}/lib/rcon_client.py"
[[ -f "${OPT}/lib/os_publish.py" ]] && chmod 644 "${OPT}/lib/os_publish.py"
[[ -f "${OPT}/lib/lease.py" ]] && chmod 644 "${OPT}/lib/lease.py"
[[ -f "${OPT}/lib/shape_detect.py" ]] && chmod 644 "${OPT}/lib/shape_detect.py"
[[ -f "${OPT}/lib/world_backup.py" ]] && chmod 644 "${OPT}/lib/world_backup.py"
[[ -f "${OPT}/lib/heap_clamp.py" ]] && chmod 644 "${OPT}/lib/heap_clamp.py"
[[ -f "${OPT}/lib/heap_pressure.py" ]] && chmod 644 "${OPT}/lib/heap_pressure.py"
[[ -f "${OPT}/bin/player_map.py" ]] && chmod 755 "${OPT}/bin/player_map.py"
[[ -f "${OPT}/bin/map_http.py" ]] && chmod 755 "${OPT}/bin/map_http.py"
[[ -f "${OPT}/bin/apply-jvm-heap.py" ]] && chmod 755 "${OPT}/bin/apply-jvm-heap.py"
# Normalize CRLF if files were uploaded from Windows
sed -i 's/\r$//' "${OPT}/bin/graceful_stop.sh" "${OPT}/install.sh" 2>/dev/null || true
sed -i 's/\r$//' "${OPT}/bin/"*.py "${OPT}/lib/"*.py 2>/dev/null || true

# Python venv + oci SDK (for instance principal stop / Object Storage publish)
if [[ ! -x "${OPT}/venv/bin/python" ]]; then
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq python3-venv python3-pip
  python3 -m venv "${OPT}/venv"
  "${OPT}/venv/bin/pip" install -q --upgrade pip
  "${OPT}/venv/bin/pip" install -q "oci>=2.126.0"
elif ! "${OPT}/venv/bin/python" -c "import oci" >/dev/null 2>&1; then
  "${OPT}/venv/bin/pip" install -q --upgrade pip
  "${OPT}/venv/bin/pip" install -q "oci>=2.126.0"
fi

# Systemd units
if [[ -f "${OPT}/mc-idle-watch.service" ]]; then
  cp -f "${OPT}/mc-idle-watch.service" /etc/systemd/system/mc-idle-watch.service
fi
if [[ -f "${OPT}/mc-idle-watch.timer" ]]; then
  cp -f "${OPT}/mc-idle-watch.timer" /etc/systemd/system/mc-idle-watch.timer
fi
if [[ -f "${OPT}/mc-boot-ledger.service" ]]; then
  cp -f "${OPT}/mc-boot-ledger.service" /etc/systemd/system/mc-boot-ledger.service
fi
if [[ -f "${OPT}/mc-player-map.service" ]]; then
  cp -f "${OPT}/mc-player-map.service" /etc/systemd/system/mc-player-map.service
fi
if [[ -f "${OPT}/mc-player-map.timer" ]]; then
  cp -f "${OPT}/mc-player-map.timer" /etc/systemd/system/mc-player-map.timer
fi
if [[ -f "${OPT}/mc-map-http.service" ]]; then
  cp -f "${OPT}/mc-map-http.service" /etc/systemd/system/mc-map-http.service
fi

# Existing VMs keep a generated minecraft.service; the drop-in makes Java wait
# for identity apply without rewriting ExecStart. Greenfield template has the
# same After=/Wants= (onbox/mcmgr/templates/minecraft.service.in).
mkdir -p /etc/systemd/system/minecraft.service.d
cat > /etc/systemd/system/minecraft.service.d/mcmgr-identity.conf <<'EOF'
[Unit]
After=mc-boot-ledger.service
Requires=mc-boot-ledger.service
EOF
# Existing VMs keep ProtectSystem=strict from the generated unit; ImageIO needs a
# writable /tmp to encode server-icon.png into the status ping favicon.
cat > /etc/systemd/system/minecraft.service.d/mcmgr-private-tmp.conf <<'EOF'
[Service]
PrivateTmp=true
EOF

# Ensure config exists
if [[ ! -f "${ETC}/config.json" ]]; then
  if [[ -f "${OPT}/config.example.json" ]]; then
    cp "${OPT}/config.example.json" "${ETC}/config.json"
  else
    echo '{}' > "${ETC}/config.json"
  fi
  chmod 600 "${ETC}/config.json"
fi

# Ledger readable for pull via ubuntu SSH cat (may need sudo - deploy uses cat as ubuntu)
touch "${VAR}/usage.json"
touch "${VAR}/lease.json"
chmod 644 "${VAR}/usage.json" "${VAR}/lease.json"
if [[ ! -s "${VAR}/usage.json" ]]; then
  echo '{"version":2,"revision":0,"intervals":[],"idle_since":null,"last_budget_warn_at":null}' > "${VAR}/usage.json"
fi
if [[ ! -s "${VAR}/lease.json" ]]; then
  echo '{"version":1,"active":false,"session_id":null,"interval_id":null,"started_at":null,"last_heartbeat_at":null,"ocpus":null,"memory_gb":null,"updated_at":null,"cleared_at":null,"clear_reason":null}' > "${VAR}/lease.json"
fi

# Pinned MinedMap v2.8.0 aarch64 ELF + viewer (SHA in docs/archive/Player-Map.md).
MAP=/var/lib/mcmgr-map
mkdir -p "${MAP}/http" "${MAP}/render" "${MAP}/publish" "${MAP}/shim" /opt/mcmgr/bin
VENDOR="${OPT}/vendor"
ELF="${VENDOR}/minedmap-aarch64"
VIEWER_ZIP="${VENDOR}/MinedMap-2.8.0-viewer.zip"
ELF_SHA="00295e590af406d6ca5a1e7054f112cf3678417e0a6b2942a218ed4c6c526604"
VIEWER_SHA="6ddfe50714a0ab0c190ded3bd968fe3b6d919fd062fcb1b830201351357c37db"
if [[ ! -f "${ELF}" ]]; then
  echo "missing vendored MinedMap ELF at ${ELF}" >&2
  exit 1
fi
if [[ ! -f "${VIEWER_ZIP}" ]]; then
  echo "missing MinedMap viewer zip at ${VIEWER_ZIP}" >&2
  exit 1
fi
command -v unzip >/dev/null 2>&1 || {
  apt-get update -qq
  DEBIAN_FRONTEND=noninteractive apt-get install -y -qq unzip
}
echo "${ELF_SHA}  ${ELF}" | sha256sum -c -
echo "${VIEWER_SHA}  ${VIEWER_ZIP}" | sha256sum -c -
install -m 755 "${ELF}" /opt/mcmgr/bin/minedmap
rm -rf /tmp/mm-viewer-unpack
mkdir -p /tmp/mm-viewer-unpack
unzip -qo "${VIEWER_ZIP}" -d /tmp/mm-viewer-unpack
VIEWER_SRC="$(find /tmp/mm-viewer-unpack -name index.html -printf '%h\n' | head -n 1)"
if [[ -z "${VIEWER_SRC}" ]]; then
  echo "MinedMap viewer zip has no index.html" >&2
  exit 1
fi
rm -rf "${MAP}/viewer"
mkdir -p "${MAP}/viewer"
cp -a "${VIEWER_SRC}/." "${MAP}/viewer/"
rm -rf /tmp/mm-viewer-unpack
if [[ ! -f "${MAP}/http/manifest.json" ]]; then
  printf '%s\n' '{"sha256":"","bytes":0,"rendered_at":null}' > "${MAP}/http/manifest.json"
fi
# Subnet-only tile port (PlayerMapSync.TilePort / DefaultSubnetCidr). Never world-open.
if command -v firewall-cmd >/dev/null 2>&1 && systemctl is-active --quiet firewalld; then
  TILE_RULE='rule family=ipv4 source address=10.0.0.0/24 port port=8765 protocol=tcp accept'
  firewall-cmd --permanent --query-rich-rule="${TILE_RULE}" >/dev/null 2>&1 \
    || firewall-cmd --permanent --add-rich-rule="${TILE_RULE}"
  firewall-cmd --reload
fi

systemctl daemon-reload
systemctl enable mc-idle-watch.timer
systemctl enable mc-boot-ledger.service
systemctl enable mc-player-map.timer
systemctl enable mc-map-http.service
systemctl restart mc-idle-watch.timer
systemctl start mc-boot-ledger.service || true
systemctl restart mc-map-http.service
systemctl restart mc-player-map.timer

echo "mc-manager idle agent installed."
systemctl is-active mc-idle-watch.timer || true
systemctl is-active mc-map-http.service || true
systemctl is-active mc-player-map.timer || true
