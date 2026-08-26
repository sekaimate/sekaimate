#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Keep this default in sync with concierge/deploy/40-web-deployment.yaml and
# tools/k8s-up.sh. Consumers of the image only need to pull it, so Unity is
# required here and nowhere else.
image_reference="${WEB_IMAGE:-ghcr.io/sekaimate/concierge-web:dev}"
platforms="${WEB_IMAGE_PLATFORMS:-linux/amd64,linux/arm64}"
builder="${WEB_IMAGE_BUILDER:-sekaimate-web}"
skip_build=false

usage() {
  echo "Usage: $0 [--skip-build]"
  echo "  --skip-build  Reuse the existing Build/Web directory."
  echo
  echo "Environment variables:"
  echo "  WEB_IMAGE            Image reference to push (default: ghcr.io/sekaimate/concierge-web:dev)"
  echo "  WEB_IMAGE_PLATFORMS  Platforms to build (default: linux/amd64,linux/arm64)"
  echo "  CONTAINER_ENGINE     podman or docker (default: podman when installed)"
  echo
  echo "Log in to the registry first, for example 'podman login ghcr.io' or"
  echo "'docker login ghcr.io', with a write:packages token."
}

for argument in "$@"; do
  case "$argument" in
    --skip-build)
      skip_build=true
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Unknown option: $argument" >&2
      usage >&2
      exit 2
      ;;
  esac
done

# Works on a Podman-only or a Docker-only machine without extra configuration.
# shellcheck source=tools/container-engine.sh
source "${repository_root}/tools/container-engine.sh"

# Both engines take one --platform per architecture.
platform_arguments=()
while IFS= read -r platform; do
  [[ -n "$platform" ]] && platform_arguments+=(--platform "$platform")
done <<< "${platforms//,/$'\n'}"
if [[ ${#platform_arguments[@]} -eq 0 ]]; then
  echo "WEB_IMAGE_PLATFORMS must contain at least one platform." >&2
  exit 1
fi

# Keep this a Development WebGL build, while using Build/Web as the stable
# image input directory expected by concierge/web.Dockerfile. A completed
# build can be reused explicitly when only the image needs rebuilding.
if [[ "$skip_build" == false ]]; then
  "${repository_root}/tools/build-web.sh" --dev Build/Web
elif [[ ! -f "${repository_root}/Build/Web/index.html" ]]; then
  echo "Build/Web/index.html is missing; run without --skip-build first." >&2
  exit 1
fi

# Build a deliberately tiny temporary context so that Unity's Library and the
# repository history never get uploaded to the builder.
build_context="$(mktemp -d)"
cleanup_build_context() {
  rm -rf -- "$build_context"
}
trap cleanup_build_context EXIT

mkdir -p "$build_context/Build/Web" "$build_context/tools"
cp -R -- "${repository_root}/Build/Web/." "$build_context/Build/Web/"
cp -- "${repository_root}/tools/serve-web.mjs" "$build_context/tools/serve-web.mjs"
cp -- "${repository_root}/concierge/web.Dockerfile" "$build_context/web.Dockerfile"

# Developers pull this image on both arm64 and amd64 machines, so publish a
# manifest list. Building the foreign architecture uses QEMU emulation, which
# the Podman machine and Docker Desktop both provide.
case "$container_engine" in
  podman)
    # podman build links the per-architecture builds into a local manifest
    # list. Drop a stale list first so that removed architectures do not
    # survive into the pushed manifest.
    if podman manifest exists "$image_reference" >/dev/null 2>&1; then
      podman manifest rm "$image_reference" >/dev/null
    fi
    (cd "$build_context" && podman build \
      "${platform_arguments[@]}" \
      --manifest "$image_reference" \
      -f web.Dockerfile \
      .)
    podman manifest push --all "$image_reference" "docker://$image_reference"
    ;;
  docker)
    if ! docker buildx version >/dev/null 2>&1; then
      echo "docker buildx is required to publish a multi-platform image." >&2
      exit 1
    fi
    # The default docker driver cannot produce a manifest list, so use a
    # builder with the docker-container driver.
    if ! docker buildx inspect "$builder" >/dev/null 2>&1; then
      docker buildx create --name "$builder" --driver docker-container >/dev/null
    fi
    (cd "$build_context" && docker buildx build \
      --builder "$builder" \
      "${platform_arguments[@]}" \
      -t "$image_reference" \
      -f web.Dockerfile \
      --push \
      .)
    ;;
  *)
    echo "Unsupported container engine: $container_engine" >&2
    exit 1
    ;;
esac

echo "Published $image_reference with $container_engine"
