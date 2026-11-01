#!/bin/sh
# Build the native (non-Windows) replacement for bin/vocoder.exe.
#
# Fetches github.com/borsboom/vocoder (Zerius Vocoder 1.3) at the pinned
# commit below into a scratch directory, builds it with the system C
# compiler, and copies the resulting `vocoder` binary into the directory
# given as the first argument.
#
# Usage: build-vocoder.sh <output-dir> [scratch-dir]
#
# No sudo/apt/brew, no system packages: only git, a C compiler, and make
# are required.

set -eu

VOCODER_REPO="https://github.com/borsboom/vocoder"
VOCODER_COMMIT="3faed27b4cb457c458a1feddc309c3fd0271af5a"

if [ "$#" -lt 1 ]; then
    echo "usage: $0 <output-dir> [scratch-dir]" >&2
    exit 2
fi

OUTPUT_DIR=$1

if [ "$#" -ge 2 ]; then
    SCRATCH_DIR=$2
    mkdir -p "$SCRATCH_DIR"
else
    SCRATCH_DIR=$(mktemp -d 2>/dev/null || mktemp -d -t nvg-vocoder)
fi
SCRATCH_DIR=$(cd "$SCRATCH_DIR" && pwd)
SRC_DIR="$SCRATCH_DIR/vocoder"

CC=${CC:-cc}
command -v git  >/dev/null 2>&1 || { echo "error: git not found" >&2; exit 1; }
command -v "$CC" >/dev/null 2>&1 || { echo "error: C compiler '$CC' not found" >&2; exit 1; }
command -v make >/dev/null 2>&1 || { echo "error: make not found" >&2; exit 1; }

echo "==> fetching $VOCODER_REPO at $VOCODER_COMMIT into $SRC_DIR"
if [ ! -d "$SRC_DIR/.git" ]; then
    git clone -q "$VOCODER_REPO" "$SRC_DIR"
fi
git -C "$SRC_DIR" checkout -q --detach "$VOCODER_COMMIT"
ACTUAL=$(git -C "$SRC_DIR" rev-parse HEAD)
if [ "$ACTUAL" != "$VOCODER_COMMIT" ]; then
    echo "error: fetched commit $ACTUAL does not match pinned $VOCODER_COMMIT" >&2
    exit 1
fi

echo "==> building with CC=$CC"
make -C "$SRC_DIR" clean >/dev/null
make -C "$SRC_DIR" CC="$CC"

mkdir -p "$OUTPUT_DIR"
OUTPUT_DIR=$(cd "$OUTPUT_DIR" && pwd)
cp "$SRC_DIR/vocoder" "$OUTPUT_DIR/vocoder"
echo "==> built $OUTPUT_DIR/vocoder"
"$OUTPUT_DIR/vocoder" </dev/null 2>&1 | head -3 || true
