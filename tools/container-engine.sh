# Resolves container_engine to podman or docker, matching the driver
# preference in tools/k8s-up.sh. Source this from a script that runs with
# `set -euo pipefail`.

container_engine="${CONTAINER_ENGINE:-}"
if [[ -n "$container_engine" ]]; then
  if ! command -v "$container_engine" >/dev/null 2>&1; then
    echo "CONTAINER_ENGINE is set to $container_engine, which is not installed." >&2
    exit 1
  fi
elif command -v podman >/dev/null 2>&1; then
  container_engine=podman
elif command -v docker >/dev/null 2>&1; then
  container_engine=docker
else
  echo "Either podman or docker is required." >&2
  exit 1
fi
