#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project_path="$repository_root/Basis"
editor_version="$(sed -n 's/^m_EditorVersion: //p' "$project_path/ProjectSettings/ProjectVersion.txt")"
unity_executable="${BASIS_UNITY_EXECUTABLE:-/Applications/Unity/Hub/Editor/$editor_version/Unity.app/Contents/MacOS/Unity}"
build_path="${1:-$repository_root/Build/Web}"

if [[ ! -x "$unity_executable" ]]; then
  echo "Unity executable not found: $unity_executable" >&2
  exit 1
fi

if [[ -e "$build_path" ]]; then
  build_path_parent="$(cd "$(dirname "$build_path")" && pwd)"
  build_path_name="$(basename "$build_path")"
  build_path="$build_path_parent/$build_path_name"
  if [[ "$build_path" == "/" || "$build_path" == "$repository_root" || "$build_path" == "$project_path" ]]; then
    echo "Refusing to remove an unsafe build path: $build_path" >&2
    exit 1
  fi
  rm -rf -- "$build_path"
fi

"$unity_executable" \
  -batchmode \
  -quit \
  -projectPath "$project_path" \
  -buildTarget WebGL \
  -executeMethod BasisHeadlessBuild.BuildWeb \
  -customBuildPath "$build_path" \
  -logFile -
