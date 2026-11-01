#!/bin/sh
# Build pinned FFmpeg 7.1 shared libraries into <output-dir>.
#
# Usage: build-ffmpeg7.sh <output-dir> [scratch-dir]
#
# Writes libavcodec.so.61 (or the macOS dylib) and the matching majors
# under <output-dir>/lib. Records the tag, commit and licence in
# <output-dir>/NVG-FFMPEG.txt and copies the upstream licence texts.
# Does not install anything outside <output-dir> and does not alias
# system libraries. If nasm is missing, configures --disable-x86asm.
# Needs git, cc and make. No sudo, apt or brew.

set -eu

FFMPEG_REPO="https://github.com/FFmpeg/FFmpeg.git"
FFMPEG_TAG="n7.1.1"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi

OUTPUT_DIR=$1
if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-ffmpeg7)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)
SRC_DIR="$SCRATCH_DIR/FFmpeg"

CC=${CC:-cc}
command -v git >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v make >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }

echo "==> fetching $FFMPEG_REPO tag $FFMPEG_TAG into $SRC_DIR"
if [ ! -d "$SRC_DIR/.git" ]; then
    git clone --depth 1 --branch "$FFMPEG_TAG" "$FFMPEG_REPO" "$SRC_DIR"
fi
git -C "$SRC_DIR" fetch --depth 1 origin "refs/tags/$FFMPEG_TAG:refs/tags/$FFMPEG_TAG" || true
git -C "$SRC_DIR" checkout -q "$FFMPEG_TAG"
ACTUAL_TAG=$(git -C "$SRC_DIR" describe --tags --exact-match HEAD)
if [ "$ACTUAL_TAG" != "$FFMPEG_TAG" ]; then
    echo "error: HEAD is $ACTUAL_TAG, expected $FFMPEG_TAG" >&2
    exit 1
fi
COMMIT=$(git -C "$SRC_DIR" rev-parse HEAD)

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)

ASM_FLAG=""
if ! command -v nasm >/dev/null 2>&1; then
    echo "==> nasm not found; configuring --disable-x86asm"
    ASM_FLAG="--disable-x86asm"
fi

JOBS=2
if command -v nproc >/dev/null 2>&1; then
    JOBS=$(nproc)
elif command -v sysctl >/dev/null 2>&1; then
    JOBS=$(sysctl -n hw.ncpu 2>/dev/null || echo 2)
fi

echo "==> configuring $FFMPEG_TAG ($COMMIT) prefix $OUTPUT_DIR"
# shellcheck disable=SC2086
(
    cd "$SRC_DIR"
    ./configure \
        --prefix="$OUTPUT_DIR" \
        --libdir="$OUTPUT_DIR/lib" \
        --enable-shared \
        --disable-static \
        --disable-programs \
        --disable-doc \
        --disable-debug \
        --enable-gpl \
        --enable-version3 \
        --enable-rpath \
        $ASM_FLAG
)

echo "==> building with -j$JOBS"
make -C "$SRC_DIR" -j"$JOBS"
make -C "$SRC_DIR" install

{
    echo "tag=$FFMPEG_TAG"
    echo "commit=$COMMIT"
    echo "license=GPL-3.0-or-later"
    echo "configure=--enable-shared --disable-static --disable-programs --enable-gpl --enable-version3 --enable-rpath $ASM_FLAG"
    echo "source=$FFMPEG_REPO"
    echo "note=Shared libraries only. Load libavcodec.so.61 / libavcodec.61.dylib by absolute path. Do not alias system SONAMEs."
} > "$OUTPUT_DIR/NVG-FFMPEG.txt"

if [ -f "$SRC_DIR/COPYING.GPLv3" ]; then
    cp "$SRC_DIR/COPYING.GPLv3" "$OUTPUT_DIR/LICENSE.GPLv3"
fi
if [ -f "$SRC_DIR/COPYING.LGPLv2.1" ]; then
    cp "$SRC_DIR/COPYING.LGPLv2.1" "$OUTPUT_DIR/LICENSE.LGPLv2.1"
fi

echo "==> installed FFmpeg $FFMPEG_TAG into $OUTPUT_DIR"
find "$OUTPUT_DIR/lib" -maxdepth 1 -name 'libavcodec*' -o -name 'avcodec*' | head
