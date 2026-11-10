#!/bin/sh
# Build a self-contained Nonsensical Video Generator .app and DMG.
#
# Usage: build-macos-installer.sh <osx-arm64|osx-x64> <outdir>
#
# osx-arm64 is the native build. osx-x64 on an arm64 Mac cross-compiles every
# native dependency with clang -arch x86_64 and publishes with the x64 SDK
# under Rosetta (~/.dotnet-nvg-x64). The x64 app contains no arm64 Mach-O.
# No sudo, no Homebrew, no notarisation, no upload.

set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/../.." && pwd)
. "$ROOT/tools/package/lib/package-common.sh"

YTDLP_VERSION=2026.10.21
YTDLP_MACOS_SHA=7a6fe71d7d680f1fb0ac18d4949873d77db2859e90017c0f44bd94b23be4ee40
DENO_VERSION=2.9.8
DENO_ARM64_SHA=9f0bf660393da2eaff044cdc7444cb08c73f235849b61d224d825a194ce7c98e
DENO_X64_MAC_SHA=4b2f5edd2639b7470f034238b321d7002d8dfb4726d8558f52c17afac370eb2c

if [ "$#" -ne 2 ]; then
    nvg_die "usage: $0 <osx-arm64|osx-x64> <outdir>"
fi
if [ "$(uname -s)" != "Darwin" ]; then
    nvg_die "build-macos-installer.sh must run on macOS"
fi

RID=$1
OUTDIR=$2
case $RID in
    osx-arm64|osx-x64) ;;
    *) nvg_die "rid must be osx-arm64 or osx-x64" ;;
esac

HOST=$(uname -m)
case $RID in
    osx-arm64)
        [ "$HOST" = "arm64" ] || nvg_die "osx-arm64 must be built on an arm64 Mac (this host is $HOST)"
        ;;
    osx-x64)
        case $HOST in
            arm64|x86_64) ;;
            *) nvg_die "osx-x64 cannot be built on $HOST" ;;
        esac
        ;;
esac

mkdir -p -- "$OUTDIR"
OUTDIR=$(CDPATH= cd -- "$OUTDIR" && pwd)
VERSION=$(nvg_version)
[ -n "$VERSION" ] || nvg_die "could not read Version from the csproj"
PUBLISH_PREFIX=""
if [ -z "${DOTNET:-}" ] && [ "$RID" = "osx-x64" ] && [ "$HOST" = "arm64" ]; then
    DOTNET=$HOME/.dotnet-nvg-x64/dotnet
    PUBLISH_PREFIX="arch -x86_64"
fi
DOTNET=${DOTNET:-$HOME/.dotnet-nvg/dotnet}
[ -x "$DOTNET" ] || nvg_die "dotnet not found at $DOTNET (user-local SDK, no brew)"

WORK=$(mktemp -d "${TMPDIR:-/tmp}/nvg-macos.XXXXXX")
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

PUBLISH=$WORK/publish
printf '==> publish %s\n' "$RID"
# shellcheck disable=SC2086
$PUBLISH_PREFIX "$DOTNET" publish "$ROOT/NonsensicalVideoGenerator.csproj" \
    -c Release \
    -r "$RID" \
    --self-contained true \
    -p:NvgPlatform=DesktopGL \
    -p:NvgSkipMgcb=true \
    -p:UseAppHost=true \
    -p:PublishSingleFile=false \
    -o "$PUBLISH"

APP=$WORK/NonsensicalVideoGenerator.app
mkdir -p -- "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -a "$PUBLISH"/. "$APP/Contents/MacOS"/
rm -rf "$PUBLISH"
nvg_strip_publish_dir "$APP/Contents/MacOS"
chmod 755 "$APP/Contents/MacOS/NonsensicalVideoGenerator"
nvg_assert_no_steam "$APP"
nvg_assert_no_removed_deps "$APP"
nvg_require_packaged_locales "$APP/Contents/MacOS/locales"

CC_BIN=/usr/bin/cc
CXX_BIN=/usr/bin/c++
if [ "$RID" = "osx-x64" ] && [ "$HOST" = "arm64" ]; then
    CC_BIN=$WORK/cc-x64
    CXX_BIN=$WORK/cxx-x64
    cat > "$CC_BIN" <<'EOF'
#!/bin/sh
exec /usr/bin/clang -arch x86_64 "$@"
EOF
    cat > "$CXX_BIN" <<'EOF'
#!/bin/sh
exec /usr/bin/clang++ -arch x86_64 "$@"
EOF
    chmod 755 "$CC_BIN" "$CXX_BIN"
    export NVG_MACH_ARCH=x86_64
fi

DEPS=${NVG_DEPS_DIR:-$WORK/deps}
nvg_build_media_deps "$DEPS" "$WORK/media-src" "$CC_BIN" "$CXX_BIN"
FFMPEG_PREFIX=$DEPS/ffmpeg
mkdir -p -- "$APP/Contents/MacOS/lib" "$APP/Contents/MacOS/bin"
cp -a "$FFMPEG_PREFIX/lib"/. "$APP/Contents/MacOS/lib"/
rm -rf "$APP/Contents/MacOS/lib/pkgconfig"
cp "$FFMPEG_PREFIX/bin/ffmpeg" "$FFMPEG_PREFIX/bin/ffprobe" "$APP/Contents/MacOS/bin/"
chmod 755 "$APP/Contents/MacOS/bin/ffmpeg" "$APP/Contents/MacOS/bin/ffprobe"
TP="$APP/Contents/Resources/ThirdParty"
mkdir -p -- "$TP/ffmpeg" "$TP/x264" "$TP/lame" "$TP/frei0r"
cp "$FFMPEG_PREFIX/NVG-FFMPEG.txt" "$TP/ffmpeg/NVG-FFMPEG.txt"
cp "$FFMPEG_PREFIX/LICENSE.GPLv3" "$TP/ffmpeg/LICENSE.GPLv3"
if [ -f "$FFMPEG_PREFIX/LICENSE.LGPLv2.1" ]; then
    cp "$FFMPEG_PREFIX/LICENSE.LGPLv2.1" "$TP/ffmpeg/LICENSE.LGPLv2.1"
fi
cp "$DEPS/x264/NVG-X264.txt" "$DEPS/x264/COPYING" "$TP/x264/"
cp "$DEPS/lame/NVG-LAME.txt" "$DEPS/lame/COPYING" "$TP/lame/"
cp "$ROOT/content/desktopgl-music/PROVENANCE.txt" "$TP/desktopgl-music-PROVENANCE.txt"
cp "$DEPS/frei0r/NVG-FREI0R.txt" "$DEPS/frei0r/COPYING" "$TP/frei0r/"
nvg_stage_frei0r "$DEPS/frei0r" "$APP/Contents/MacOS/bin/frei0r-1"
FFMPEG_TAG=$(sed -n 's/^tag=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
FFMPEG_COMMIT=$(sed -n 's/^commit=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
FFMPEG_CONFIGURE=$(sed -n 's/^configure=//p' "$TP/ffmpeg/NVG-FFMPEG.txt")
X264_COMMIT=$(sed -n 's/^commit=//p' "$TP/x264/NVG-X264.txt")
X264_ASM=$(sed -n 's/^asm=//p' "$TP/x264/NVG-X264.txt")
LAME_VERSION=$(sed -n 's/^version=//p' "$TP/lame/NVG-LAME.txt")
LAME_SHA=$(sed -n 's/^sha256=//p' "$TP/lame/NVG-LAME.txt")
FREI0R_TAG=$(sed -n 's/^tag=//p' "$TP/frei0r/NVG-FREI0R.txt")
FREI0R_COMMIT=$(sed -n 's/^commit=//p' "$TP/frei0r/NVG-FREI0R.txt")
[ -e "$APP/Contents/MacOS/lib/libavcodec.61.dylib" ] || nvg_die "libavcodec.61.dylib missing"
[ -x "$APP/Contents/MacOS/bin/ffmpeg" ] || nvg_die "bin/ffmpeg missing"

printf '==> ImageMagick\n'
nvg_build_magick "$DEPS/magick" "$WORK/magick-src" "$CC_BIN" "$CXX_BIN"
rm -rf "$WORK/magick-src"
cp "$DEPS/magick/bin/magick" "$APP/Contents/MacOS/bin/magick"
chmod 755 "$APP/Contents/MacOS/bin/magick"
mkdir -p -- "$TP/magick"
cp "$DEPS/magick/NVG-MAGICK.txt" "$TP/magick/NVG-MAGICK.txt"
cp -a "$DEPS/magick/LICENSES"/. "$TP/magick/"
MAGICK_VERSION_BUILT=$(sed -n 's/^version=//p' "$TP/magick/NVG-MAGICK.txt")
MAGICK_COMMIT=$(sed -n 's/^commit=//p' "$TP/magick/NVG-MAGICK.txt")
MAGICK_SHA=$(sed -n 's/^sha256=//p' "$TP/magick/NVG-MAGICK.txt")

printf '==> vocoder\n'
VOCODER_SCRATCH=$WORK/vocoder-src
nvg_build_vocoder "$APP/Contents/MacOS/bin" "$VOCODER_SCRATCH" "$CC_BIN"
rm -rf "$VOCODER_SCRATCH"
chmod 755 "$APP/Contents/MacOS/bin/vocoder"
VOCODER_COMMIT=$(sed -n 's/^VOCODER_COMMIT="\(.*\)".*/\1/p' "$ROOT/tools/vocoder/build-vocoder.sh")

BIN=$APP/Contents/MacOS/bin
mkdir -p -- "$BIN"
YTDLP_RECORD="- Not bundled. Download or checksum failed, so yt-dlp is left for PATH."
DENO_RECORD="- Not bundled. Download or checksum failed, so deno is left for PATH."

YTDLP_URL="https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION/yt-dlp_macos"
YTDLP_TMP=$WORK/yt-dlp_macos
if curl -fL --retry 3 --retry-delay 2 -o "$YTDLP_TMP" "$YTDLP_URL"; then
    if nvg_require_sha256 "$YTDLP_TMP" "$YTDLP_MACOS_SHA"; then
        DESC=$(file -b "$YTDLP_TMP")
        case $RID in
            osx-arm64) OK=$(printf '%s' "$DESC" | grep -c 'arm64' || true) ;;
            osx-x64) OK=$(printf '%s' "$DESC" | grep -c 'x86_64' || true) ;;
        esac
        if [ "$OK" -ge 1 ]; then
            cp "$YTDLP_TMP" "$BIN/yt-dlp"
            chmod 755 "$BIN/yt-dlp"
            YTDLP_RECORD="- Version: $YTDLP_VERSION
- URL: $YTDLP_URL
- SHA-256: $YTDLP_MACOS_SHA
- Upstream checksum file: https://github.com/yt-dlp/yt-dlp/releases/download/$YTDLP_VERSION/SHA2-256SUMS
- Licence: The Unlicense. Text shipped as yt-dlp-LICENSE.txt.
- File: bin/yt-dlp
- file(1): $DESC"
        else
            YTDLP_RECORD="- Not bundled. $YTDLP_URL is $DESC, which does not match $RID."
        fi
    fi
fi

case $RID in
    osx-arm64)
        DENO_ZIP_NAME="deno-aarch64-apple-darwin.zip"
        DENO_SHA=$DENO_ARM64_SHA
        ;;
    osx-x64)
        DENO_ZIP_NAME="deno-x86_64-apple-darwin.zip"
        DENO_SHA=$DENO_X64_MAC_SHA
        ;;
esac
DENO_URL="https://github.com/denoland/deno/releases/download/v$DENO_VERSION/$DENO_ZIP_NAME"
DENO_TMP=$WORK/$DENO_ZIP_NAME
if curl -fL --retry 3 --retry-delay 2 -o "$DENO_TMP" "$DENO_URL"; then
    if nvg_require_sha256 "$DENO_TMP" "$DENO_SHA"; then
        nvg_unzip_deno "$DENO_TMP" "$WORK/deno-extract"
        DESC=$(file -b "$WORK/deno-extract/deno")
        case $RID in
            osx-arm64) OK=$(printf '%s' "$DESC" | grep -c 'arm64' || true) ;;
            osx-x64) OK=$(printf '%s' "$DESC" | grep -c 'x86_64' || true) ;;
        esac
        if [ "$OK" -ge 1 ]; then
            cp "$WORK/deno-extract/deno" "$BIN/deno"
            chmod 755 "$BIN/deno"
            DENO_RECORD="- Version: $DENO_VERSION
- URL: $DENO_URL
- SHA-256: $DENO_SHA
- Upstream checksum file: $DENO_URL.sha256sum
- Licence: MIT. Text shipped as Deno-LICENSE.txt.
- File: bin/deno
- file(1): $DESC"
        else
            DENO_RECORD="- Not bundled. $DENO_URL unpacked to $DESC, which does not match $RID."
        fi
    fi
fi

if [ "$RID" = "osx-x64" ]; then
    printf '==> keep x86_64 Mach-O only\n'
    python3 "$ROOT/tools/package/macos/macho_arch.py" --arch x86_64 --thin "$APP"
fi

MAGICK_DESC=$(file -b "$APP/Contents/MacOS/bin/magick")
MAGICK_RECORD="- Version: $MAGICK_VERSION_BUILT
- Commit: $MAGICK_COMMIT
- URL: https://github.com/ImageMagick/ImageMagick/releases/download/$MAGICK_VERSION_BUILT/ImageMagick-$MAGICK_VERSION_BUILT.tar.xz
- SHA-256: $MAGICK_SHA
- Licence: ImageMagick License. Static build. zlib 1.3.1, libpng 1.6.47 and libjpeg-turbo 3.1.0 are linked in. Their licence texts are in ThirdParty/magick/.
- Built by tools/magick/build-magick.sh. No X11 and no Homebrew libraries.
- File: bin/magick
- file(1): $MAGICK_DESC"
OMITTED_RECORD="- Steam: omitted on purpose.
- x264 CLI: the library is linked into FFmpeg. The x264 program is not shipped.
- lame CLI: the library is linked into FFmpeg. The lame program is not shipped."

NVG_VERSION=$VERSION
PLATFORM_LABEL=$RID
export NVG_VERSION PLATFORM_LABEL FFMPEG_TAG FFMPEG_COMMIT FFMPEG_CONFIGURE
export X264_COMMIT X264_ASM LAME_VERSION LAME_SHA FREI0R_TAG FREI0R_COMMIT VOCODER_COMMIT
export YTDLP_RECORD DENO_RECORD MAGICK_RECORD OMITTED_RECORD
nvg_write_third_party "$APP/Contents/Resources/ThirdParty/THIRD_PARTY.md"
nvg_write_source_offer "$APP/Contents/Resources/ThirdParty/SOURCE_OFFER.txt"

printf '==> rewrite install names\n'
python3 "$ROOT/tools/package/macos/rewrite_install_names.py" "$APP/Contents/MacOS"

SHORT=$(nvg_short_version "$VERSION")
python3 - "$ROOT/tools/package/macos/Info.plist.in" "$APP/Contents/Info.plist" "$VERSION" "$SHORT" <<'PY'
import pathlib, sys
src, dest, version, short = sys.argv[1:]
text = pathlib.Path(src).read_text()
text = text.replace("@VERSION@", version).replace("@SHORT_VERSION@", short)
pathlib.Path(dest).write_text(text)
PY
plutil -lint "$APP/Contents/Info.plist"

ICONSET=$WORK/NonsensicalVideoGenerator.iconset
mkdir -p -- "$ICONSET"
sips -s format png "$ROOT/icon.ico" --out "$WORK/icon-src.png" >/dev/null
sips -z 16 16 "$WORK/icon-src.png" --out "$ICONSET/icon_16x16.png" >/dev/null
sips -z 32 32 "$WORK/icon-src.png" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$WORK/icon-src.png" --out "$ICONSET/icon_32x32.png" >/dev/null
sips -z 64 64 "$WORK/icon-src.png" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$WORK/icon-src.png" --out "$ICONSET/icon_128x128.png" >/dev/null
sips -z 256 256 "$WORK/icon-src.png" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$WORK/icon-src.png" --out "$ICONSET/icon_256x256.png" >/dev/null
sips -z 512 512 "$WORK/icon-src.png" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$WORK/icon-src.png" --out "$ICONSET/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$WORK/icon-src.png" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/NonsensicalVideoGenerator.icns"

printf '==> bundled ffmpeg and frei0r\n'
python3 "$ROOT/tools/package/lib/audit_native_libs.py" "$APP/Contents/MacOS"
python3 "$ROOT/tools/package/lib/smoke_ffmpeg.py" \
    "$APP/Contents/MacOS/bin/ffmpeg" \
    "$APP/Contents/MacOS/bin/ffprobe" \
    "$APP/Contents/MacOS/bin/frei0r-1" \
    "$WORK/smoke" \
    "$WORK/smoke/speed.txt"
cp "$WORK/smoke/speed.txt" "$APP/Contents/Resources/ThirdParty/x264/NVG-X264-SPEED.txt"

printf '==> codesign\n'
codesign --force --deep -s - "$APP"
codesign -v --deep "$APP"
nvg_assert_no_steam "$APP"
nvg_assert_no_removed_deps "$APP"
if [ "$RID" = "osx-x64" ] && [ "$HOST" = "arm64" ]; then
    arch -x86_64 "$APP/Contents/MacOS/bin/ffmpeg" -hide_banner -version >/dev/null \
        || nvg_die "ffmpeg did not run under Rosetta"
    arch -x86_64 "$APP/Contents/MacOS/bin/ffprobe" -hide_banner -version >/dev/null \
        || nvg_die "ffprobe did not run under Rosetta"
    arch -x86_64 "$APP/Contents/MacOS/bin/magick" -version >/dev/null \
        || nvg_die "magick did not run under Rosetta"
fi
PATH=/usr/bin:/bin "$APP/Contents/MacOS/bin/ffmpeg" -hide_banner -encoders | grep -E '(^|[[:space:]])libx264([[:space:]]|$)' >/dev/null \
    || nvg_die "signed ffmpeg does not list libx264"
PATH=/usr/bin:/bin "$APP/Contents/MacOS/bin/ffmpeg" -hide_banner -encoders | grep -E '(^|[[:space:]])libmp3lame([[:space:]]|$)' >/dev/null \
    || nvg_die "signed ffmpeg does not list libmp3lame"
PATH=/usr/bin:/bin "$APP/Contents/MacOS/bin/magick" -size 16x16 "canvas:red" "$WORK/smoke/red.png"
PATH=/usr/bin:/bin "$APP/Contents/MacOS/bin/magick" "$WORK/smoke/red.png" "$WORK/smoke/red.jpg"
file "$WORK/smoke/red.jpg" | grep -q 'JPEG image data' || nvg_die "magick PNG to JPEG failed"

DMG=$OUTDIR/NonsensicalVideoGenerator-$VERSION-$RID.dmg
DMG_ROOT=$WORK/dmg-root
mkdir -p -- "$DMG_ROOT"
cp -R "$APP" "$DMG_ROOT/NonsensicalVideoGenerator.app"
ln -s /Applications "$DMG_ROOT/Applications"
cp "$APP/Contents/Resources/ThirdParty/THIRD_PARTY.md" "$DMG_ROOT/THIRD_PARTY.md"
cp "$APP/Contents/Resources/ThirdParty/SOURCE_OFFER.txt" "$DMG_ROOT/SOURCE_OFFER.txt"
rm -f "$DMG"
hdiutil create -volname "Nonsensical Video Generator" -srcfolder "$DMG_ROOT" -ov -format UDZO "$DMG"
printf '==> %s\n' "$DMG"
ls -lh "$DMG"
trap - EXIT
rm -rf "$WORK"
