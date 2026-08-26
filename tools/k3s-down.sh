#!/usr/bin/env bash
set -euo pipefail

# Removes the Concierge workloads from the k3s cluster. k3s itself, Agones and
# the imported images stay in place so the next deploy is short.

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
namespace="${K8S_NAMESPACE:-basis}"
# shellcheck source=tools/public-env.sh
source "${repository_root}/tools/public-env.sh"

kubectl -n "$namespace" delete deployment concierge-web concierge --ignore-not-found
kubectl -n "$namespace" delete gameservers --all --ignore-not-found
kubectl -n "$namespace" delete service concierge-nodeport concierge-web-nodeport --ignore-not-found
kubectl -n "$namespace" delete configmap concierge-endpoints --ignore-not-found
kubectl -n "$namespace" delete secret basis-web-tls concierge-config concierge-admin concierge-world --ignore-not-found
echo "Stopped Concierge resources. k3s and Agones remain installed."
