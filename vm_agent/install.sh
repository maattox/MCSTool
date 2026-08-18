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

chmod 755 "${OPT}/bin/idle_watch.py" "${OPT}/bin/record_boot.py" "${OPT}/bin/graceful_stop.sh"
chmod 644 "${OPT}/lib/ledger.py" "${OPT}/lib/rcon_client.py"
[[ -f "${OPT}/lib/os_publish.py" ]] && chmod 644 "${OPT}/lib/os_publish.py"
[[ -f "${OPT}/lib/lease.py" ]] && chmod 644 "${OPT}/lib/lease.py"
[[ -f "${OPT}/lib/shape_detect.py" ]] && chmod 644 "${OPT}/lib/shape_detect.py"
[[ -f "${OPT}/lib/world_backup.py" ]] && chmod 644 "${OPT}/lib/world_backup.py"
# Normalize CRLF if files were uploaded from Windows
sed -i 's/\r$//' "${OPT}/bin/graceful_stop.sh" "${OPT}/install.sh" 2>/dev/null || true

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

systemctl daemon-reload
systemctl enable mc-idle-watch.timer
systemctl enable mc-boot-ledger.service
systemctl restart mc-idle-watch.timer
systemctl start mc-boot-ledger.service || true

echo "mc-manager idle agent installed."
systemctl is-active mc-idle-watch.timer || true
