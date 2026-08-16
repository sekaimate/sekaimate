#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ca_path="${ROOT_DIR}/Basis Server/Docker/sso/certs/basis-local-ca.crt"

if [[ ! -f "${ca_path}" ]]; then
  echo "Local CA does not exist yet. Run: mise run sso:up" >&2
  exit 1
fi
echo "${ca_path}"
