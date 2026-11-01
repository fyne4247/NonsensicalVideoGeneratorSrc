#!/bin/sh
# Build pinned x264 as a static PIC library into <output-dir>.
#
# Usage: build-x264.sh <output-dir> [scratch-dir]
#
# The x264 CLI is not installed. nasm is an x86 assembler. On x86_64, a
# missing nasm configures --disable-asm and the manifest records that.
# aarch64 assembly uses the platform assembler, not nasm.
# Needs git, cc and make. No sudo, apt or brew. Does not copy Homebrew.

set -eu

X264_REPO="https://code.videolan.org/videolan/x264.git"
X264_COMMIT="b35605ace3ddf7c1a5d67a2eb553f034aef41d55"
X264_LICENSE="GPL-2.0-or-later"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi

OUTPUT_DIR=$1
if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-x264)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)
SRC_DIR="$SCRATCH_DIR/x264"

CC=${CC:-cc}
command -v git >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v make >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }

ARCH=$(uname -m)
ASM_FLAG=""
ASM_NOTE="asm=platform-assembler"
if [ "$ARCH" = "x86_64" ] || [ "$ARCH" = "amd64" ] || [ "$ARCH" = "i386" ]; then
    if ! command -v nasm >/dev/null 2>&1; then
        echo "==> nasm not found on $ARCH; configuring --disable-asm"
        ASM_FLAG="--disable-asm"
        ASM_NOTE="asm=disabled reason=nasm-missing-on-$ARCH. NVG-X264-SPEED.txt records one ultrafast 160x120 encode from this build. There is no asm build on this machine to compare."
    else
        ASM_NOTE="asm=nasm"
    fi
else
    ASM_NOTE="asm=platform-assembler reason=nasm-is-x86-only-on-$ARCH"
fi

SIGNATURE="commit=$X264_COMMIT asm=$ASM_FLAG pic=yes static=yes cli=no"
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)
if [ -f "$OUTPUT_DIR/NVG-X264.txt" ] && grep -F -q "signature=$SIGNATURE" "$OUTPUT_DIR/NVG-X264.txt"; then
    if [ -f "$OUTPUT_DIR/lib/libx264.a" ]; then
        echo "==> x264 $X264_COMMIT already installed in $OUTPUT_DIR"
        exit 0
    fi
fi

echo "==> fetching $X264_REPO $X264_COMMIT into $SRC_DIR"
if [ ! -d "$SRC_DIR/.git" ]; then
    rm -rf "$SRC_DIR"
    git clone --depth 1 --branch stable "$X264_REPO" "$SRC_DIR"
fi
git -C "$SRC_DIR" fetch --depth 1 origin "$X264_COMMIT" || git -C "$SRC_DIR" fetch --depth 1 origin stable
git -C "$SRC_DIR" checkout -q "$X264_COMMIT"
ACTUAL=$(git -C "$SRC_DIR" rev-parse HEAD)
if [ "$ACTUAL" != "$X264_COMMIT" ]; then
    echo "error: HEAD is $ACTUAL, expected $X264_COMMIT" >&2
    exit 1
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

JOBS=${NVG_JOBS:-4}

echo "==> configuring x264 $X264_COMMIT prefix $OUTPUT_DIR"
# shellcheck disable=SC2086
(
    cd "$SRC_DIR"
    ./configure \
        --prefix="$OUTPUT_DIR" \
        --enable-static \
        --disable-shared \
        --enable-pic \
        --disable-cli \
        --disable-lavf \
        --disable-swscale \
        --disable-ffms \
        --disable-gpac \
        --disable-lsmash \
        --disable-opencl \
        $ASM_FLAG
)

echo "==> building x264 with -j$JOBS"
make -C "$SRC_DIR" -j"$JOBS"
make -C "$SRC_DIR" install

PC="$OUTPUT_DIR/lib/pkgconfig/x264.pc"
if [ ! -f "$PC" ]; then
    echo "error: $PC was not installed" >&2
    exit 1
fi
# ffmpeg's pkg-config check uses --libs, which omits Libs.private.
# Fold the static dependencies into Libs. A second run starts clean.
python3 - "$PC" <<'PY'
import pathlib, sys
path = pathlib.Path(sys.argv[1])
lines = path.read_text().splitlines()
out = []
for line in lines:
    if line.startswith("Libs:"):
        for flag in ("-lpthread", "-lm", "-ldl"):
            if flag not in line.split():
                line = line + " " + flag
    out.append(line)
path.write_text("\n".join(out) + "\n")
PY

if [ -f "$SRC_DIR/COPYING" ]; then
    cp "$SRC_DIR/COPYING" "$OUTPUT_DIR/COPYING"
fi

{
    echo "commit=$X264_COMMIT"
    echo "license=$X264_LICENSE"
    echo "source=$X264_REPO"
    echo "signature=$SIGNATURE"
    echo "$ASM_NOTE"
    echo "configure=--enable-static --disable-shared --enable-pic --disable-cli $ASM_FLAG"
    echo "note=Static PIC library only. Linked into FFmpeg. The x264 program is not installed."
} > "$OUTPUT_DIR/NVG-X264.txt"

echo "==> installed static x264 $X264_COMMIT into $OUTPUT_DIR"
