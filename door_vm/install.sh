#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_VM2="$SCRIPT_DIR"

YES=0
SKIP_FIREWALL=0
NO_START=0
SKIP_OCI_CLI=0

usage() {
  cat <<'EOF'
Usage: bash vm2/install.sh [options]

  --yes             Non-interactive (require env file / existing /etc values)
  --skip-firewall   Do not modify iptables
  --no-start        Install/configure only; do not enable/start systemd unit
  --skip-oci-cli    Do not auto-install OCI CLI if missing
  -h, --help        Show this help
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --yes) YES=1 ;;
    --skip-firewall) SKIP_FIREWALL=1 ;;
    --no-start) NO_START=1 ;;
    --skip-oci-cli) SKIP_OCI_CLI=1 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
  shift
done

log() { printf '%s\n' "$*" >&2; }
die() { printf 'ERROR: %s\n' "$*" >&2; exit 1; }

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "missing required command: $1"
}

# Install stages
stage_deps() {
  log "==> Installing packages"
  sudo apt-get update
  # DEBIAN_FRONTEND avoids iptables-persistent interactive prompts
  sudo DEBIAN_FRONTEND=noninteractive apt-get install -y \
    build-essential git curl libssl-dev libffi-dev python3-dev \
    iptables-persistent
}

stage_oci_cli() {
  export PATH="${HOME}/bin:${PATH}"
  if command -v oci >/dev/null 2>&1; then
    log "==> OCI CLI already present: $(oci -v 2>&1 | head -1)"
    return 0
  fi
  if [[ "$SKIP_OCI_CLI" -eq 1 ]]; then
    die "oci not on PATH and --skip-oci-cli was set"
  fi
  log "==> Installing OCI CLI (Oracle installer, accept-all-defaults)"
  local tmp
  tmp="$(mktemp)"
  curl -fsSL -o "$tmp" https://raw.githubusercontent.com/oracle/oci-cli/master/scripts/install/install.sh
  bash "$tmp" --accept-all-defaults
  rm -f "$tmp"
  export PATH="${HOME}/bin:${PATH}"
  command -v oci >/dev/null 2>&1 || die "OCI CLI install finished but oci not on PATH"
  log "==> OCI CLI: $(oci -v 2>&1 | head -1)"
}
stage_build() {
  log "==> Building mccontrol"
  need_cmd make
  need_cmd gcc
  (
    cd "$REPO_VM2"
    make clean
    make mccontrol
  )
  local bin="$REPO_VM2/build/mccontrol"
  [[ -f "$bin" ]] || die "build/mccontrol missing after make"
  [[ -x "$bin" ]] || die "build/mccontrol is not executable"
  if command -v file >/dev/null 2>&1; then
    file "$bin" | grep -qi 'ELF' || die "build/mccontrol is not an ELF binary (got: $(file "$bin"))"
  fi
  log "==> Built $bin"
}

stage_install_files() {
  log "==> Installing files to /opt/mccontrol and unit"
  sudo mkdir -p /opt/mccontrol/build /opt/mccontrol/scripts /etc/mccontrol /var/lib/mccontrol
  sudo cp "$REPO_VM2/build/mccontrol" /opt/mccontrol/build/mccontrol
  sudo cp -a "$REPO_VM2/oci" /opt/mccontrol/
  sudo cp -a "$REPO_VM2/scripts/." /opt/mccontrol/scripts/
  sudo cp -a "$REPO_VM2/web" /opt/mccontrol/
  sudo cp -a "$REPO_VM2/assets" /opt/mccontrol/
  # Strip ALL CR bytes (WinSCP). sed 's/\r$//' alone is not reliable.
  sudo python3 - <<'PY'
from pathlib import Path
import sys
roots = [Path("/opt/mccontrol/oci"), Path("/opt/mccontrol/scripts")]
bad = []
n = 0
for root in roots:
    if not root.is_dir():
        continue
    for p in sorted(root.glob("*.sh")):
        data = p.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n")
        p.write_bytes(data)
        p.chmod(p.stat().st_mode | 0o111)
        n += 1
        if b"\r" in p.read_bytes():
            bad.append(str(p))
if bad:
    print("CRLF still present after strip:", ", ".join(bad), file=sys.stderr)
    sys.exit(1)
print("normalized", n, "oci/scripts shell files (LF)")
PY
  sudo chmod +x /opt/mccontrol/build/mccontrol /opt/mccontrol/oci/*.sh /opt/mccontrol/scripts/*.sh
  sudo cp "$REPO_VM2/mccontrol.service" /etc/systemd/system/mccontrol.service
  if [[ -f "$REPO_VM2/systemd/mccontrol-reconcile.service" ]]; then
    sudo cp "$REPO_VM2/systemd/mccontrol-reconcile.service" /etc/systemd/system/mccontrol-reconcile.service
    sudo cp "$REPO_VM2/systemd/mccontrol-reconcile.timer" /etc/systemd/system/mccontrol-reconcile.timer
  fi
}
REQUIRED_OCI_KEYS=(INSTANCE_ID RESERVED_PUBLIC_IP_ID VM1_PRIVATE_IP_ID VM2_PRIVATE_IP_ID VM1_PRIVATE_IP)

get_env_val() {
  local key="$1" file="$2"
  [[ -f "$file" ]] || return 1
  local line
  line="$(grep -E "^[[:space:]]*${key}=" "$file" | tail -1 || true)"
  [[ -n "$line" ]] || return 1
  printf '%s\n' "${line#*=}"
}

prompt_val() {
  local key="$1" cur="${2:-}"
  # Already have a value from /etc or oci.env — use it (no confirm prompt).
  # Only the value goes to stdout (captured by callers); logs stay on stderr.
  if [[ -n "$cur" ]]; then
    log "  using $key from env file"
    printf '%s\n' "$cur"
    return 0
  fi
  if [[ "$YES" -eq 1 ]]; then
    die "missing $key (non-interactive)"
  fi
  local ans
  read -r -p "$key: " ans
  [[ -n "$ans" ]] || die "empty $key"
  printf '%s\n' "$ans"
}

stage_config() {
  log "==> Writing /etc/mccontrol/oci.env and config.json"
  local src=""
  if [[ -n "${OCI_ENV_FILE:-}" ]]; then
    src="$OCI_ENV_FILE"
  elif [[ -f "$REPO_VM2/oci.env" ]]; then
    src="$REPO_VM2/oci.env"
  fi

  declare -A vals=()
  local k v
  for k in "${REQUIRED_OCI_KEYS[@]}"; do
    v=""
    # --yes + vm2/oci.env: prefer the repo file so reinstall picks up edits.
    if [[ "$YES" -eq 1 && -n "$src" ]]; then
      v="$(get_env_val "$k" "$src" 2>/dev/null || true)"
    fi
    if [[ -z "$v" ]]; then
      v="$(get_env_val "$k" /etc/mccontrol/oci.env 2>/dev/null || true)"
    fi
    if [[ -z "$v" && -n "$src" ]]; then
      v="$(get_env_val "$k" "$src" 2>/dev/null || true)"
    fi
    # Strip CR if env file was edited on Windows.
    v="${v//$'\r'/}"
    vals["$k"]="$(prompt_val "$k" "$v")"
  done

  local wait_sec
  wait_sec="$(get_env_val WAIT_TIMEOUT_SEC /etc/mccontrol/oci.env 2>/dev/null || true)"
  [[ -n "$wait_sec" ]] || wait_sec="$(get_env_val WAIT_TIMEOUT_SEC "${src:-/dev/null}" 2>/dev/null || true)"
  wait_sec="${wait_sec:-600}"
  wait_sec="${wait_sec//$'\r'/}"

  local auth_cli path_cli
  auth_cli="$(get_env_val OCI_CLI_AUTH /etc/mccontrol/oci.env 2>/dev/null || true)"
  [[ -n "$auth_cli" ]] || auth_cli="$(get_env_val OCI_CLI_AUTH "${src:-/dev/null}" 2>/dev/null || true)"
  auth_cli="${auth_cli:-instance_principal}"
  auth_cli="${auth_cli//$'\r'/}"
  path_cli="$(get_env_val PATH /etc/mccontrol/oci.env 2>/dev/null || true)"
  [[ -n "$path_cli" ]] || path_cli="$(get_env_val PATH "${src:-/dev/null}" 2>/dev/null || true)"
  path_cli="${path_cli:-/home/ubuntu/bin:/usr/bin:/bin}"
  path_cli="${path_cli//$'\r'/}"

  local tmp_env
  tmp_env="$(mktemp)"
  copy_opt() {
    local key="$1" fallback="${2:-}"
    local v
    v="$(get_env_val "$key" "${src:-/dev/null}" 2>/dev/null || true)"
    [[ -z "$v" ]] && v="$(get_env_val "$key" /etc/mccontrol/oci.env 2>/dev/null || true)"
    v="${v//$'\r'/}"
    [[ -z "$v" ]] && v="$fallback"
    if [[ -n "$v" ]]; then
      printf '%s=%s\n' "$key" "$v"
    fi
  }

  {
    printf 'INSTANCE_ID=%s\n' "${vals[INSTANCE_ID]}"
    printf 'RESERVED_PUBLIC_IP_ID=%s\n' "${vals[RESERVED_PUBLIC_IP_ID]}"
    printf 'VM1_PRIVATE_IP_ID=%s\n' "${vals[VM1_PRIVATE_IP_ID]}"
    printf 'VM2_PRIVATE_IP_ID=%s\n' "${vals[VM2_PRIVATE_IP_ID]}"
    printf 'VM1_PRIVATE_IP=%s\n' "${vals[VM1_PRIVATE_IP]}"
    printf 'WAIT_TIMEOUT_SEC=%s\n' "$wait_sec"
    printf 'OCI_CLI_AUTH=%s\n' "$auth_cli"
    printf 'PATH=%s\n' "$path_cli"
    copy_opt HOME /home/ubuntu
    copy_opt OBJECT_STORAGE_NAMESPACE
    copy_opt OBJECT_STORAGE_BUCKET
    copy_opt OS_CACHE_DIR /var/lib/mccontrol/os-cache
    copy_opt COMPARTMENT_ID
  } >"$tmp_env"
  sudo cp "$tmp_env" /etc/mccontrol/oci.env
  rm -f "$tmp_env"
  sudo chmod 600 /etc/mccontrol/oci.env
  # WinSCP leaves CR in KEY=value — breaks systemd EnvironmentFile and `source`.
  sudo python3 - <<'PY'
from pathlib import Path
p = Path("/etc/mccontrol/oci.env")
p.write_bytes(p.read_bytes().replace(b"\r\n", b"\n").replace(b"\r", b"\n"))
PY

  if [[ ! -f /etc/mccontrol/config.json ]]; then
    sudo cp "$REPO_VM2/config.example.json" /etc/mccontrol/config.json
  fi

  sudo python3 - "${vals[VM1_PRIVATE_IP]}" <<'PY'
import json, sys
ip = sys.argv[1]
path = "/etc/mccontrol/config.json"
with open(path, encoding="utf-8") as f:
    cfg = json.load(f)
cfg["vm1_private_ip"] = ip
with open(path, "w", encoding="utf-8") as f:
    json.dump(cfg, f, indent=2)
    f.write("\n")
PY
}
ensure_iptables_accept() {
  local port="$1"
  if sudo iptables -C INPUT -p tcp -m state --state NEW -m tcp --dport "$port" -j ACCEPT 2>/dev/null; then
    log "iptables: tcp/$port already allowed"
    return 0
  fi
  sudo iptables -I INPUT 5 -p tcp -m state --state NEW -m tcp --dport "$port" -j ACCEPT
  log "iptables: inserted ACCEPT for tcp/$port"
}

stage_firewall() {
  if [[ "$SKIP_FIREWALL" -eq 1 ]]; then
    log "==> Skipping firewall (--skip-firewall)"
    return 0
  fi
  log "==> Host firewall: allow tcp 22, 25565, 8080 (no firewalld)"
  ensure_iptables_accept 22
  ensure_iptables_accept 25565
  ensure_iptables_accept 8080
  if command -v netfilter-persistent >/dev/null 2>&1; then
    sudo netfilter-persistent save
  elif [[ -d /etc/iptables ]]; then
    sudo sh -c 'iptables-save > /etc/iptables/rules.v4'
  else
    log "WARN: could not persist iptables rules automatically"
  fi
}
stage_systemd() {
  log "==> systemd"
  sudo systemctl daemon-reload
  if [[ "$NO_START" -eq 1 ]]; then
    log "Skipping enable/start (--no-start)"
    return 0
  fi
  if systemctl is-active --quiet mccontrol 2>/dev/null; then
    sudo systemctl restart mccontrol
  else
    sudo systemctl enable --now mccontrol
  fi
  if [[ -f /etc/systemd/system/mccontrol-reconcile.timer ]]; then
    sudo systemctl enable --now mccontrol-reconcile.timer
  fi
  sudo systemctl --no-pager --full status mccontrol || true
}

stage_smoke() {
  if [[ "$NO_START" -eq 1 ]]; then
    log "==> Skipping smoke (--no-start)"
    return 0
  fi
  log "==> Smoke: GET http://127.0.0.1:8080/api/status"
  if curl -sf http://127.0.0.1:8080/api/status; then
    echo
    log "Smoke OK"
    return 0
  fi
  echo
  log "WARN: smoke failed — check: journalctl -u mccontrol -n 50 --no-pager"
  if [[ "$YES" -eq 1 ]]; then
    die "smoke failed in --yes mode"
  fi
}

stage_checklist() {
  cat <<'EOF'

==> Console checklist (manual)

1) Reserved public IP
   - Create a reserved public IP in this VCN for the play address friends use.
   - Keep each VM's ephemeral public IP for SSH / admin UI (door UI on :8080).
   - Put RESERVED_PUBLIC_IP_ID and VM1/VM2 private IP OCIDs into /etc/mccontrol/oci.env.

2) IAM (instance principal on the door)
   - Dynamic group matching the door instance OCID.
   - Policies allowing that group to:
       - start / SOFTSTOP the primary (VM1) instance
       - move the reserved public IP between VM1 and VM2 private IPs
   - Test from the door:
       oci iam region get --auth instance_principal
       oci compute instance get --instance-id "$INSTANCE_ID" --auth instance_principal

3) Security List (IP allowlist; no firewalld)
   - Friend /32s → TCP 25565
   - Admin /32s → TCP 8080 (and SSH 22 as today)
   - Do not open Minecraft/SSH/admin UI to 0.0.0.0/0

4) After IAM + reserved IP are real
   - Re-run: bash vm2/install.sh --yes
   - Or: sudo systemctl restart mccontrol
   - Then exercise wake / MOTD from a client IP on the allowlist.

EOF
}

main() {
  stage_deps
  stage_oci_cli
  stage_build
  stage_install_files
  stage_config
  stage_firewall
  stage_systemd
  stage_smoke
  stage_checklist
  log "Install script finished."
}

main "$@"
