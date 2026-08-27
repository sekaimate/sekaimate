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

world_url="${BASIS_WORLD_BEE_URL:-http://127.0.0.1:4173/BEE/world.BEE}"
world_password="${BASIS_WORLD_BEE_PASSWORD:-}"
initial_resources_dir="${ROOT_DIR}/Basis Server/Docker/initialresources"
initial_world_file="${initial_resources_dir}/world.xml"

xml_escape() {
  sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g' -e 's/"/\&quot;/g' -e "s/'/\&apos;/g"
}

mkdir -p "${initial_resources_dir}"
escaped_world_url="$(printf '%s' "${world_url}" | xml_escape)"
escaped_world_password="$(printf '%s' "${world_password}" | xml_escape)"
{
  printf '%s\n' '<BasisLoadableConfiguration>'
  printf '%s\n' '  <Mode>1</Mode>'
  printf '%s\n' '  <LoadedNetID></LoadedNetID>'
  printf '  <UnlockPassword>%s</UnlockPassword>\n' "${escaped_world_password}"
  printf '  <CombinedURL>%s</CombinedURL>\n' "${escaped_world_url}"
  printf '%s\n' '  <Persist>true</Persist>'
  printf '%s\n' '</BasisLoadableConfiguration>'
} > "${initial_world_file}"

echo "Basis startup world configured through initialresources: ${world_url}"

exec docker compose \
  -f "Basis Server/Docker/docker-compose.yml" \
  -f "Basis Server/Docker/docker-compose.local-web.yml" \
  up -d --build
