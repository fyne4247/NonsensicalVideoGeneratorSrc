#!/bin/sh
# Build MonoGame content for DesktopGL into content/bin/DesktopGL/Content.
#
# On an arm64 Mac, MGCB's FreeImage and freetype are x64-only, so this
# runs the user-local osx-x64 SDK under Rosetta (~/.dotnet-nvg-x64).
# On Linux and on an x64 Mac it uses ~/.dotnet-nvg.
# No sudo, apt or brew. The app build on arm64 Mac skips MGCB
# (NvgSkipMgcb) and copies this output when it is present.

set -eu

ROOT=$(CDPATH= cd -- "$(dirname "$0")/.." && pwd)
OUT="$ROOT/content/bin/DesktopGL/Content"
INT="$ROOT/content/obj/DesktopGL/Content"

OS=$(uname -s)
ARCH=$(uname -m)
DOTNET=${DOTNET:-}
RUN=""

if [ "$OS" = "Darwin" ] && [ "$ARCH" = "arm64" ]; then
    if [ -z "$DOTNET" ]; then
        DOTNET="$HOME/.dotnet-nvg-x64/dotnet"
    fi
    RUN="arch -x86_64"
else
    if [ -z "$DOTNET" ]; then
        DOTNET="$HOME/.dotnet-nvg/dotnet"
    fi
fi

if [ ! -x "$DOTNET" ]; then
    echo "error: dotnet not found at $DOTNET" >&2
    exit 1
fi

echo "==> content via $RUN $DOTNET"
echo "==> output $OUT"
cd "$ROOT"
# shellcheck disable=SC2086
$RUN "$DOTNET" tool restore
# shellcheck disable=SC2086
$RUN "$DOTNET" tool run mgcb -- \
    "/@:$ROOT/content/Content.DesktopGL.mgcb" \
    /platform:DesktopGL \
    "/outputDir:$OUT" \
    "/intermediateDir:$INT" \
    "/workingDir:$ROOT/content"

echo "==> content build finished"
find "$OUT" -name '*.xnb' | wc -l
