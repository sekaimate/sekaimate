#!/usr/bin/env bash
set -euo pipefail

install_dir="$(mktemp -d)"
trap 'rm -rf -- "$install_dir"' EXIT

kubectl create namespace agones-system --dry-run=client -o yaml | kubectl apply -f -
curl -fsSL https://raw.githubusercontent.com/googleforgames/agones/release-1.60.0/install/yaml/install.yaml \
  -o "$install_dir/agones-install.yaml"
sed -E '/x-kubernetes-patch-strategy:|x-kubernetes-patch-merge-key:/d' \
  "$install_dir/agones-install.yaml" > "$install_dir/agones-install-fixed.yaml"
kubectl apply --server-side --force-conflicts -f "$install_dir/agones-install-fixed.yaml"
# The controllers create these pods just after the apply; waiting before any
# exist would fail with "no matching resources found".
for _ in $(seq 1 60); do
  if kubectl -n agones-system get pods -o name 2>/dev/null | grep -q .; then
    break
  fi
  sleep 2
done
kubectl wait --for=condition=Ready pods --all -n agones-system --timeout=300s
