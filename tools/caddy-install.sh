#!/usr/bin/env bash
set -euo pipefail

# Installs the mise-provided Caddy binary as a systemd service. mise keeps the
# binary under the invoking user's home, which the caddy service user cannot
# execute, so the binary is copied to /usr/local/bin.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
service_path="${CADDY_SERVICE_PATH:-/etc/systemd/system/caddy.service}"
binary_path="${CADDY_BINARY_PATH:-/usr/local/bin/caddy}"
data_dir="${CADDY_HOME:-/var/lib/caddy}"
# tools/caddy-apply.sh writes the Caddyfile to this same path.
caddyfile_path="${CADDYFILE_PATH:-/etc/caddy/Caddyfile}"

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "The Caddy service is only set up on the Linux server." >&2
  exit 1
fi

source_binary="$(command -v caddy || true)"
if [[ -z "$source_binary" ]]; then
  echo "caddy is not installed; run mise install first." >&2
  exit 1
fi

if ! id -u caddy >/dev/null 2>&1; then
  echo "==> Creating the caddy service user"
  sudo useradd --system --home "$data_dir" --create-home --shell /usr/sbin/nologin caddy
fi
sudo mkdir -p "$data_dir" "$(dirname "$caddyfile_path")"
sudo chown caddy:caddy "$data_dir"

binary_changed=false
if ! sudo cmp -s "$source_binary" "$binary_path"; then
  sudo install -m 755 "$source_binary" "$binary_path"
  binary_changed=true
fi

# Modelled on caddyserver/dist init/caddy.service, with ExecStart pointing at
# the copied binary.
service_unit="$(cat <<EOF
[Unit]
Description=Caddy
Documentation=https://caddyserver.com/docs/
After=network.target network-online.target
Requires=network-online.target

[Service]
Type=notify
User=caddy
Group=caddy
ExecStart=${binary_path} run --environ --config ${caddyfile_path}
ExecReload=${binary_path} reload --config ${caddyfile_path} --force
TimeoutStopSec=5s
LimitNOFILE=1048576
PrivateTmp=true
ProtectSystem=full
AmbientCapabilities=CAP_NET_ADMIN CAP_NET_BIND_SERVICE

[Install]
WantedBy=multi-user.target
EOF
)"
if ! printf '%s\n' "$service_unit" | sudo cmp -s - "$service_path"; then
  printf '%s\n' "$service_unit" | sudo tee "$service_path" >/dev/null
  sudo systemctl daemon-reload
  binary_changed=true
fi

if [[ "$binary_changed" == true ]] && systemctl is-active --quiet caddy; then
  sudo systemctl restart caddy
fi

echo "Caddy service is installed: $binary_path ($("$source_binary" version | head -n 1))"
