# Shared helpers for the macOS and Linux package scripts.
# Sourced, not executed. Callers set ROOT to the repository root.

nvg_die() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

# $1 directory of locale json files, $2 label, $3 hint appended to the error.
# A missing directory, no *.json, or a zero-byte json fails the package build.
# public/locales comes from the public submodule; a git bundle does not carry it.
nvg_require_locale_jsons() {
    dir=$1
    label=$2
    hint=$3
    if [ ! -d "$dir" ]; then
        nvg_die "$label is missing. $hint"
    fi
    found=0
    empty=0
    for locale_file in "$dir"/*.json; do
        if [ ! -f "$locale_file" ]; then
            continue
        fi
        found=$((found + 1))
        if [ ! -s "$locale_file" ]; then
            printf 'error: locale file is empty: %s\n' "$locale_file" >&2
            empty=$((empty + 1))
        fi
    done
    if [ "$found" -eq 0 ]; then
        nvg_die "no *.json files in $label. $hint"
    fi
    if [ "$empty" -ne 0 ]; then
        nvg_die "$empty empty locale file(s) in $label"
    fi
    printf '==> %s locale json files in %s\n' "$found" "$label"
}

nvg_require_public_locales() {
    nvg_require_locale_jsons "$ROOT/public/locales" "public/locales" "public is a submodule (git submodule update --init public). A git bundle does not include it."
}

nvg_require_packaged_locales() {
    nvg_require_locale_jsons "$1" "$1" "public/locales was not copied into the package."
}

nvg_version() {
    sed -n 's/.*<Version>\([^<]*\)<\/Version>.*/\1/p' "$ROOT/NonsensicalVideoGenerator.csproj" | head -n 1
}

nvg_short_version() {
    printf '%s\n' "$1" | awk -F. '{ print $1 "." $2 "." $3 }'
}

nvg_sha256() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    else
        sha256sum "$1" | awk '{ print $1 }'
    fi
}

nvg_download() {
    url=$1
    dest=$2
    printf '==> download %s\n' "$url"
    curl -fL --retry 3 --retry-delay 2 -o "$dest" "$url"
}

nvg_require_sha256() {
    file=$1
    expect=$2
    got=$(nvg_sha256 "$file")
    if [ "$got" != "$expect" ]; then
        printf 'error: checksum mismatch for %s\n  expected %s\n  got      %s\n' "$file" "$expect" "$got" >&2
        return 1
    fi
    printf '==> checksum ok %s\n' "$got"
}

# Remove Steam files and Windows-only binaries from a publish directory.
# The DesktopGL publish target copies baseroot wholesale. The community
# package must not ship steam_appid.txt, Steam natives, the Windows
# FFmpeg DLLs, or the Windows desktop runtime in redist/.
nvg_strip_publish_dir() {
    root=$1
    printf '==> strip Steam and Windows binaries from %s\n' "$root"
    find "$root" \( \
        -name 'steam_appid.txt' -o \
        -name 'steam_api.dll' -o \
        -name 'steam_api64.dll' -o \
        -name 'libsteam_api.so' -o \
        -name 'libsteam_api.dylib' -o \
        -name 'avcodec-61.dll' -o \
        -name 'avdevice-61.dll' -o \
        -name 'avfilter-10.dll' -o \
        -name 'avformat-61.dll' -o \
        -name 'avutil-59.dll' -o \
        -name 'postproc-58.dll' -o \
        -name 'swresample-5.dll' -o \
        -name 'swscale-8.dll' -o \
        -name '*.exe' \
        \) -type f -print -exec rm -f {} +
    if [ -d "$root/steam_api.bundle" ]; then
        printf 'remove %s\n' "$root/steam_api.bundle"
        rm -rf "$root/steam_api.bundle"
    fi
    if [ -d "$root/redist" ]; then
        printf 'remove %s\n' "$root/redist"
        rm -rf "$root/redist"
    fi
    if [ -d "$root/bin/frei0r-1" ]; then
        printf 'remove %s\n' "$root/bin/frei0r-1"
        rm -rf "$root/bin/frei0r-1"
    fi
}

nvg_assert_no_steam() {
    root=$1
    hits=$(find "$root" \( \
        -name 'steam_appid.txt' -o \
        -name 'steam_api.dll' -o \
        -name 'steam_api64.dll' -o \
        -name 'libsteam_api.so' -o \
        -name 'libsteam_api.dylib' -o \
        -name 'steam_api.bundle' \
        \) -print)
    if [ -n "$hits" ]; then
        printf '%s\n' "$hits" >&2
        nvg_die "Steam files remain in $root"
    fi
}

# SharpCompress and its ZstdSharp dependency were removed from the project.
# A publish that still contains either DLL is the wrong tree.
nvg_assert_no_removed_deps() {
    root=$1
    hits=$(find "$root" \( -name 'SharpCompress.dll' -o -name 'ZstdSharp.dll' \) -print)
    if [ -n "$hits" ]; then
        printf '%s\n' "$hits" >&2
        nvg_die "SharpCompress.dll or ZstdSharp.dll remains in $root"
    fi
    printf '==> no SharpCompress.dll or ZstdSharp.dll in %s\n' "$root"
}

# Build FFmpeg 7 with a PATH that cannot see Homebrew.
# $1 prefix  $2 scratch  $3 optional CC
# $4 optional x264 prefix  $5 optional frei0r prefix
# With both prefixes this is the shared libraries and the CLI, one build.
nvg_build_ffmpeg() {
    prefix=$1
    scratch=$2
    cc=${3:-/usr/bin/cc}
    x264_prefix=${4:-}
    frei0r_prefix=${5:-}
    (
        unset PKG_CONFIG_PATH PKG_CONFIG_LIBDIR PKG_CONFIG_SYSROOT_DIR || true
        unset CFLAGS CXXFLAGS LDFLAGS CPPFLAGS CPATH LIBRARY_PATH || true
        unset C_INCLUDE_PATH CPLUS_INCLUDE_PATH || true
        unset DYLD_LIBRARY_PATH DYLD_FALLBACK_LIBRARY_PATH LD_LIBRARY_PATH || true
        export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
        export CC="$cc"
        if [ -n "$x264_prefix" ]; then
            export NVG_X264_PREFIX="$x264_prefix"
        fi
        if [ -n "$frei0r_prefix" ]; then
            export NVG_FREI0R_PREFIX="$frei0r_prefix"
        fi
        /bin/sh "$ROOT/tools/ffmpeg7/build-ffmpeg7.sh" "$prefix" "$scratch"
    )
}

# Static x264, frei0r plugins, then one FFmpeg build that links them.
# $1 deps dir  $2 scratch dir  $3 CC  $4 CXX
# Leaves $1/x264, $1/frei0r and $1/ffmpeg. A matching manifest skips a rebuild.
nvg_build_media_deps() {
    deps=$1
    scratch=$2
    cc=$3
    cxx=$4
    mkdir -p -- "$deps" "$scratch"
    printf '==> x264\n'
    (
        unset PKG_CONFIG_PATH PKG_CONFIG_LIBDIR DYLD_LIBRARY_PATH LD_LIBRARY_PATH || true
        export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
        export CC="$cc"
        /bin/sh "$ROOT/tools/x264/build-x264.sh" "$deps/x264" "$scratch/x264"
    )
    rm -rf "$scratch/x264"
    printf '==> frei0r\n'
    (
        unset PKG_CONFIG_PATH PKG_CONFIG_LIBDIR CMAKE_PREFIX_PATH CMAKE_MODULE_PATH || true
        unset DYLD_LIBRARY_PATH LD_LIBRARY_PATH || true
        export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
        export CC="$cc"
        export CXX="$cxx"
        /bin/sh "$ROOT/tools/frei0r/build-frei0r.sh" "$deps/frei0r" "$scratch/frei0r"
    )
    rm -rf "$scratch/frei0r"
    printf '==> FFmpeg 7 libraries and CLI\n'
    nvg_build_ffmpeg "$deps/ffmpeg" "$scratch/ffmpeg" "$cc" "$deps/x264" "$deps/frei0r"
    rm -rf "$scratch/ffmpeg"
}

# $1 prefix  $2 scratch  $3 CC  $4 CXX
nvg_build_magick() {
    prefix=$1
    scratch=$2
    cc=$3
    cxx=$4
    (
        unset PKG_CONFIG_PATH PKG_CONFIG_LIBDIR DYLD_LIBRARY_PATH LD_LIBRARY_PATH || true
        export CC="$cc"
        export CXX="$cxx"
        /bin/sh "$ROOT/tools/magick/build-magick.sh" "$prefix" "$scratch"
    )
}

# Copy frei0r plugins to <dest>. On macOS also copy .so to .dylib.
nvg_stage_frei0r() {
    prefix=$1
    dest=$2
    src=$(find "$prefix" -type d -name 'frei0r-1' | head -n 1)
    [ -n "$src" ] || nvg_die "frei0r-1 directory missing under $prefix"
    mkdir -p -- "$dest"
    found=0
    for plugin in "$src"/*; do
        [ -f "$plugin" ] || continue
        cp "$plugin" "$dest/$(basename "$plugin")"
        found=$((found + 1))
    done
    [ "$found" -gt 0 ] || nvg_die "no frei0r plugins in $src"
    if [ "$(uname -s)" = "Darwin" ]; then
        for plugin in "$dest"/*.so; do
            [ -f "$plugin" ] || continue
            base=$(basename "$plugin" .so)
            if [ ! -e "$dest/$base.dylib" ]; then
                cp "$plugin" "$dest/$base.dylib"
            fi
        done
    fi
    if [ ! -e "$dest/nervous.so" ] && [ ! -e "$dest/nervous.dylib" ]; then
        nvg_die "nervous plugin missing in $dest"
    fi
    if [ ! -e "$dest/colorize.so" ] && [ ! -e "$dest/colorize.dylib" ]; then
        nvg_die "colorize plugin missing in $dest"
    fi
}

# $1 output dir  $2 scratch  $3 optional CC
# The third argument is the same compiler the media builds use, so an
# osx-x64 cross build does not leave an arm64 vocoder in the x64 app.
nvg_build_vocoder() {
    out=$1
    scratch=$2
    cc=${3:-/usr/bin/cc}
    (
        unset PKG_CONFIG_PATH DYLD_LIBRARY_PATH LD_LIBRARY_PATH || true
        export PATH="/usr/bin:/bin:/usr/sbin:/sbin"
        export CC="$cc"
        /bin/sh "$ROOT/tools/vocoder/build-vocoder.sh" "$out" "$scratch"
    )
}

# Globals set by the package script: NVG_VERSION PLATFORM_LABEL FFMPEG_TAG
# FFMPEG_COMMIT FFMPEG_CONFIGURE X264_COMMIT X264_ASM FREI0R_TAG FREI0R_COMMIT
# VOCODER_COMMIT YTDLP_RECORD DENO_RECORD MAGICK_RECORD OMITTED_RECORD
nvg_write_third_party() {
    dest=$1
    {
        printf '# Third-party components\n\n'
        printf 'Package version %s (%s).\n\n' "$NVG_VERSION" "$PLATFORM_LABEL"
        printf 'This community build does not include Steam files, steam_appid.txt, or Steam natives. Workshop and achievements stay unavailable.\n\n'
        printf 'Corresponding source for the GPL pieces is the exact upstream revision named here. No patches are applied. See SOURCE_OFFER.txt.\n\n'
        printf '## FFmpeg\n\n'
        printf -- '- Version tag: %s\n' "$FFMPEG_TAG"
        printf -- '- Commit: %s\n' "$FFMPEG_COMMIT"
        printf -- '- Source: https://github.com/FFmpeg/FFmpeg\n'
        printf -- '- Licence: GPL-3.0-or-later\n'
        printf -- '- Built by tools/ffmpeg7/build-ffmpeg7.sh (%s). One build produces the shared libraries and bin/ffmpeg plus bin/ffprobe, linked with the static x264 below and the frei0r wrapper. Configure cannot see Homebrew libraries.\n' "$FFMPEG_CONFIGURE"
        printf -- '- Licence text: ThirdParty/ffmpeg/LICENSE.GPLv3 (also FFmpeg-LICENSE.txt from the app tree)\n'
        printf -- '- Files: lib/libavcodec.61.dylib or lib/libavcodec.so.61, and bin/ffmpeg, bin/ffprobe\n\n'
        printf '## x264\n\n'
        printf -- '- Commit: %s (stable)\n' "$X264_COMMIT"
        printf -- '- Source: https://code.videolan.org/videolan/x264\n'
        printf -- '- Licence: GPL-2.0-or-later\n'
        printf -- '- Built by tools/x264/build-x264.sh as a static PIC library (--disable-cli). It is linked into the FFmpeg libraries. The x264 program is not shipped.\n'
        printf -- '- Assembly: %s\n' "$X264_ASM"
        printf -- '- Licence text: ThirdParty/x264/COPYING\n\n'
        printf '## frei0r\n\n'
        printf -- '- Tag: %s\n' "$FREI0R_TAG"
        printf -- '- Commit: %s\n' "$FREI0R_COMMIT"
        printf -- '- Source: https://github.com/dyne/frei0r\n'
        printf -- '- Licence: GPL-2.0-or-later. nervous.cpp is GPL-3.0-or-later.\n'
        printf -- '- Built by tools/frei0r/build-frei0r.sh. OpenCV, Cairo and gavl plugins are off.\n'
        printf -- '- Files: bin/frei0r-1/. The app sets FREI0R_PATH to that directory.\n'
        printf -- '- Licence text: ThirdParty/frei0r/COPYING\n\n'
        printf '## vocoder\n\n'
        printf -- '- Upstream: https://github.com/borsboom/vocoder\n'
        printf -- '- Commit: %s\n' "$VOCODER_COMMIT"
        printf -- '- Built by tools/vocoder/build-vocoder.sh\n'
        printf -- '- Licence: permissive, notice required. Zerius Vocoder is Copyright (C) 1996-1999, 2002 Emanuel Borsboom. The FFT code is Copyright (C) 1993 Steven Trainoff. The extended-precision conversion is Copyright (C) 1988-1991 Apple Computer Inc. The upstream text says you may do what you like with the vocoder as long as the copyright notice stays intact and you note changes. There is no warranty.\n'
        printf -- '- The build applies the prompt_user EOF exit change described in tools/vocoder/NOTES.md. The copyright notice is unchanged.\n'
        printf -- '- Notice text shipped in the app tree: vocoder-LICENSE.txt\n'
        printf -- '- File: bin/vocoder\n\n'
        printf '## yt-dlp\n\n%s\n\n' "$YTDLP_RECORD"
        printf '## deno\n\n%s\n\n' "$DENO_RECORD"
        printf '## ImageMagick (magick)\n\n%s\n\n' "$MAGICK_RECORD"
        printf '## Not bundled\n\n%s\n' "$OMITTED_RECORD"
    } > "$dest"
}

# Written offer for the GPL components. The binaries are built from the
# unmodified upstream revisions named in THIRD_PARTY.md.
nvg_write_source_offer() {
    dest=$1
    {
        printf '%s\n' "Corresponding source for the GPL components in this package"
        printf '%s\n\n' "============================================================"
        printf '%s\n\n' "These programs were built from unmodified upstream source at the revisions below. That tree is the corresponding source. No patches are applied."
        printf 'FFmpeg %s\n' "$FFMPEG_TAG"
        printf '  commit: %s\n' "$FFMPEG_COMMIT"
        printf '%s\n' "  https://github.com/FFmpeg/FFmpeg/archive/refs/tags/$FFMPEG_TAG.tar.gz"
        printf '%s\n\n' "  licence: GPL-3.0-or-later (--enable-gpl --enable-version3 --enable-libx264 --enable-frei0r)"
        printf '%s\n' "x264"
        printf '  commit: %s\n' "$X264_COMMIT"
        printf '%s\n' "  https://code.videolan.org/videolan/x264/-/archive/$X264_COMMIT/x264-$X264_COMMIT.tar.gz"
        printf '%s\n\n' "  licence: GPL-2.0-or-later. Static library linked into FFmpeg. The x264 CLI is not shipped."
        printf 'frei0r %s\n' "$FREI0R_TAG"
        printf '  commit: %s\n' "$FREI0R_COMMIT"
        printf '%s\n' "  https://github.com/dyne/frei0r/archive/refs/tags/$FREI0R_TAG.tar.gz"
        printf '%s\n\n' "  licence: GPL-2.0-or-later (nervous.cpp is GPL-3.0-or-later)"
        printf '%s\n' "You can obtain this source from the URLs above at no charge. The same revisions are in the upstream git repositories named in THIRD_PARTY.md."
        printf '%s\n' "This written offer accompanies the binaries."
    } > "$dest"
}

nvg_unzip_deno() {
    zip=$1
    dest_dir=$2
    python3 - "$zip" "$dest_dir" <<'PY'
import sys, zipfile
from pathlib import Path
zip_path, dest = sys.argv[1], Path(sys.argv[2])
dest.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(zip_path) as zf:
    names = [n for n in zf.namelist() if n == "deno" or n.endswith("/deno")]
    if len(names) != 1:
        sys.stderr.write("error: expected one deno binary in %s, found %s\n" % (zip_path, names))
        sys.exit(1)
    target = dest / "deno"
    target.write_bytes(zf.read(names[0]))
    target.chmod(0o755)
    print(target)
PY
}
