#!/usr/bin/env bash
set -euo pipefail

readonly OPUS_REPOSITORY="https://github.com/xiph/opus.git"
readonly OPUS_COMMIT="788cc89ce4f2c42025d8c70ec1b4457dc89cd50f"
readonly EMSCRIPTEN_VERSION="4.0.20-git"

if [[ $# -lt 3 ]]; then
    echo "Usage: $0 <source-directory> <build-directory> <unity-editor-directory>" >&2
    exit 64
fi

readonly SOURCE_DIRECTORY="$1"
readonly BUILD_DIRECTORY="$2"
readonly UNITY_EDITOR_DIRECTORY="$3"
readonly SCRIPT_DIRECTORY="$(cd "$(dirname "$0")" && pwd)"
readonly EMSDK_DIRECTORY="$UNITY_EDITOR_DIRECTORY/PlaybackEngines/WebGLSupport/BuildTools/Emscripten"
readonly EMSCRIPTEN_DIRECTORY="$EMSDK_DIRECTORY/emscripten"
readonly EM_CONFIG_FILE="$BUILD_DIRECTORY/.emscripten"
readonly EM_CACHE_DIRECTORY="$BUILD_DIRECTORY/emscripten-cache"
readonly OPUS_BUILD_DIRECTORY="$BUILD_DIRECTORY/opus"
readonly OUTPUT_DIRECTORY="$SCRIPT_DIRECTORY/../../Plugins/webgl"

if [[ ! -d "$SOURCE_DIRECTORY/.git" ]]; then
    git clone "$OPUS_REPOSITORY" "$SOURCE_DIRECTORY"
fi

git -C "$SOURCE_DIRECTORY" fetch origin "$OPUS_COMMIT"
git -C "$SOURCE_DIRECTORY" checkout --detach "$OPUS_COMMIT"

if [[ "$(git -C "$SOURCE_DIRECTORY" rev-parse HEAD)" != "$OPUS_COMMIT" ]]; then
    echo "Unexpected Opus source revision." >&2
    exit 1
fi

if [[ "$(<"$EMSCRIPTEN_DIRECTORY/emscripten-version.txt")" != "$EMSCRIPTEN_VERSION" ]]; then
    echo "Unity Emscripten must be $EMSCRIPTEN_VERSION." >&2
    exit 1
fi

mkdir -p "$BUILD_DIRECTORY" "$EM_CACHE_DIRECTORY" "$OUTPUT_DIRECTORY"

cat > "$EM_CONFIG_FILE" <<CONFIG
LLVM_ROOT = '$EMSDK_DIRECTORY/llvm'
BINARYEN_ROOT = '$EMSDK_DIRECTORY/binaryen'
NODE_JS = ['$EMSDK_DIRECTORY/node/node']
CACHE = '$EM_CACHE_DIRECTORY'
CONFIG

export EM_CONFIG="$EM_CONFIG_FILE"
export PATH="$EMSDK_DIRECTORY/node:$EMSCRIPTEN_DIRECTORY:$PATH"

"$EMSCRIPTEN_DIRECTORY/emcmake" cmake \
    -S "$SOURCE_DIRECTORY" \
    -B "$OPUS_BUILD_DIRECTORY" \
    -DCMAKE_BUILD_TYPE=Release \
    -DOPUS_BUILD_PROGRAMS=OFF \
    -DOPUS_BUILD_TESTING=OFF \
    -DOPUS_CUSTOM_MODES=OFF \
    -DOPUS_DISABLE_INTRINSICS=ON \
    -DOPUS_DRED=OFF \
    -DOPUS_OSCE=OFF \
    -DOPUS_INSTALL_PKG_CONFIG_MODULE=OFF \
    -DOPUS_INSTALL_CMAKE_CONFIG_MODULE=OFF

cmake --build "$OPUS_BUILD_DIRECTORY" --target opus --parallel

"$EMSCRIPTEN_DIRECTORY/emcc" \
    -O3 \
    -I"$SOURCE_DIRECTORY/include" \
    -c "$SCRIPT_DIRECTORY/opussharp_ctl.c" \
    -o "$BUILD_DIRECTORY/opussharp_ctl.o"

cp "$OPUS_BUILD_DIRECTORY/libopus.a" "$OUTPUT_DIRECTORY/libopus.a"
"$EMSCRIPTEN_DIRECTORY/emar" rcs "$OUTPUT_DIRECTORY/libopus.a" "$BUILD_DIRECTORY/opussharp_ctl.o"
