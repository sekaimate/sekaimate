#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
if [[ -f "${ROOT_DIR}/.env.local" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.local"
  set +a
fi

gateway_port="${BASIS_SSO_GATEWAY_PORT:-5081}"
curl --silent --show-error --insecure --fail \
  "https://127.0.0.1:${gateway_port}/health"
echo
