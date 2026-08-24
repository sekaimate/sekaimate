#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
namespace="${K8S_NAMESPACE:-basis}"
profile="${MINIKUBE_PROFILE:-minikube}"

if [[ -f "${repository_root}/.env.local" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${repository_root}/.env.local"
  set +a
fi
if [[ -n "${MINIKUBE_DRIVER:-}" ]]; then
  driver="$MINIKUBE_DRIVER"
elif command -v podman >/dev/null 2>&1; then
  driver=podman
elif command -v docker >/dev/null 2>&1; then
  driver=docker
else
  echo "Either podman or docker is required for Minikube." >&2
  exit 1
fi
config_path="${K8S_CONCIERGE_CONFIG:-${repository_root}/local/concierge/appsettings.minikube.json}"
pid_dir="${repository_root}/.tmp/k8s"
start_only=false

if [[ "${1:-}" == "--start-only" ]]; then
  start_only=true
elif [[ -n "${1:-}" ]]; then
  echo "Unknown option: $1" >&2
  exit 2
fi

for command_name in minikube kubectl node openssl; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "$command_name is required; run mise install first." >&2
    exit 1
  fi
done

echo "==> Starting Minikube"
minikube start -p "$profile" --driver="$driver" --cpus=4 --memory=6g --container-runtime=containerd
if [[ "$start_only" == true ]]; then
  exit 0
fi
echo "==> Installing Agones"
bash "$repository_root/tools/k8s-agones-install.sh"
echo "==> Building Concierge image"
(cd "$repository_root/concierge" && minikube image build -p "$profile" -t concierge:dev .)
echo "==> Building Basis Server image"
(cd "$repository_root/Basis Server" && minikube image build -p "$profile" -t basis-server:dev -f Docker/Dockerfile .)
echo "==> Building Development WebGL image"
MINIKUBE_PROFILE="$profile" "$repository_root/tools/build-web-image.sh"

echo "==> Applying namespace and RBAC"
kubectl --context "$profile" apply -f "$repository_root/concierge/deploy/00-namespace.yaml"
kubectl --context "$profile" apply -f "$repository_root/concierge/deploy/10-rbac.yaml"

if [[ ! -f "$config_path" ]]; then
  mkdir -p "$(dirname "$config_path")"
  cp "$repository_root/concierge/appsettings.minikube.example.json" "$config_path"
  chmod 600 "$config_path"
  echo "Created $config_path from the example. Edit Google credentials before using SSO." >&2
fi
node -e 'JSON.parse(require("fs").readFileSync(process.argv[1], "utf8"))' "$config_path"

echo "==> Creating development TLS Secret"
if ! kubectl --context "$profile" --namespace "$namespace" get secret basis-web-tls >/dev/null 2>&1; then
  cert_dir="$(mktemp -d)"
  minikube_ip="$(minikube -p "$profile" ip)"
  openssl req -x509 -newkey rsa:2048 -sha256 -nodes -keyout "$cert_dir/tls.key" -out "$cert_dir/tls.crt" -days 7 -subj '/CN=basis-web.local' -addext "subjectAltName=DNS:basis-web.local,DNS:localhost,IP:127.0.0.1,IP:${minikube_ip}"
  kubectl --context "$profile" --namespace "$namespace" create secret tls basis-web-tls --cert="$cert_dir/tls.crt" --key="$cert_dir/tls.key"
  rm -rf -- "$cert_dir"
fi

echo "==> Creating Concierge Secrets"
kubectl --context "$profile" --namespace "$namespace" create secret generic concierge-config --from-file="appsettings.json=$config_path" --dry-run=client -o yaml | kubectl --context "$profile" apply -f -
admin_token="${CONCIERGE_ADMIN_TOKEN:-}"
if [[ -z "$admin_token" ]]; then admin_token="$(openssl rand -base64 32)"; fi
if [[ -z "${CONCIERGE_ADMIN_TOKEN:-}" ]]; then
  existing_token="$(kubectl --context "$profile" --namespace "$namespace" get secret concierge-admin -o jsonpath='{.data.token}' 2>/dev/null | base64 -D 2>/dev/null || true)"
  if [[ -n "$existing_token" ]]; then
    admin_token="$existing_token"
  fi
fi
kubectl --context "$profile" --namespace "$namespace" create secret generic concierge-admin --from-literal="token=$admin_token" --dry-run=client -o yaml | kubectl --context "$profile" apply -f -
world_password="${BASIS_WORLD_BEE_PASSWORD:-}"
if [[ -z "$world_password" ]]; then
  echo "BASIS_WORLD_BEE_PASSWORD is required; set it in .env.local before creating a meeting." >&2
  exit 1
fi
kubectl --context "$profile" --namespace "$namespace" create secret generic concierge-world --from-literal="password=$world_password" --dry-run=client -o yaml | kubectl --context "$profile" apply -f -

echo "==> Applying Concierge and WebGL"
kubectl --context "$profile" apply -f "$repository_root/concierge/deploy/20-deployment-dev.yaml"
kubectl --context "$profile" apply -f "$repository_root/concierge/deploy/30-service.yaml"
kubectl --context "$profile" apply -f "$repository_root/concierge/deploy/40-web-deployment.yaml"
kubectl --context "$profile" --namespace "$namespace" rollout status deployment/concierge --timeout=180s
kubectl --context "$profile" --namespace "$namespace" rollout status deployment/concierge-web --timeout=180s

mkdir -p "$pid_dir"
start_forward() {
  local name="$1" service="$2" local_port="$3" target_port="$4"
  local pid_file="$pid_dir/${name}.pid"
  if [[ -f "$pid_file" ]] && kill -0 "$(cat "$pid_file")" 2>/dev/null; then return; fi
  kubectl --context "$profile" --namespace "$namespace" port-forward "svc/$service" "$local_port:$target_port" >"$pid_dir/${name}.log" 2>&1 &
  echo $! > "$pid_file"
}
start_forward concierge concierge 15080 5080
start_forward web concierge-web 4173 4173

gameserver_pid_file="$pid_dir/gameservers.pid"
if [[ ! -f "$gameserver_pid_file" ]] || ! kill -0 "$(cat "$gameserver_pid_file")" 2>/dev/null; then
  MINIKUBE_PROFILE="$profile" K8S_NAMESPACE="$namespace" K8S_PID_DIR="$pid_dir" \
    bash "$repository_root/tools/k8s-gameserver-forward.sh" >"$pid_dir/gameservers.log" 2>&1 &
  echo $! > "$gameserver_pid_file"
fi

echo "Kubernetes development environment is ready."
echo "Concierge: http://127.0.0.1:15080/admin/"
echo "Admin token: ${admin_token}"
echo "WebGL:     http://127.0.0.1:4173/"
echo "Logs:      $pid_dir/*.log"
echo "Stop:      mise run k8s:down"
