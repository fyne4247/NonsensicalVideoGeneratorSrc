#!/bin/sh
# Build a self-contained linux-x64 tarball with a user-local installer.
#
# Usage: build-linux-package.sh <outdir>
#
# Run this on an x86_64 Linux machine. No sudo, apt, or brew.

set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
. "$ROOT/tools/package/lib/package-common.sh"

YTDLP_VERSION=2026.10.21
YTDLP_LINUX_SHA=d8bd33fca9d5b23bd5b9952b3fca36db5c46331933fda9f598a3da14ce2c243f
DENO_VERSION=2.9.8
DENO_LINUX_SHA=e890975bc2549807bbcc0b584ffafda72cc7c93349dd99c80483dd784f6c4609
IMAGEMAGICK_VERSION=7.1.2-34
IMAGEMAGICK_SHA=6e9edfd35d5f30b57c27533afe003d6085faaf8607b207426f398d11751acf93

if [ "$#" -ne 1 ]; then
    nvg_die "usage: $0 <outdir>"
fi
if [ "$(uname -s)" != "Linux" ] || [ "$(uname -m)" != "x86_64" ]; then
    nvg_die "build-linux-package.sh builds linux-x64 and must run on Linux x86_64 (this host is $(uname -s) $(uname -m))"
fi

OUTDIR=$1
mkdir -p -- "$OUTDIR"
OUTDIR=$(CDPATH= cd -- "$OUTDIR" && pwd)
VERSION=$(nvg_version)
[ -n "$VERSION" ] || nvg_die "could not read Version from the csproj"
DOTNET=${DOTNET:-$HOME/.dotnet-nvg/dotnet}
[ -x "$DOTNET" ] || nvg_die "dotnet not found at $DOTNET (user-local SDK)"

WORK=$(mktemp -d "${TMPDIR:-/tmp}/nvg-linux.XXXXXX")
FFMPEG_SCRATCH=
printf '==> work %s\n' "$WORK"
cleanup() {
    if [ -n "$FFMPEG_SCRATCH" ] && [ -d "$FFMPEG_SCRATCH" ]; then
        rm -rf "$FFMPEG_SCRATCH"
    fi
}
trap cleanup EXIT

nvg_require_public_locales

printf '==> DesktopGL content\n'
env -u DOTNET /bin/sh "$ROOT/tools/build-content-desktopgl.sh"
XNB_COUNT=$(find "$ROOT/content/bin/DesktopGL/Content" -name '*.xnb' | wc -l | tr -d ' ')
[ "$XNB_COUNT" -gt 0 ] || nvg_die "content build produced no .xnb files"
printf '==> %s xnb files\n' "$XNB_COUNT"

DOTNET_ROOT=$(CDPATH= cd -- "$(dirname -- "$DOTNET")" && pwd)
export DOTNET_ROOT
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1

NAME=NonsensicalVideoGenerator-$VERSION-linux-x64
PKG=$WORK/$NAME
printf '==> publish linux-x64\n'
"$DOTNET" publish "$ROOT/NonsensicalVideoGenerator.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:NvgPlatform=DesktopGL \
    -p:NvgSkipMgcb=true \
    -p:UseAppHost=true \
    -p:PublishSingleFile=false \
    -o "$PKG"
[ -f "$PKG/NonsensicalVideoGenerator" ] || nvg_die "publish did not produce the apphost"
chmod 755 "$PKG/NonsensicalVideoGenerator"
nvg_strip_publish_dir "$PKG"
nvg_assert_no_steam "$PKG"
nvg_assert_no_removed_deps "$PKG"
nvg_require_packaged_locales "$PKG/locales"

DEPS=${NVG_DEPS_DIR:-$WORK/deps}
nvg_build_media_deps "$DEPS" "$WORK/media-src" /usr/bin/cc /usr/bin/c++
FFMPEG_PREFIX=$DEPS/ffmpeg
mkdir -p -- "$PKG/lib" "$PKG/bin"
cp -a "$FFMPEG_PREFIX/lib"/. "$PKG/lib"/
rm -rf "$PKG/lib/pkgconfig"
cp "$FFMPEG_PREFIX/bin/ffmpeg" "$FFMPEG_PREFIX/bin/ffprobe" "$PKG/bin/"
chmod 755 "$PKG/bin/ffmpeg" "$PKG/bin/ffprobe"
TP="$PKG/ThirdParty"
mkdir -p -- "$TP/ffmpeg" "$TP/x264" "$TP/lame" "$TP/frei0r"
cp "$FFMPEG_PREFIX/NVG-FFMPEG.txt" "$TP/ffmpeg/NVG-FFMPEG.txt"
cp "$FFMPEG_PREFIX/LICENSE.GPLv3" "$TP/ffmpeg/LICENSE.GPLv3"
if [ -f "$FFMPEG_PREFIX/LICENSE.LGPLv2.1" ]; then
    cp "$FFMPEG_PREFIX/LICENSE.LGPLv2.1" "$TP/ffmpeg/LICENSE.LGPLv2.1"
fi
cp "$DEPS/x264/NVG-X264.txt" "$DEPS/x264/COPYING" "$TP/x264/"
cp "$DEPS/lame/NVG-LAME.txt" "$DEPS/lame/COPYING" "$TP/lame/"
cp "$DEPS/frei0r/NVG-FREI0R.txt" "$DEPS/frei0r/COPYING" "$TP/frei0r/"
cp "$ROOT/content/desktopgl-music/PROVENANCE.txt" "$TP/desktopgl-music-PROVENANCE.txt"
nvg_stage_frei0r "$DEPS/frei0r" "$PKG/bin/frei0r-1"
python3 "$ROOT/tools/package/linux/set_rpath_origin.py" "$PKG/lib" "$FFMPEG_PREFIX/lib"
python3 "$ROOT/tools/package/linux/set_rpath_origin.py" "$PKG/bin" "$FFMPEG_PREFIX/lib" '$ORIGIN/../lib'
FFMPEG_TAG=$(sed -n 's/^tag=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
FFMPEG_COMMIT=$(sed -n 's/^commit=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
FFMPEG_CONFIGURE=$(sed -n 's/^configure=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
X264_COMMIT=$(sed -n 's/^commit=//p' "$TP/x264/NVG-X264.txt")
X264_ASM=$(sed -n 's/^asm=//p' "$TP/x264/NVG-X264.txt")
LAME_VERSION=$(sed -n 's/^version=//p' "$TP/lame/NVG-LAME.txt")
LAME_SHA=$(sed -n 's/^sha256=//p' "$TP/lame/NVG-LAME.txt")
FREI0R_TAG=$(sed -n 's/^tag=//p' "$TP/frei0r/NVG-FREI0R.txt")
FREI0R_COMMIT=$(sed -n 's/^commit=//p' "$TP/frei0r/NVG-FREI0R.txt")
[ -e "$PKG/lib/libavcodec.so.61" ] || nvg_die "libavcodec.so.61 missing"
[ -x "$PKG/bin/ffmpeg" ] || nvg_die "bin/ffmpeg missing"

printf '==> vocoder\n'
VOCODER_SCRATCH=$WORK/vocoder-src
nvg_build_vocoder "$PKG/bin" "$VOCODER_SCRATCH"
rm -rf "$VOCODER_SCRATCH"
chmod 755 "$PKG/bin/vocoder"
VOCODER_COMMIT=$(sed -n 's/^VOCODER_COMMIT="\(.*\)".*/\1/p' "$ROOT/tools/vocoder/build-vocoder.sh")

BIN=$PKG/bin
mkdir -p -- "$BIN"
YTDLP_RECORD="- Not bundled. Download or checksum failed, so yt-dlp is left for PATH."
DENO_RECORD="- Not bundled. Download or checksum failed, so deno is left for PATH."
MAGICK_RECORD="- Not bundled. The official Linux AppImage was not used. magick is resolved from bin/magick, then PATH."

YTDLP_URL="https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION/yt-dlp_linux"
YTDLP_TMP=$WORK/yt-dlp_linux
if curl -fL --retry 3 --retry-delay 2 -o "$YTDLP_TMP" "$YTDLP_URL"; then
    if nvg_require_sha256 "$YTDLP_TMP" "$YTDLP_LINUX_SHA"; then
        DESC=$(file -b "$YTDLP_TMP")
        OK=$(printf '%s' "$DESC" | grep -c 'x86-64' || true)
        if [ "$OK" -ge 1 ]; then
            cp "$YTDLP_TMP" "$BIN/yt-dlp"
            chmod 755 "$BIN/yt-dlp"
            YTDLP_RECORD="- Version: $YTDLP_VERSION
- URL: $YTDLP_URL
- SHA-256: $YTDLP_LINUX_SHA
- Upstream checksum file: https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION/SHA2-256SUMS
- Licence: The Unlicense. Text shipped as yt-dlp-LICENSE.txt.
- File: bin/yt-dlp
- file(1): $DESC"
        else
            YTDLP_RECORD="- Not bundled. $YTDLP_URL is $DESC, which is not x86-64."
        fi
    fi
fi

DENO_ZIP_NAME=deno-x86_64-unknown-linux-gnu.zip
DENO_URL="https://github.com/denoland/deno/releases/download/v$DENO_VERSION/$DENO_ZIP_NAME"
DENO_TMP=$WORK/$DENO_ZIP_NAME
if curl -fL --retry 3 --retry-delay 2 -o "$DENO_TMP" "$DENO_URL"; then
    if nvg_require_sha256 "$DENO_TMP" "$DENO_LINUX_SHA"; then
        if nvg_unzip_deno "$DENO_TMP" "$WORK/deno-extract"; then
            DESC=$(file -b "$WORK/deno-extract/deno")
            OK=$(printf '%s' "$DESC" | grep -c 'x86-64' || true)
            if [ "$OK" -ge 1 ]; then
                cp "$WORK/deno-extract/deno" "$BIN/deno"
                chmod 755 "$BIN/deno"
                DENO_RECORD="- Version: $DENO_VERSION
- URL: $DENO_URL
- SHA-256: $DENO_LINUX_SHA
- Upstream checksum file: $DENO_URL.sha256sum
- Licence: MIT. Text shipped as Deno-LICENSE.txt.
- File: bin/deno
- file(1): $DESC"
            else
                DENO_RECORD="- Not bundled. $DENO_URL unpacked to $DESC, which is not x86-64."
            fi
        else
            DENO_RECORD="- Not bundled. $DENO_URL matched its checksum but did not contain a deno binary."
        fi
    fi
fi

IM_NAME="ImageMagick-$IMAGEMAGICK_VERSION-gcc-x86_64.AppImage"
IM_URL="https://github.com/ImageMagick/ImageMagick/releases/download/$IMAGEMAGICK_VERSION/$IM_NAME"
IM_TMP=$WORK/$IM_NAME
if curl -fL --retry 3 --retry-delay 2 -o "$IM_TMP" "$IM_URL"; then
    if nvg_require_sha256 "$IM_TMP" "$IMAGEMAGICK_SHA"; then
        cp "$IM_TMP" "$BIN/magick"
        chmod 755 "$BIN/magick"
        if python3 - "$BIN/magick" <<'PY'
import subprocess, sys
try:
    result = subprocess.run([sys.argv[1], "-version"], timeout=30, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
except Exception as exc:
    sys.stderr.write(str(exc) + "\n")
    sys.exit(1)
sys.stdout.buffer.write(result.stdout[:400])
sys.exit(result.returncode)
PY
        then
            DESC=$(file -b "$BIN/magick")
            MAGICK_RECORD="- Version: $IMAGEMAGICK_VERSION
- URL: $IM_URL
- SHA-256: $IMAGEMAGICK_SHA (GitHub release asset digest)
- Licence: ImageMagick License. Text shipped as ImageMagick-LICENSE.txt.
- File: bin/magick (official gcc x86_64 AppImage)
- file(1): $DESC
- Checked: bin/magick -version exited 0 on the build machine."
        else
            rm -f "$BIN/magick"
            MAGICK_RECORD="- Not bundled. $IM_URL matched its checksum but \`magick -version\` did not succeed here (often missing FUSE for an AppImage). magick is resolved from bin/magick, then PATH."
        fi
    fi
fi

OMITTED_RECORD="- Steam: omitted on purpose.
- x264 CLI: the library is linked into FFmpeg. The x264 program is not shipped.
- lame CLI: the library is linked into FFmpeg. The lame program is not shipped.
- macOS ImageMagick source build: this tarball uses the Linux AppImage above."

NVG_VERSION=$VERSION
PLATFORM_LABEL=linux-x64
export NVG_VERSION PLATFORM_LABEL FFMPEG_TAG FFMPEG_COMMIT FFMPEG_CONFIGURE
export X264_COMMIT X264_ASM LAME_VERSION LAME_SHA FREI0R_TAG FREI0R_COMMIT VOCODER_COMMIT
export YTDLP_RECORD DENO_RECORD MAGICK_RECORD OMITTED_RECORD
nvg_write_third_party "$PKG/THIRD_PARTY.md"
nvg_write_source_offer "$PKG/SOURCE_OFFER.txt"

cp "$ROOT/tools/package/linux/nonsensicalvideogenerator" "$PKG/nonsensicalvideogenerator"
cp "$ROOT/tools/package/linux/install-linux.sh" "$PKG/install-linux.sh"
cp "$ROOT/tools/package/linux/uninstall-linux.sh" "$PKG/uninstall-linux.sh"
mkdir -p -- "$PKG/share"
cp "$ROOT/tools/package/linux/nonsensicalvideogenerator.desktop" "$PKG/share/nonsensicalvideogenerator.desktop"
cp "$ROOT/content/graphics/icon.png" "$PKG/share/icon.png"
chmod 755 "$PKG/nonsensicalvideogenerator" "$PKG/install-linux.sh" "$PKG/uninstall-linux.sh"
cat > "$PKG/NVG-PACKAGE.txt" <<EOF
id=io.github.nvg.NonsensicalVideoGenerator
name=Nonsensical Video Generator
version=$VERSION
platform=linux-x64
EOF

nvg_assert_no_steam "$PKG"
nvg_assert_no_removed_deps "$PKG"
python3 "$ROOT/tools/package/lib/audit_native_libs.py" "$PKG"
printf '==> bundled ffmpeg and frei0r\n'
python3 "$ROOT/tools/package/lib/smoke_ffmpeg.py" \
    "$PKG/bin/ffmpeg" \
    "$PKG/bin/ffprobe" \
    "$PKG/bin/frei0r-1" \
    "$WORK/smoke" \
    "$PKG/ThirdParty/x264/NVG-X264-SPEED.txt"
PATH=/usr/bin:/bin "$PKG/bin/ffmpeg" -hide_banner -encoders | grep -E '(^|[[:space:]])libmp3lame([[:space:]]|$)' >/dev/null \
    || nvg_die "ffmpeg does not list libmp3lame"
if [ -x "$PKG/bin/magick" ]; then
    if PATH=/usr/bin:/bin "$PKG/bin/magick" -size 16x16 "canvas:red" "$WORK/smoke/red.png" \
        && PATH=/usr/bin:/bin "$PKG/bin/magick" "$WORK/smoke/red.png" "$WORK/smoke/red.jpg" \
        && file "$WORK/smoke/red.jpg" | grep -q 'JPEG image data'
    then
        printf '==> AppImage magick PNG to JPEG ok\n'
    else
        printf '==> AppImage magick is present but PNG to JPEG failed\n' >&2
        exit 1
    fi
fi
ARCHIVE=$OUTDIR/$NAME.tar.gz
rm -f "$ARCHIVE"
tar -C "$WORK" -czf "$ARCHIVE" "$NAME"
printf '==> %s\n' "$ARCHIVE"
ls -lh "$ARCHIVE"
trap - EXIT
rm -rf "$WORK"
