#!/bin/sh
# Build pinned frei0r plugins into <output-dir>.
#
# Usage: build-frei0r.sh <output-dir> [scratch-dir]
#
# Plugins land in <output-dir>/lib/frei0r-1/. OpenCV, Cairo and gavl plugins
# are not built, so the plugins do not link those libraries. On macOS the
# CMake MODULE suffix is .so, and FFmpeg's frei0r wrapper dlopens .dylib, so
# each plugin is also copied to that name.
# Needs git, cc, c++ and cmake. No sudo, apt or brew.

set -eu

FREI0R_REPO="https://github.com/dyne/frei0r.git"
FREI0R_TAG="v2.3.3"
FREI0R_COMMIT="cdeddc7553bbfdc446c61d1bfa6a51bcc029b4a8"
FREI0R_LICENSE="GPL-2.0-or-later"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi

OUTPUT_DIR=$1
if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-frei0r)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)
SRC_DIR="$SCRATCH_DIR/frei0r"

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
    echo "error: cmake not found. Refusing to install one." >&2
    return 1
}

CC=${CC:-cc}
CXX=${CXX:-c++}
command -v git >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v "$CXX" >/dev/null 2>&1 || { echo "error: C++ compiler '$CXX' not found" >&2; exit 1; }
if [ -z "${MAKE:-}" ]; then
    if [ -x /usr/bin/make ]; then
        MAKE=/usr/bin/make
    fi
fi
command -v "${MAKE:-make}" >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }
MAKE_BIN=$(command -v "${MAKE:-make}")
CMAKE_BIN=$(pick_cmake)

SIGNATURE="commit=$FREI0R_COMMIT tag=$FREI0R_TAG no-opencv no-cairo no-gavl"
mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)
if [ -f "$OUTPUT_DIR/NVG-FREI0R.txt" ] && grep -F -q "signature=$SIGNATURE" "$OUTPUT_DIR/NVG-FREI0R.txt"; then
    if [ -e "$OUTPUT_DIR/lib/frei0r-1/nervous.so" ] || [ -e "$OUTPUT_DIR/lib/frei0r-1/nervous.dylib" ]; then
        echo "==> frei0r $FREI0R_TAG already installed in $OUTPUT_DIR"
        exit 0
    fi
fi

echo "==> fetching $FREI0R_REPO $FREI0R_TAG into $SRC_DIR"
if [ ! -d "$SRC_DIR/.git" ]; then
    rm -rf "$SRC_DIR"
    git clone --depth 1 --branch "$FREI0R_TAG" "$FREI0R_REPO" "$SRC_DIR"
fi
git -C "$SRC_DIR" checkout -q "$FREI0R_COMMIT"
ACTUAL=$(git -C "$SRC_DIR" rev-parse HEAD)
if [ "$ACTUAL" != "$FREI0R_COMMIT" ]; then
    echo "error: HEAD is $ACTUAL, expected $FREI0R_COMMIT" >&2
    exit 1
fi

rm -rf "$OUTPUT_DIR"
mkdir -p "$OUTPUT_DIR"
BUILD_DIR="$SCRATCH_DIR/frei0r-build"
rm -rf "$BUILD_DIR"

JOBS=${NVG_JOBS:-4}

echo "==> configuring frei0r $FREI0R_TAG ($FREI0R_COMMIT)"
# CMAKE_DISABLE_FIND_PACKAGE_* stops a Homebrew or distro Cairo/OpenCV/gavl
# from being linked. The cmake binary may itself live under Homebrew; the
# plugin audit rejects any /opt/homebrew or /usr/local dependency.
# frei0r 2.3.3 declares cmake_minimum_required below 3.5. CMake 4
# refuses that unless this policy floor is set. It does not patch the source.
"$CMAKE_BIN" -S "$SRC_DIR" -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DCMAKE_INSTALL_PREFIX="$OUTPUT_DIR" \
    -DCMAKE_INSTALL_LIBDIR=lib \
    -DCMAKE_C_COMPILER="$CC" \
    -DCMAKE_CXX_COMPILER="$CXX" \
    -DCMAKE_MAKE_PROGRAM="$MAKE_BIN" \
    -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
    -DCMAKE_FIND_USE_SYSTEM_ENVIRONMENT_PATH=OFF \
    -DCMAKE_IGNORE_PREFIX_PATH="/opt/homebrew;/usr/local" \
    -DWITHOUT_OPENCV=ON \
    -DWITHOUT_GAVL=ON \
    -DCMAKE_DISABLE_FIND_PACKAGE_Cairo=ON \
    -DCMAKE_DISABLE_FIND_PACKAGE_OpenCV=ON

echo "==> building frei0r with -j$JOBS"
"$CMAKE_BIN" --build "$BUILD_DIR" --parallel "$JOBS"
"$CMAKE_BIN" --install "$BUILD_DIR"

PLUGIN_DIR="$OUTPUT_DIR/lib/frei0r-1"
[ -d "$PLUGIN_DIR" ] || { echo "error: $PLUGIN_DIR missing" >&2; exit 1; }

if [ "$(uname -s)" = "Darwin" ]; then
    for plugin in "$PLUGIN_DIR"/*.so; do
        [ -f "$plugin" ] || continue
        base=$(basename "$plugin" .so)
        if [ ! -e "$PLUGIN_DIR/$base.dylib" ]; then
            cp "$plugin" "$PLUGIN_DIR/$base.dylib"
        fi
    done
fi

if [ ! -e "$PLUGIN_DIR/nervous.so" ] && [ ! -e "$PLUGIN_DIR/nervous.dylib" ]; then
    echo "error: nervous plugin was not built" >&2
    exit 1
fi
if [ ! -e "$PLUGIN_DIR/colorize.so" ] && [ ! -e "$PLUGIN_DIR/colorize.dylib" ]; then
    echo "error: colorize plugin was not built" >&2
    exit 1
fi

if [ -f "$SRC_DIR/COPYING" ]; then
    cp "$SRC_DIR/COPYING" "$OUTPUT_DIR/COPYING"
fi

if [ "$(uname -s)" = "Linux" ]; then
    REPO_ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
    python3 "$REPO_ROOT/tools/package/linux/set_rpath_origin.py" "$PLUGIN_DIR" "$BUILD_DIR" '$ORIGIN'
fi

{
    echo "tag=$FREI0R_TAG"
    echo "commit=$FREI0R_COMMIT"
    echo "license=$FREI0R_LICENSE"
    echo "source=$FREI0R_REPO"
    echo "signature=$SIGNATURE"
    echo "plugins=$PLUGIN_DIR"
    echo "note=OpenCV, Cairo and gavl plugins are off. On macOS each .so is also copied to .dylib because FFmpeg dlopens the platform suffix. nervous.cpp is GPL-3.0-or-later; other plugins are GPL-2.0-or-later."
} > "$OUTPUT_DIR/NVG-FREI0R.txt"

echo "==> installed frei0r $FREI0R_TAG into $OUTPUT_DIR"
find "$PLUGIN_DIR" -maxdepth 1 -name 'nervous.*' -o -name 'colorize.*'
