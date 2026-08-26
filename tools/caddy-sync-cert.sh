#!/usr/bin/env bash
set -euo pipefail

# Copies the certificate Caddy holds for the rooms domain into the
# basis-web-tls Secret. Agones GameServers mount that Secret and terminate
# wss themselves, because their ports are assigned per meeting and cannot sit
# behind a fixed reverse-proxy port.
#
# Run this after a renewal: pods mount the Secret at creation, so existing
# meetings keep the certificate they started with and new meetings pick up
# the current one.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
namespace="${K8S_NAMESPACE:-basis}"
# shellcheck source=tools/public-env.sh
source "${repository_root}/tools/public-env.sh"

certificate_root="${CADDY_DATA_DIR:-/var/lib/caddy/.local/share/caddy}/certificates"
certificate_path="$(sudo find "$certificate_root" -type f -name "${rooms_domain}.crt" 2>/dev/null | head -n 1 || true)"
if [[ -z "$certificate_path" ]]; then
  echo "No certificate for ${rooms_domain} under ${certificate_root}." >&2
  echo "Run 'mise run caddy:apply' and wait for the certificate to be issued." >&2
  exit 1
fi
key_path="${certificate_path%.crt}.key"

work_dir="$(mktemp -d)"
cleanup_work_dir() {
  rm -rf -- "$work_dir"
}
trap cleanup_work_dir EXIT
umask 077
sudo cat "$certificate_path" > "$work_dir/tls.crt"
sudo cat "$key_path" > "$work_dir/tls.key"

kubectl -n "$namespace" create secret tls basis-web-tls \
  --cert="$work_dir/tls.crt" --key="$work_dir/tls.key" \
  --dry-run=client -o yaml | kubectl apply -f -
echo "Updated basis-web-tls from ${certificate_path}"
