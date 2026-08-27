#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
case "${1:-}" in
  --dev)
    WEB_DIR="${ROOT_DIR}/Build/WebDev"
    ;;
  --help|-h)
    echo "Usage: $0 [--dev|web-directory]"
    exit 0
    ;;
  "")
    WEB_DIR="${ROOT_DIR}/Build/Web"
    ;;
  *)
    WEB_DIR="$1"
    ;;
esac
PORT="${PORT:-4173}"

if [[ -f "${ROOT_DIR}/.env.local" ]]; then
  set -a
  # shellcheck disable=SC1091
  source "${ROOT_DIR}/.env.local"
  set +a
fi

if [[ ! -d "${WEB_DIR}" ]]; then
  echo "Web build directory does not exist: ${WEB_DIR}" >&2
  echo "Run ./tools/build-web.sh first, or pass an existing directory as the first argument." >&2
  exit 1
fi

if [[ ! -f "${WEB_DIR}/BEE/world.BEE" ]]; then
  echo "World BEE is missing: ${WEB_DIR}/BEE/world.BEE" >&2
  echo "Place the source BEE at ${ROOT_DIR}/local/BEE/world.BEE and run the Web build again." >&2
  exit 1
fi

echo "Serving ${WEB_DIR} at http://127.0.0.1:${PORT}/"
echo "World BEE: http://127.0.0.1:${PORT}/BEE/world.BEE"
exec node "${ROOT_DIR}/tools/serve-web.mjs" "${WEB_DIR}" "${PORT}"
