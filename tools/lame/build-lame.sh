#!/bin/sh
# Build pinned LAME as a static library into <output-dir>.
#
# Usage: build-lame.sh <output-dir> [scratch-dir]
#
# The lame CLI is not installed. FFmpeg links libmp3lame.a for the
# libmp3lame MP3 encoder. Needs curl, cc and make. No sudo, apt or brew.
# Does not copy Homebrew.

set -eu

LAME_VERSION="3.100"
LAME_SHA256="ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e"
LAME_URL="https://downloads.sourceforge.net/project/lame/lame/3.100/lame-3.100.tar.gz"
LAME_LICENSE="LGPL-2.0-only"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi

OUTPUT_DIR=$1
if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-lame)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)
SRC_DIR="$SCRATCH_DIR/lame-$LAME_VERSION"

CC=${CC:-cc}
command -v curl >/dev/null 2>&1 || { echo "error: curl not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v make >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }

# config.guess uses uname. On an arm64 Mac, `clang -arch x86_64 -dumpmachine`
# is x86_64 while uname stays arm64, so the target has to come from the compiler.
host_arch=$(uname -m)
case $host_arch in
    aarch64) host_arch=arm64 ;;
    amd64) host_arch=x86_64 ;;
esac
target_arch=$host_arch
cc_triple=$("$CC" -dumpmachine 2>/dev/null || true)
case $cc_triple in
    x86_64*) target_arch=x86_64 ;;
    aarch64*|arm64*) target_arch=arm64 ;;
esac
if [ -n "${NVG_MACH_ARCH:-}" ]; then
    target_arch=$NVG_MACH_ARCH
fi

HOST_FLAGS=""
if [ "$target_arch" != "$host_arch" ]; then
    [ -n "$cc_triple" ] || { echo "error: $CC -dumpmachine failed" >&2; exit 1; }
    build_triple=$(/usr/bin/cc -dumpmachine)
    HOST_FLAGS="--host=$cc_triple --build=$build_triple"
    echo "==> cross compile LAME for $target_arch on $host_arch ($HOST_FLAGS)"
fi

# FFmpeg is a shared library. A non-PIC libmp3lame.a fails to link into
# libavcodec.so on Linux (R_X86_64_PC32 against stderr).
SIGNATURE="version=$LAME_VERSION sha256=$LAME_SHA256 arch=$target_arch static=yes frontend=no pic=yes"
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)
if [ -f "$OUTPUT_DIR/NVG-LAME.txt" ] && grep -F -q "signature=$SIGNATURE" "$OUTPUT_DIR/NVG-LAME.txt"; then
    if [ -f "$OUTPUT_DIR/lib/libmp3lame.a" ] && [ -f "$OUTPUT_DIR/include/lame/lame.h" ]; then
        echo "==> LAME $LAME_VERSION already installed in $OUTPUT_DIR"
        exit 0
    fi
fi

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    else
        sha256sum "$1" | awk '{ print $1 }'
    fi
}

TARBALL="$SCRATCH_DIR/lame-$LAME_VERSION.tar.gz"
if [ -n "${NVG_SRC_CACHE:-}" ] && [ -f "$NVG_SRC_CACHE/lame-$LAME_VERSION.tar.gz" ]; then
    cp "$NVG_SRC_CACHE/lame-$LAME_VERSION.tar.gz" "$TARBALL"
else
    echo "==> download $LAME_URL"
    curl -fL --retry 3 --retry-delay 2 -o "$TARBALL" "$LAME_URL"
fi
got=$(sha256_file "$TARBALL")
if [ "$got" != "$LAME_SHA256" ]; then
    echo "error: checksum mismatch for lame-$LAME_VERSION.tar.gz" >&2
    echo "  expected $LAME_SHA256" >&2
    echo "  got      $got" >&2
    exit 1
fi
echo "==> checksum ok lame-$LAME_VERSION.tar.gz"

rm -rf "$SRC_DIR"
tar -xzf "$TARBALL" -C "$SCRATCH_DIR"
if [ ! -d "$SRC_DIR" ]; then
    echo "error: $SRC_DIR was not unpacked" >&2
    exit 1
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"

JOBS=${NVG_JOBS:-4}

echo "==> configuring LAME $LAME_VERSION prefix $OUTPUT_DIR"
# shellcheck disable=SC2086
(
    cd "$SRC_DIR"
    CFLAGS="${CFLAGS:-} -fPIC" \
    ./configure \
        --prefix="$OUTPUT_DIR" \
        --enable-static \
        --disable-shared \
        --disable-frontend \
        --disable-decoder \
        --disable-gtktest \
        $HOST_FLAGS
)

echo "==> building LAME with -j$JOBS"
make -C "$SRC_DIR" -j"$JOBS"
make -C "$SRC_DIR" install

if [ ! -f "$OUTPUT_DIR/lib/libmp3lame.a" ]; then
    echo "error: $OUTPUT_DIR/lib/libmp3lame.a was not installed" >&2
    exit 1
fi
if [ ! -f "$OUTPUT_DIR/include/lame/lame.h" ]; then
    echo "error: $OUTPUT_DIR/include/lame/lame.h was not installed" >&2
    exit 1
fi
if [ -f "$SRC_DIR/COPYING" ]; then
    cp "$SRC_DIR/COPYING" "$OUTPUT_DIR/COPYING"
fi

if [ "$(uname -s)" = "Darwin" ]; then
    got_arch=$(lipo -archs "$OUTPUT_DIR/lib/libmp3lame.a" | tr -d '[:space:]')
    if [ "$got_arch" != "$target_arch" ]; then
        echo "error: libmp3lame.a is [$got_arch], expected $target_arch" >&2
        exit 1
    fi
fi
if [ "$(uname -s)" = "Linux" ]; then
    pic_out="$SCRATCH_DIR/libmp3lame-pic.so"
    if ! "$CC" -shared -o "$pic_out" \
        -Wl,--whole-archive "$OUTPUT_DIR/lib/libmp3lame.a" \
        -Wl,--no-whole-archive -lm; then
        echo "error: libmp3lame.a is not position-independent and cannot link into shared libavcodec" >&2
        exit 1
    fi
    rm -f "$pic_out"
fi

{
    echo "version=$LAME_VERSION"
    echo "sha256=$LAME_SHA256"
    echo "license=$LAME_LICENSE"
    echo "source=$LAME_URL"
    echo "signature=$SIGNATURE"
    echo "arch=$target_arch"
    echo "configure=--enable-static --disable-shared --disable-frontend --disable-decoder --disable-gtktest CFLAGS=-fPIC $HOST_FLAGS"
    echo "note=Static library only. Linked into FFmpeg for libmp3lame. The lame program is not installed. Licence is the GNU Library General Public License, version 2."
} > "$OUTPUT_DIR/NVG-LAME.txt"

echo "==> installed static LAME $LAME_VERSION into $OUTPUT_DIR"
