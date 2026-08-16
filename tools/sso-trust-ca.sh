#!/usr/bin/env bash
set -euo pipefail

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "sso:trust-ca currently supports macOS only." >&2
  exit 1
fi

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ca_path="${ROOT_DIR}/Basis Server/Docker/sso/certs/basis-local-ca.crt"
if [[ ! -f "${ca_path}" ]]; then
  echo "Local CA does not exist yet. Run: mise run sso:up" >&2
  exit 1
fi

keychain="$(security default-keychain -d user | sed -E 's/^[[:space:]]*"//; s/"[[:space:]]*$//')"
if [[ ! -f "${keychain}" ]]; then
  echo "Default macOS keychain does not exist: ${keychain}" >&2
  exit 1
fi
security add-trusted-cert -d -r trustRoot -p ssl -k "${keychain}" "${ca_path}"
echo "Trusted local SSO CA in ${keychain}"
