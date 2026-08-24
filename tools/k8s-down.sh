#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
pid_dir="${repository_root}/.tmp/k8s"

if [[ -d "$pid_dir" ]]; then
  for pid_file in "$pid_dir"/*.pid; do
    [[ -f "$pid_file" ]] || continue
    kill "$(cat "$pid_file")" 2>/dev/null || true
    rm -f -- "$pid_file"
  done
fi

kubectl -n basis delete deployment concierge-web concierge --ignore-not-found
kubectl -n basis delete gameservers --all --ignore-not-found
kubectl -n basis delete secret basis-web-tls concierge-config concierge-admin --ignore-not-found
echo "Stopped Concierge resources and local port-forwards. Minikube itself remains running."
