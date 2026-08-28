#!/usr/bin/env bash
set -euo pipefail

# Deploys Concierge, Agones, Basis Server and the WebGL client onto a
# single-node k3s host that serves users over the internet. Run this on the
# server itself. tools/k8s-up.sh stays the local minikube workflow.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
namespace="${K8S_NAMESPACE:-basis}"
# shellcheck source=tools/public-env.sh
source "${repository_root}/tools/public-env.sh"
# shellcheck source=tools/container-engine.sh
source "${repository_root}/tools/container-engine.sh"

concierge_image="${CONCIERGE_IMAGE:-concierge:local}"
basis_server_image="${BASIS_SERVER_IMAGE:-basis-server:local}"
# Keep this default in sync with concierge/deploy/40-web-deployment.yaml.
web_image_default="ghcr.io/sekaimate/concierge-web:dev"
web_image="${WEB_IMAGE:-$web_image_default}"
config_path="${K3S_CONCIERGE_CONFIG:-${repository_root}/local/concierge/appsettings.public.json}"
cluster_only=false

usage() {
  echo "Usage: $0 [--cluster-only]"
  echo "  --cluster-only  Install k3s and Agones, then stop."
}

if [[ "${1:-}" == "--cluster-only" ]]; then
  cluster_only=true
elif [[ "${1:-}" == "--help" || "${1:-}" == "-h" ]]; then
  usage
  exit 0
elif [[ -n "${1:-}" ]]; then
  echo "Unknown option: $1" >&2
  usage >&2
  exit 2
fi

if [[ "$(uname -s)" != "Linux" ]]; then
  echo "k3s runs on Linux only; use 'mise run k8s:up' for local development." >&2
  exit 1
fi
for command_name in curl node openssl; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required; run mise install first." >&2
    exit 1
  fi
done

if ! command -v k3s >/dev/null 2>&1; then
  echo "==> Installing k3s"
  # Traefik is disabled because Caddy owns 80 and 443 on this host. The
  # kubeconfig mode lets the deploying user run kubectl without sudo.
  curl -sfL https://get.k3s.io \
    | sudo INSTALL_K3S_EXEC="--disable=traefik --disable=servicelb --write-kubeconfig-mode=644" sh -
fi
if ! command -v kubectl >/dev/null 2>&1; then
  echo "kubectl is required; run mise install first." >&2
  exit 1
fi
# k3s installs into /usr/local/bin, which sudo's secure_path usually leaves
# out, so image imports have to call it by absolute path.
k3s_binary="$(command -v k3s || true)"
if [[ -z "$k3s_binary" ]]; then
  echo "k3s is not on PATH after installation." >&2
  exit 1
fi
# kubectl wait fails outright when nothing matches yet, and the Node object
# appears a moment after k3s first starts.
for _ in $(seq 1 60); do
  if kubectl get nodes -o name 2>/dev/null | grep -q .; then
    break
  fi
  sleep 2
done
kubectl wait --for=condition=Ready node --all --timeout=180s

# Basis Server watches files through inotify, and the distro default of 128
# instances is shared by every pod running as the same uid on this host, so
# game servers die with "The configured user limit (128) on the number of
# inotify instances has been reached".
echo "==> Checking inotify limits"
inotify_instances_minimum=1024
inotify_watches_minimum=524288
current_instances="$(sysctl -n fs.inotify.max_user_instances 2>/dev/null || echo 0)"
current_watches="$(sysctl -n fs.inotify.max_user_watches 2>/dev/null || echo 0)"
if (( current_instances < inotify_instances_minimum || current_watches < inotify_watches_minimum )); then
  printf 'fs.inotify.max_user_instances = %s\nfs.inotify.max_user_watches = %s\n' \
    "$(( current_instances > inotify_instances_minimum ? current_instances : inotify_instances_minimum ))" \
    "$(( current_watches > inotify_watches_minimum ? current_watches : inotify_watches_minimum ))" \
    | sudo tee /etc/sysctl.d/90-basis-inotify.conf >/dev/null
  sudo sysctl -q -p /etc/sysctl.d/90-basis-inotify.conf
  echo "Raised inotify limits to at least ${inotify_instances_minimum} instances and ${inotify_watches_minimum} watches."
fi

echo "==> Installing Agones"
bash "$repository_root/tools/k8s-agones-install.sh"

# k3s ServiceLB publishes LoadBalancer Services on the node's own ports, and
# Agones ships agones-allocator (443) and agones-ping (80 and UDP 50000) as
# LoadBalancers. On a cluster that still has ServiceLB they take the ports
# Caddy needs. Concierge creates GameServers through the Kubernetes API and
# never calls the allocation or ping services.
for service_name in agones-allocator agones-ping-http-service agones-ping-udp-service; do
  if kubectl -n agones-system get "service/$service_name" >/dev/null 2>&1; then
    kubectl -n agones-system patch "service/$service_name" --type=merge -p '{"spec":{"type":"ClusterIP"}}' >/dev/null
  fi
done

if [[ "$cluster_only" == true ]]; then
  exit 0
fi

# Caddy comes first so that certificate issuance runs while the images build.
echo "==> Installing Caddy"
bash "$repository_root/tools/caddy-install.sh"
echo "==> Applying the Caddyfile"
bash "$repository_root/tools/caddy-apply.sh"

# Kubernetes expands a bare name such as concierge:local to
# docker.io/library/concierge:local, while podman stores local builds under
# localhost/. Build with the fully qualified name so the imported image is the
# one the kubelet looks up.
qualified_image_name() {
  case "$1" in
    */*) printf '%s' "$1" ;;
    *) printf 'docker.io/library/%s' "$1" ;;
  esac
}

import_image() {
  local image="$1" context="$2" dockerfile="$3"
  local archive_dir archive build_name
  build_name="$(qualified_image_name "$image")"
  archive_dir="$(mktemp -d)"
  archive="$archive_dir/image.tar"
  ( cd "$context" && "$container_engine" build -t "$build_name" -f "$dockerfile" . )
  "$container_engine" save -o "$archive" "$build_name"
  # k3s uses its own containerd, so a locally built image has to be imported
  # rather than pulled.
  sudo "$k3s_binary" ctr images import "$archive"
  rm -rf -- "$archive_dir"
}

echo "==> Building Concierge image"
import_image "$concierge_image" "$repository_root/concierge" Dockerfile
echo "==> Building Basis Server image"
import_image "$basis_server_image" "$repository_root/Basis Server" Docker/Dockerfile

echo "==> Applying namespace and RBAC"
kubectl apply -f "$repository_root/concierge/deploy/00-namespace.yaml"
kubectl apply -f "$repository_root/concierge/deploy/10-rbac.yaml"

if [[ ! -f "$config_path" ]]; then
  mkdir -p "$(dirname "$config_path")"
  sed \
    -e "s#concierge\.example\.com#${concierge_domain}#g" \
    -e "s#web\.example\.com#${web_domain}#g" \
    -e "s#rooms\.example\.com#${rooms_domain}#g" \
    "$repository_root/concierge/appsettings.public.example.json" > "$config_path"
  chmod 600 "$config_path"
  echo "Created $config_path for ${concierge_domain}. Edit the Google credentials before using SSO." >&2
fi
node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$config_path"
configured_base_url="$(node -e 'process.stdout.write(String(JSON.parse(require("fs").readFileSync(process.argv[1], "utf8")).Broker.PublicBaseUrl || ""))' "$config_path")"
if [[ "$configured_base_url" != "https://${concierge_domain}" ]]; then
  echo "WARNING: Broker.PublicBaseUrl is '$configured_base_url' but the configured domain is https://${concierge_domain}." >&2
  echo "         Join URLs use PublicBaseUrl; update $config_path if this is not intended." >&2
fi

echo "==> Creating Concierge Secrets"
kubectl -n "$namespace" create secret generic concierge-config --from-file="appsettings.json=$config_path" --dry-run=client -o yaml | kubectl apply -f -
admin_token="${CONCIERGE_ADMIN_TOKEN:-}"
if [[ -z "$admin_token" ]]; then
  admin_token="$(kubectl -n "$namespace" get secret concierge-admin -o jsonpath='{.data.token}' 2>/dev/null | base64 -d 2>/dev/null || true)"
fi
if [[ -z "$admin_token" ]]; then
  admin_token="$(openssl rand -base64 32)"
fi
kubectl -n "$namespace" create secret generic concierge-admin --from-literal="token=$admin_token" --dry-run=client -o yaml | kubectl apply -f -
world_password="${BASIS_WORLD_BEE_PASSWORD:-}"
if [[ -z "$world_password" ]]; then
  echo "BASIS_WORLD_BEE_PASSWORD is required; set it in .env.local before creating a meeting." >&2
  exit 1
fi
kubectl -n "$namespace" create secret generic concierge-world --from-literal="password=$world_password" --dry-run=client -o yaml | kubectl apply -f -

echo "==> Creating browser endpoint ConfigMap"
kubectl -n "$namespace" create configmap concierge-endpoints \
  --from-literal="BASIS_SERVER_IMAGE=$basis_server_image" \
  --from-literal="BASIS_SERVER_WEBSOCKET_ENABLED=true" \
  --from-literal="BASIS_SERVER_WEBSOCKET_USE_TLS=true" \
  --from-literal="BASIS_SERVER_WEBSOCKET_ALLOWED_ORIGINS=https://${web_domain}" \
  --from-literal="BASIS_SERVER_PUBLIC_HOST=${rooms_domain}" \
  --from-literal="BASIS_WORLD_BEE_URL=https://${web_domain}/BEE/world.BEE" \
  --dry-run=client -o yaml | kubectl apply -f -

echo "==> Waiting for the ${rooms_domain} certificate"
# Issuance needs DNS pointing here and 80/tcp reachable, so this can fail on a
# first deploy while the security list is still closed.
certificate_synced=false
for _ in $(seq 1 "${CADDY_CERT_WAIT_ATTEMPTS:-30}"); do
  if bash "$repository_root/tools/caddy-sync-cert.sh" >/dev/null 2>&1; then
    certificate_synced=true
    break
  fi
  sleep "${CADDY_CERT_WAIT_INTERVAL:-10}"
done
if [[ "$certificate_synced" == true ]]; then
  echo "basis-web-tls now holds Caddy's certificate for ${rooms_domain}."
else
  echo "WARNING: no certificate for ${rooms_domain} yet, so basis-web-tls was not updated." >&2
  echo "         Browsers cannot join meetings over wss until 80/tcp and 443/tcp reach this host" >&2
  echo "         and DNS resolves to it. Re-run 'mise run k3s:up' once that is in place." >&2
fi

echo "==> Applying Concierge and WebGL"
sed "s#image: concierge:local#image: ${concierge_image}#" "$repository_root/concierge/deploy/20-deployment-public.yaml" \
  | kubectl apply -f -
kubectl apply -f "$repository_root/concierge/deploy/30-service.yaml"
sed "s#image: ${web_image_default}#image: ${web_image}#" "$repository_root/concierge/deploy/40-web-deployment.yaml" \
  | kubectl apply -f -
sed \
  -e "s#nodePort: 30080#nodePort: ${concierge_node_port}#" \
  -e "s#nodePort: 30173#nodePort: ${web_node_port}#" \
  "$repository_root/concierge/deploy/50-nodeport.yaml" \
  | kubectl apply -f -
# The images are rebuilt on every run, and an unchanged pod spec would keep
# the previous ones running, so roll both deployments explicitly.
kubectl -n "$namespace" rollout restart deployment/concierge deployment/concierge-web

# A failed rollout is almost always visible in the pod events, so print them
# instead of leaving only the timeout message.
report_rollout_failure() {
  local selector="$1"
  echo "--- pods ($selector)" >&2
  kubectl -n "$namespace" get pods -l "$selector" -o wide >&2 || true
  echo "--- events ($selector)" >&2
  kubectl -n "$namespace" describe pods -l "$selector" 2>/dev/null | sed -n '/Events:/,$p' >&2 || true
  echo "--- logs ($selector)" >&2
  kubectl -n "$namespace" logs -l "$selector" --tail=30 --all-containers >&2 2>/dev/null || true
}
for deployment_name in concierge concierge-web; do
  if ! kubectl -n "$namespace" rollout status "deployment/$deployment_name" --timeout=180s; then
    report_rollout_failure "app=$deployment_name"
    exit 1
  fi
done

echo "Kubernetes environment is ready on k3s."
echo "Admin Console: https://${concierge_domain}/admin/"
echo "WebGL client:  https://${web_domain}/"
echo "Meetings:      wss://${rooms_domain}:{port}/basis (Agones dynamic ports)"
echo "Admin token:   ${admin_token}"
echo "Ports:         open 80/tcp, 443/tcp and 7000-8000 (tcp+udp) in the OCI security list and the OS firewall."
