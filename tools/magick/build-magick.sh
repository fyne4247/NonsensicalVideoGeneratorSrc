#!/bin/sh
# Build a static ImageMagick 7 magick for macOS into <output-dir>/bin/magick.
#
# Usage: build-magick.sh <output-dir> [scratch-dir]
#
# zlib, libpng and libjpeg-turbo are built from pinned tarballs first and
# linked statically. No X11, no fontconfig, no Homebrew libraries. Linux
# packages keep the official AppImage instead of this script.
# Needs curl, cc, c++, make, python3 and cmake (libjpeg-turbo). No sudo or brew.

set -eu

ZLIB_VERSION="1.3.1"
ZLIB_SHA256="9a93b2b7dfdac77ceba5a558a580e74667dd6fede4585b91eefb60f03b72df23"
ZLIB_URL="https://github.com/madler/zlib/releases/download/v1.3.1/zlib-1.3.1.tar.gz"

LIBPNG_VERSION="1.6.47"
LIBPNG_SHA256="084115c62fe023e3d88cd78764a4d8e89763985ee4b4a085825f7a00d85eafbb"
LIBPNG_URL="https://downloads.sourceforge.net/project/libpng/libpng16/1.6.47/libpng-1.6.47.tar.gz"

JPEG_VERSION="3.1.0"
JPEG_SHA256="9564c72b1dfd1d6fe6274c5f95a8d989b59854575d4bbee44ade7bc17aa9bc93"
JPEG_URL="https://github.com/libjpeg-turbo/libjpeg-turbo/releases/download/3.1.0/libjpeg-turbo-3.1.0.tar.gz"

MAGICK_VERSION="7.1.2-34"
MAGICK_COMMIT="a0c4bf729a26a5c2feb28b0365b47a8db736078a"
MAGICK_SHA256="55fe0ec246245b46c429f9e6bc65294ad4a78a66b88abd9817971ca157856323"
MAGICK_URL="https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34.tar.xz"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi
if [ "$(uname -s)" != "Darwin" ]; then
    echo "error: build-magick.sh is the macOS source build. Linux ships the ImageMagick AppImage." >&2
    exit 1
fi

OUTPUT_DIR=$1
if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-magick)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)

CC=${CC:-/usr/bin/cc}
CXX=${CXX:-/usr/bin/c++}
command -v curl >/dev/null 2>&1 || { echo "error: curl not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v "$CXX" >/dev/null 2>&1 || { echo "error: C++ compiler '$CXX' not found" >&2; exit 1; }
command -v make >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { echo "error: python3 not found" >&2; exit 1; }

pick_cmake() {
    if [ -n "${CMAKE:-}" ] && [ -x "$CMAKE" ]; then
        printf '%s\n' "$CMAKE"
        return 0
    fi
    for candidate in /usr/bin/cmake /opt/homebrew/bin/cmake; do
        if [ -x "$candidate" ]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done
    echo "error: cmake not found. libjpeg-turbo 3.1.0 is built with cmake. Refusing to install one." >&2
    return 1
}
CMAKE_BIN=$(pick_cmake)

pick_pkg_config() {
    if [ -n "${PKG_CONFIG:-}" ] && [ -x "$PKG_CONFIG" ]; then
        printf '%s\n' "$PKG_CONFIG"
        return 0
    fi
    for candidate in /usr/bin/pkg-config /opt/homebrew/bin/pkg-config; do
        if [ -x "$candidate" ]; then
            printf '%s\n' "$candidate"
            return 0
        fi
    done
    echo "error: pkg-config not found. Refusing to install one." >&2
    return 1
}
PKG_CONFIG_BIN=$(pick_pkg_config)

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    else
        sha256sum "$1" | awk '{ print $1 }'
    fi
}

fetch() {
    name=$1
    url=$2
    expect=$3
    dest="$SCRATCH_DIR/$name"
    if [ -n "${NVG_SRC_CACHE:-}" ] && [ -f "$NVG_SRC_CACHE/$name" ]; then
        cp "$NVG_SRC_CACHE/$name" "$dest"
    else
        echo "==> download $url"
        curl -fL --retry 3 --retry-delay 2 -o "$dest" "$url"
    fi
    got=$(sha256_file "$dest")
    if [ "$got" != "$expect" ]; then
        echo "error: checksum mismatch for $name" >&2
        echo "  expected $expect" >&2
        echo "  got      $got" >&2
        exit 1
    fi
    echo "==> checksum ok $name"
}

SIGNATURE="magick=$MAGICK_VERSION zlib=$ZLIB_VERSION png=$LIBPNG_VERSION jpeg=$JPEG_VERSION static=yes"
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)
if [ -f "$OUTPUT_DIR/NVG-MAGICK.txt" ] && grep -F -q "signature=$SIGNATURE" "$OUTPUT_DIR/NVG-MAGICK.txt"; then
    if [ -x "$OUTPUT_DIR/bin/magick" ]; then
        echo "==> ImageMagick $MAGICK_VERSION already installed in $OUTPUT_DIR"
        exit 0
    fi
fi

JOBS=${NVG_JOBS:-4}
fetch "zlib-$ZLIB_VERSION.tar.gz" "$ZLIB_URL" "$ZLIB_SHA256"
fetch "libpng-$LIBPNG_VERSION.tar.gz" "$LIBPNG_URL" "$LIBPNG_SHA256"
fetch "libjpeg-turbo-$JPEG_VERSION.tar.gz" "$JPEG_URL" "$JPEG_SHA256"
fetch "ImageMagick-$MAGICK_VERSION.tar.xz" "$MAGICK_URL" "$MAGICK_SHA256"

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
DEPS="$SCRATCH_DIR/deps"
rm -rf "$DEPS"
mkdir -p "$DEPS"

echo "==> zlib $ZLIB_VERSION"
tar -xf "$SCRATCH_DIR/zlib-$ZLIB_VERSION.tar.gz" -C "$SCRATCH_DIR"
(
    cd "$SCRATCH_DIR/zlib-$ZLIB_VERSION"
    ./configure --prefix="$DEPS" --static
    make -j"$JOBS"
    make install
) || {
    echo "error: delegate zlib $ZLIB_VERSION could not be built" >&2
    exit 1
}

# pkg-config sees only the delegates we just built.
WRAP="$SCRATCH_DIR/bin"
mkdir -p "$WRAP"
cat > "$WRAP/pkg-config" <<EOF
#!/bin/sh
export PKG_CONFIG_LIBDIR="$DEPS/lib/pkgconfig"
export PKG_CONFIG_PATH="\$PKG_CONFIG_LIBDIR"
unset PKG_CONFIG_SYSROOT_DIR
exec "$PKG_CONFIG_BIN" "\$@"
EOF
chmod 755 "$WRAP/pkg-config"

echo "==> libpng $LIBPNG_VERSION"
tar -xf "$SCRATCH_DIR/libpng-$LIBPNG_VERSION.tar.gz" -C "$SCRATCH_DIR"
(
    cd "$SCRATCH_DIR/libpng-$LIBPNG_VERSION"
    export PATH="$WRAP:/usr/bin:/bin:/usr/sbin:/sbin"
    export CC="$CC"
    export CPPFLAGS="-I$DEPS/include"
    export LDFLAGS="-L$DEPS/lib"
    export PKG_CONFIG_LIBDIR="$DEPS/lib/pkgconfig"
    export PKG_CONFIG_PATH="$DEPS/lib/pkgconfig"
    ./configure --prefix="$DEPS" --disable-shared --enable-static --with-zlib-prefix="$DEPS"
    make -j"$JOBS"
    make install
) || {
    echo "error: delegate libpng $LIBPNG_VERSION could not be built" >&2
    exit 1
}

echo "==> libjpeg-turbo $JPEG_VERSION"
tar -xf "$SCRATCH_DIR/libjpeg-turbo-$JPEG_VERSION.tar.gz" -C "$SCRATCH_DIR"
"$CMAKE_BIN" -S "$SCRATCH_DIR/libjpeg-turbo-$JPEG_VERSION" -B "$SCRATCH_DIR/jpeg-build" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$DEPS" \
    -DCMAKE_C_COMPILER="$CC" \
    -DCMAKE_MAKE_PROGRAM="${MAKE:-/usr/bin/make}" \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DENABLE_SHARED=OFF \
    -DENABLE_STATIC=ON \
    -DWITH_TURBOJPEG=OFF \
    -DWITH_JAVA=OFF \
    -DCMAKE_FIND_USE_SYSTEM_ENVIRONMENT_PATH=OFF \
    -DCMAKE_IGNORE_PREFIX_PATH="/opt/homebrew;/usr/local" \
    || {
        echo "error: delegate libjpeg-turbo $JPEG_VERSION could not be configured" >&2
        exit 1
    }
"$CMAKE_BIN" --build "$SCRATCH_DIR/jpeg-build" --parallel "$JOBS" || {
    echo "error: delegate libjpeg-turbo $JPEG_VERSION could not be built" >&2
    exit 1
}
"$CMAKE_BIN" --install "$SCRATCH_DIR/jpeg-build"

echo "==> ImageMagick $MAGICK_VERSION"
tar -xf "$SCRATCH_DIR/ImageMagick-$MAGICK_VERSION.tar.xz" -C "$SCRATCH_DIR"
(
    cd "$SCRATCH_DIR/ImageMagick-$MAGICK_VERSION"
    export PATH="$WRAP:/usr/bin:/bin:/usr/sbin:/sbin"
    export CC="$CC"
    export CXX="$CXX"
    export CPPFLAGS="-I$DEPS/include"
    export LDFLAGS="-L$DEPS/lib"
    export PKG_CONFIG_LIBDIR="$DEPS/lib/pkgconfig"
    export PKG_CONFIG_PATH="$DEPS/lib/pkgconfig"
    ./configure \
        --prefix="$OUTPUT_DIR" \
        --disable-shared \
        --enable-static \
        --disable-openmp \
        --disable-hdri \
        --with-quantum-depth=16 \
        --without-modules \
        --without-x \
        --without-perl \
        --without-magick-plus-plus \
        --without-fontconfig \
        --without-freetype \
        --without-xml \
        --without-lcms \
        --without-webp \
        --without-heic \
        --without-raw \
        --without-tiff \
        --without-lzma \
        --without-bzlib \
        --without-zstd \
        --without-pango \
        --without-rsvg \
        --without-jxl \
        --without-openjp2 \
        --without-djvu \
        --without-gvc \
        --without-raqm \
        --with-zlib \
        --with-png \
        --with-jpeg \
        --disable-docs
    make -j"$JOBS"
    make install
) || {
    echo "error: ImageMagick $MAGICK_VERSION could not be built. PNG and JPEG are the required delegates." >&2
    exit 1
}

if [ ! -x "$OUTPUT_DIR/bin/magick" ]; then
    echo "error: $OUTPUT_DIR/bin/magick missing after install" >&2
    exit 1
fi

LIC="$OUTPUT_DIR/LICENSES"
mkdir -p "$LIC"
cp "$SCRATCH_DIR/zlib-$ZLIB_VERSION/README" "$LIC/zlib-README"
cp "$SCRATCH_DIR/libpng-$LIBPNG_VERSION/LICENSE" "$LIC/libpng-LICENSE"
cp "$SCRATCH_DIR/libjpeg-turbo-$JPEG_VERSION/LICENSE.md" "$LIC/libjpeg-turbo-LICENSE.md"
cp "$SCRATCH_DIR/ImageMagick-$MAGICK_VERSION/LICENSE" "$LIC/ImageMagick-LICENSE"

{
    echo "version=$MAGICK_VERSION"
    echo "commit=$MAGICK_COMMIT"
    echo "license=ImageMagick"
    echo "source=$MAGICK_URL"
    echo "sha256=$MAGICK_SHA256"
    echo "signature=$SIGNATURE"
    echo "zlib=$ZLIB_VERSION $ZLIB_SHA256 $ZLIB_URL license=zlib"
    echo "libpng=$LIBPNG_VERSION $LIBPNG_SHA256 $LIBPNG_URL license=libpng"
    echo "libjpeg-turbo=$JPEG_VERSION $JPEG_SHA256 $JPEG_URL license=BSD-3-Clause-AND-IJG-AND-zlib"
    echo "note=Static magick. Delegates are zlib, libpng and libjpeg-turbo, built from the pinned tarballs. No X11 and no Homebrew libraries."
} > "$OUTPUT_DIR/NVG-MAGICK.txt"

echo "==> installed ImageMagick $MAGICK_VERSION into $OUTPUT_DIR"
