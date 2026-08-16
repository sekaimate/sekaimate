#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repository_root/Basis"
editor_version="$(sed -n 's/^m_EditorVersion: //p' "$project_path/ProjectSettings/ProjectVersion.txt")"
unity_executable="${BASIS_UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/$editor_version/Unity.app/Contents/MacOS/Unity}"

development_build=false
build_path=""
for argument in "$@"; do
  case "$argument" in
    --dev)
      development_build=true
      ;;
    --help|-h)
      echo "Usage: $0 [--dev] [output-directory]"
      echo "  --dev  Build a reusable Development WebGL build without rebuilding Addressables."
      exit 0
      ;;
    -*)
      echo "Unknown option: $argument" >&2
      exit 2
      ;;
    *)
      if [[ -n "$build_path" ]]; then
        echo "Only one output directory may be specified." >&2
        exit 2
      fi
      build_path="$argument"
      ;;
  esac
done

if [[ -z "$build_path" ]]; then
  if [[ "$development_build" == true ]]; then
    build_path="$repository_root/Build/WebDev"
  else
    build_path="$repository_root/Build/Web"
  fi
elif [[ "$build_path" != /* ]]; then
  build_path="$repository_root/$build_path"
fi

bee_backup_dir="$(mktemp -d)"
cleanup_bee_backup() {
  rm -rf -- "$bee_backup_dir"
}
trap cleanup_bee_backup EXIT

bee_path="$build_path/BEE/world.BEE"
local_bee_path="$repository_root/local/BEE/world.BEE"
legacy_bee_path="$repository_root/Build/Web/BEE/world.BEE"
canonical_addressables_path="$repository_root/Build/Web/StreamingAssets/aa"
bee_source_path=""
if [[ -f "$local_bee_path" ]]; then
  bee_source_path="$local_bee_path"
elif [[ -f "$bee_path" ]]; then
  bee_source_path="$bee_path"
elif [[ -f "$legacy_bee_path" ]]; then
  bee_source_path="$legacy_bee_path"
fi

if [[ -n "$bee_source_path" ]]; then
  mkdir -p "$bee_backup_dir/BEE"
  cp -- "$bee_source_path" "$bee_backup_dir/BEE/world.BEE"
  if [[ "$bee_source_path" == "$legacy_bee_path" && ! -f "$local_bee_path" ]]; then
    mkdir -p "$repository_root/local/BEE"
    cp -- "$legacy_bee_path" "$local_bee_path"
    echo "Migrated world BEE to $local_bee_path"
  fi
fi

if [[ ! -x "$unity_executable" ]]; then
  echo "Unity executable not found: $unity_executable" >&2
  exit 1
fi

if [[ "$development_build" == false && -e "$build_path" ]]; then
  build_path_parent="$(cd "$(dirname "$build_path")" && pwd)"
  build_path_name="$(basename "$build_path")"
  build_path="$build_path_parent/$build_path_name"
  if [[ "$build_path" == "/" || "$build_path" == "$repository_root" || "$build_path" == "$project_path" ]]; then
    echo "Refusing to remove an unsafe build path: $build_path" >&2
    exit 1
  fi
  rm -rf -- "$build_path"
fi

if [[ "$development_build" == true && -d "$canonical_addressables_path" ]]; then
  mkdir -p "$build_path/StreamingAssets"
  cp -R -- "$canonical_addressables_path/." "$build_path/StreamingAssets/aa"
  echo "Reusing Addressables content from $canonical_addressables_path"
fi

execute_method="BasisHeadlessBuild.BuildWeb"
if [[ "$development_build" == true ]]; then
  execute_method="BasisHeadlessBuild.BuildWebDev"
fi

"$unity_executable" \
  -batchmode \
  -quit \
  -projectPath "$project_path" \
  -buildTarget WebGL \
  -executeMethod "$execute_method" \
  -customBuildPath "$build_path" \
  -logFile -

if [[ -f "$bee_backup_dir/BEE/world.BEE" ]]; then
  mkdir -p "$build_path/BEE"
  cp -- "$bee_backup_dir/BEE/world.BEE" "$build_path/BEE/world.BEE"
  echo "Preserved world BEE at $build_path/BEE/world.BEE"
fi

if [[ "$development_build" == true && "$build_path" == "$repository_root/Build/WebDev" ]]; then
  mkdir -p "$repository_root/.cache"
  touch "$repository_root/.cache/web-dev-build.stamp"
fi
