#!/usr/bin/env bash
set -euo pipefail

minikube status -p "${MINIKUBE_PROFILE:-minikube}"
kubectl -n basis get pods,svc,gameservers 2>/dev/null || true
