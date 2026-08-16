#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="${ROOT_DIR}/Build/WebDev"
STAMP="${ROOT_DIR}/.cache/web-dev-build.stamp"

has_required_output() {
  [[ -s "${BUILD_DIR}/index.html" ]] \
    && compgen -G "${BUILD_DIR}/Build/*.loader.js" >/dev/null \
    && compgen -G "${BUILD_DIR}/Build/*.framework.js*" >/dev/null \
    && compgen -G "${BUILD_DIR}/Build/*.wasm*" >/dev/null \
    && compgen -G "${BUILD_DIR}/Build/*.data*" >/dev/null \
    && [[ -s "${BUILD_DIR}/BEE/world.BEE" ]] \
    && [[ -s "${BUILD_DIR}/StreamingAssets/aa/settings.json" ]] \
    && [[ -s "${BUILD_DIR}/StreamingAssets/aa/catalog.bin" ]]
}

needs_build=false
if ! has_required_output || [[ ! -f "${STAMP}" ]]; then
  needs_build=true
else
  for source_dir in \
    "${ROOT_DIR}/Basis/Assets" \
    "${ROOT_DIR}/Basis/Packages" \
    "${ROOT_DIR}/Basis/ProjectSettings" \
    "${ROOT_DIR}/local"; do
    if [[ -d "${source_dir}" ]] && find "${source_dir}" -type f -newer "${STAMP}" -print -quit | grep -q .; then
      needs_build=true
      break
    fi
  done

  for source_file in \
    "${ROOT_DIR}/tools/build-web.sh"; do
    if [[ -f "${source_file}" && "${source_file}" -nt "${STAMP}" ]]; then
      needs_build=true
      break
    fi
  done
fi

if [[ "${needs_build}" == true ]]; then
  echo "WebDev build is missing or stale; building it."
  exec "${ROOT_DIR}/tools/build-web.sh" --dev
fi

echo "WebDev build is up to date; reusing ${BUILD_DIR}."
