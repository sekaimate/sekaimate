#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image_tag="${WEB_IMAGE_TAG:-concierge-web:dev}"
skip_build=false

usage() {
  echo "Usage: $0 [--skip-build]"
  echo "  --skip-build  Reuse the existing Build/Web directory."
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

if ! command -v minikube >/dev/null 2>&1; then
  echo "minikube is required to build the WebGL image." >&2
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

# Build a deliberately tiny temporary context. This works consistently with
# minikube's Docker and Podman backends, regardless of whether they honor the
# Dockerfile-specific .dockerignore file. Unity's Library and repository
# history never get uploaded to the builder.
build_context="$(mktemp -d)"
cleanup_build_context() {
  rm -rf -- "$build_context"
}
trap cleanup_build_context EXIT

mkdir -p "$build_context/Build/Web" "$build_context/tools"
cp -R -- "${repository_root}/Build/Web/." "$build_context/Build/Web/"
cp -- "${repository_root}/tools/serve-web.mjs" "$build_context/tools/serve-web.mjs"
cp -- "${repository_root}/concierge/web.Dockerfile" "$build_context/web.Dockerfile"

(cd "$build_context" && minikube image build \
  -t "$image_tag" \
  -f web.Dockerfile \
  .)
