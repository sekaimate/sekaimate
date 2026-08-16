#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "${ROOT_DIR}"

if [[ -f "${ROOT_DIR}/.env.local" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.local"
  set +a
fi

gateway_port="${BASIS_SSO_GATEWAY_PORT:-5081}"
compose_file="Basis Server/Docker/sso/docker-compose.yml"
health_url="https://127.0.0.1:${gateway_port}/health"

docker compose -f "${compose_file}" up -d --build

for attempt in {1..30}; do
  if response="$(curl --silent --show-error --insecure --fail "${health_url}" 2>/dev/null)"; then
    if [[ "${response}" == *'"status":"ready"'* ]]; then
      echo "SSO is ready: ${health_url}"
      echo "Admin UI: https://127.0.0.1:${gateway_port}/admin/"
      echo "CA: ${ROOT_DIR}/Basis Server/Docker/sso/certs/basis-local-ca.crt"
      exit 0
    fi
  fi
  sleep 1
done

echo "SSO gateway did not become ready: ${health_url}" >&2
docker compose -f "${compose_file}" ps >&2 || true
exit 1
