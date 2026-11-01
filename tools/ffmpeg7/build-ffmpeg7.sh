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
#
# When NVG_X264_PREFIX is set, this is one build for the shared libraries
# and the ffmpeg/ffprobe programs: --enable-gpl --enable-libx264, linked
# against that static x264. When NVG_FREI0R_PREFIX is set, the frei0r
# filter wrapper is enabled and looks for frei0r.h there. The plugins
# themselves are loaded at runtime from FREI0R_PATH.
# Needs git, cc and make. pkg-config is required only for libx264.
# No sudo, apt or brew.

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

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)

X264_PIN=""
FREI0R_PIN=""
if [ -n "${NVG_X264_PREFIX:-}" ] && [ -f "$NVG_X264_PREFIX/NVG-X264.txt" ]; then
    X264_PIN=$(sed -n 's/^commit=//p' "$NVG_X264_PREFIX/NVG-X264.txt")
fi
if [ -n "${NVG_FREI0R_PREFIX:-}" ] && [ -f "$NVG_FREI0R_PREFIX/NVG-FREI0R.txt" ]; then
    FREI0R_PIN=$(sed -n 's/^commit=//p' "$NVG_FREI0R_PREFIX/NVG-FREI0R.txt")
fi

ASM_FLAG=""
if ! command -v nasm >/dev/null 2>&1; then
    echo "==> nasm not found; configuring --disable-x86asm"
    ASM_FLAG="--disable-x86asm"
fi

# Same rule as x264: uname stays arm64 when CC is `clang -arch x86_64`.
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
ARCH_FLAG=""
if [ "$target_arch" != "$host_arch" ]; then
    case $target_arch in
        x86_64) ARCH_FLAG="--arch=x86_64" ;;
        arm64) ARCH_FLAG="--arch=aarch64" ;;
        *)
            echo "error: unsupported FFmpeg target arch $target_arch" >&2
            exit 1
            ;;
    esac
    echo "==> cross compile FFmpeg for $target_arch on $host_arch ($ARCH_FLAG)"
fi

JOBS=${NVG_JOBS:-4}

FFMPEG_SIGNATURE="tag=$FFMPEG_TAG arch=$target_arch x264=${X264_PIN:-none} frei0r=${FREI0R_PIN:-none} asm=$ASM_FLAG"
if [ -f "$OUTPUT_DIR/NVG-FFMPEG.txt" ] && grep -F -q "signature=$FFMPEG_SIGNATURE" "$OUTPUT_DIR/NVG-FFMPEG.txt"; then
    if [ -e "$OUTPUT_DIR/lib/libavcodec.61.dylib" ] || [ -e "$OUTPUT_DIR/lib/libavcodec.so.61" ]; then
        if [ -z "${NVG_X264_PREFIX:-}" ] || [ -x "$OUTPUT_DIR/bin/ffmpeg" ]; then
            echo "==> FFmpeg $FFMPEG_TAG already installed in $OUTPUT_DIR"
            exit 0
        fi
    fi
fi

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

PROGRAM_FLAGS="--disable-programs"
X264_FLAGS=""
FREI0R_FLAGS=""
EXTRA_CFLAGS=""
CONFIGURE_NOTE="--enable-shared --disable-static --disable-programs --enable-gpl --enable-version3 --enable-rpath"
if [ -n "${NVG_X264_PREFIX:-}" ]; then
    if [ ! -f "$NVG_X264_PREFIX/lib/pkgconfig/x264.pc" ]; then
        echo "error: $NVG_X264_PREFIX has no lib/pkgconfig/x264.pc" >&2
        exit 1
    fi
    PROGRAM_FLAGS="--disable-ffplay --disable-sdl2"
    X264_FLAGS="--enable-libx264"
    CONFIGURE_NOTE="--enable-shared --disable-static --disable-ffplay --enable-gpl --enable-version3 --enable-libx264 --enable-rpath"
fi
if [ -n "${NVG_FREI0R_PREFIX:-}" ]; then
    if [ ! -f "$NVG_FREI0R_PREFIX/include/frei0r.h" ]; then
        echo "error: $NVG_FREI0R_PREFIX has no include/frei0r.h" >&2
        exit 1
    fi
    FREI0R_FLAGS="--enable-frei0r"
    EXTRA_CFLAGS="-I$NVG_FREI0R_PREFIX/include"
    CONFIGURE_NOTE="$CONFIGURE_NOTE --enable-frei0r"
fi

# configure reads --cc, not the CC environment variable. Without this it
# runs `gcc`, which on an arm64 Mac links arm64 and rejects an x86_64 libx264.
CC_FLAG="--cc=$CC"
HOST_CC_FLAG=""
EXTRA_LDFLAGS=""
if [ -n "$ARCH_FLAG" ]; then
    EXTRA_CFLAGS="${EXTRA_CFLAGS:+$EXTRA_CFLAGS }-arch $target_arch"
    EXTRA_LDFLAGS="-arch $target_arch"
    HOST_CC_FLAG="--host-cc=/usr/bin/cc"
    CONFIGURE_NOTE="$CONFIGURE_NOTE $ARCH_FLAG --extra-cflags=-arch $target_arch --extra-ldflags=-arch $target_arch"
fi

pick_pkg_config() {
    if [ -n "${NVG_PKG_CONFIG:-}" ] && [ -x "$NVG_PKG_CONFIG" ]; then
        printf '%s\n' "$NVG_PKG_CONFIG"
        return 0
    fi
    for candidate in /usr/bin/pkg-config /opt/homebrew/bin/pkg-config; do
        if [ -x "$candidate" ]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done
    echo "error: pkg-config not found, needed for libx264. Refusing to install one." >&2
    return 1
}

PKG_WRAP=""
if [ -n "${NVG_X264_PREFIX:-}" ]; then
    REAL_PKG_CONFIG=$(pick_pkg_config)
    PKG_WRAP="$SCRATCH_DIR/bin"
    mkdir -p "$PKG_WRAP"
    cat > "$PKG_WRAP/pkg-config" <<EOF
#!/bin/sh
export PKG_CONFIG_LIBDIR="$NVG_X264_PREFIX/lib/pkgconfig"
export PKG_CONFIG_PATH="\$PKG_CONFIG_LIBDIR"
unset PKG_CONFIG_SYSROOT_DIR
exec "$REAL_PKG_CONFIG" "\$@"
EOF
    chmod 755 "$PKG_WRAP/pkg-config"
fi

echo "==> configuring $FFMPEG_TAG ($COMMIT) prefix $OUTPUT_DIR"
# shellcheck disable=SC2086
(
    cd "$SRC_DIR"
    if [ -n "$PKG_WRAP" ]; then
        PATH="$PKG_WRAP:/usr/bin:/bin:/usr/sbin:/sbin"
        export PATH
        export PKG_CONFIG_LIBDIR="$NVG_X264_PREFIX/lib/pkgconfig"
        export PKG_CONFIG_PATH="$PKG_CONFIG_LIBDIR"
    fi
    ./configure \
        --prefix="$OUTPUT_DIR" \
        --libdir="$OUTPUT_DIR/lib" \
        --enable-shared \
        --disable-static \
        --disable-doc \
        --disable-debug \
        --enable-gpl \
        --enable-version3 \
        --enable-rpath \
        "$CC_FLAG" \
        $HOST_CC_FLAG \
        $PROGRAM_FLAGS \
        $X264_FLAGS \
        $FREI0R_FLAGS \
        $ASM_FLAG \
        $ARCH_FLAG \
        ${EXTRA_CFLAGS:+--extra-cflags="$EXTRA_CFLAGS"} \
        ${EXTRA_LDFLAGS:+--extra-ldflags="$EXTRA_LDFLAGS"}
)

echo "==> building with -j$JOBS"
make -C "$SRC_DIR" -j"$JOBS"
make -C "$SRC_DIR" install

{
    echo "tag=$FFMPEG_TAG"
    echo "commit=$COMMIT"
    echo "license=GPL-3.0-or-later"
    echo "configure=$CONFIGURE_NOTE $ASM_FLAG $ARCH_FLAG"
    echo "arch=$target_arch"
    echo "source=$FFMPEG_REPO"
    echo "signature=$FFMPEG_SIGNATURE"
    if [ -n "${NVG_X264_PREFIX:-}" ]; then
        echo "note=One GPL build: shared libraries plus bin/ffmpeg and bin/ffprobe, linked with static libx264. Do not alias system SONAMEs."
    else
        echo "note=Shared libraries only. Load libavcodec.so.61 / libavcodec.61.dylib by absolute path. Do not alias system SONAMEs."
    fi
} > "$OUTPUT_DIR/NVG-FFMPEG.txt"

if [ -x "$OUTPUT_DIR/bin/ffmpeg" ]; then
    echo "==> checking encoders and stock filters"
    ENC=$("$OUTPUT_DIR/bin/ffmpeg" -hide_banner -encoders)
    COD=$("$OUTPUT_DIR/bin/ffmpeg" -hide_banner -codecs)
    FIL=$("$OUTPUT_DIR/bin/ffmpeg" -hide_banner -filters)
    for name in libx264 aac; do
        printf '%s\n' "$ENC" | grep -E "(^|[[:space:]])$name([[:space:]]|$)" >/dev/null \
            || { echo "error: encoder $name missing" >&2; exit 1; }
    done
    for name in h264 aac pcm_s16le; do
        printf '%s\n' "$COD" | grep -E "(^|[[:space:]])$name([[:space:]]|$)" >/dev/null \
            || { echo "error: codec $name missing" >&2; exit 1; }
    done
    for name in aresample concat reverse areverse setpts atempo scale fps volume amix overlay colorkey; do
        printf '%s\n' "$FIL" | grep -E "(^|[[:space:]])$name([[:space:]]|$)" >/dev/null \
            || { echo "error: filter $name missing" >&2; exit 1; }
    done
    if [ -n "${NVG_FREI0R_PREFIX:-}" ]; then
        printf '%s\n' "$FIL" | grep -E '(^|[[:space:]])frei0r([[:space:]]|$)' >/dev/null \
            || { echo "error: filter frei0r missing" >&2; exit 1; }
    fi
fi

if [ -f "$SRC_DIR/COPYING.GPLv3" ]; then
    cp "$SRC_DIR/COPYING.GPLv3" "$OUTPUT_DIR/LICENSE.GPLv3"
fi
if [ -f "$SRC_DIR/COPYING.LGPLv2.1" ]; then
    cp "$SRC_DIR/COPYING.LGPLv2.1" "$OUTPUT_DIR/LICENSE.LGPLv2.1"
fi

if [ "$(uname -s)" = "Darwin" ]; then
    if [ -x "$OUTPUT_DIR/bin/ffmpeg" ]; then
        check_bin="$OUTPUT_DIR/bin/ffmpeg"
    else
        check_bin="$OUTPUT_DIR/lib/libavcodec.61.dylib"
    fi
    got=$(lipo -archs "$check_bin" | tr -d '[:space:]')
    if [ "$got" != "$target_arch" ]; then
        echo "error: $check_bin is [$got], expected $target_arch" >&2
        exit 1
    fi
fi

echo "==> installed FFmpeg $FFMPEG_TAG into $OUTPUT_DIR"
find "$OUTPUT_DIR/lib" -maxdepth 1 -name 'libavcodec*' -o -name 'avcodec*' | head
