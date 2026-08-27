# Shared settings for the public k3s deployment (tools/k3s-*.sh and
# tools/caddy-*.sh). Source this from a script that has set repository_root
# and runs with `set -euo pipefail`.

if [[ -f "${repository_root}/.env.local" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${repository_root}/.env.local"
  set +a
fi

base_domain="${BASIS_PUBLIC_DOMAIN:-}"
concierge_domain="${CONCIERGE_DOMAIN:-}"
web_domain="${WEB_DOMAIN:-}"
rooms_domain="${ROOMS_DOMAIN:-}"

if [[ -n "$base_domain" ]]; then
  concierge_domain="${concierge_domain:-concierge.${base_domain}}"
  web_domain="${web_domain:-web.${base_domain}}"
  rooms_domain="${rooms_domain:-rooms.${base_domain}}"
fi

if [[ -z "$concierge_domain" || -z "$web_domain" || -z "$rooms_domain" ]]; then
  echo "Set BASIS_PUBLIC_DOMAIN in .env.local, or set CONCIERGE_DOMAIN, WEB_DOMAIN and ROOMS_DOMAIN individually." >&2
  exit 1
fi

# Caddy reaches both Services through these NodePorts on the loopback
# interface, so they never need to be reachable from the internet.
concierge_node_port="${CONCIERGE_NODE_PORT:-30080}"
web_node_port="${WEB_NODE_PORT:-30173}"

# k3s writes this kubeconfig on install; --write-kubeconfig-mode=644 makes it
# readable without sudo.
export KUBECONFIG="${KUBECONFIG:-/etc/rancher/k3s/k3s.yaml}"
