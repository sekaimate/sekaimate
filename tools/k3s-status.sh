#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
namespace="${K8S_NAMESPACE:-basis}"
# shellcheck source=tools/public-env.sh
source "${repository_root}/tools/public-env.sh"

systemctl is-active --quiet k3s && echo "k3s: active" || echo "k3s: inactive"
systemctl is-active --quiet caddy && echo "caddy: active" || echo "caddy: inactive"
kubectl -n "$namespace" get pods,svc,gameservers 2>/dev/null || true
