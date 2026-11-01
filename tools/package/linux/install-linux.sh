#!/bin/sh
# Install a built Nonsensical Video Generator tree for the current user.
#
# Default prefix: ~/.local/opt/NonsensicalVideoGenerator
# Also creates:
#   ~/.local/bin/nonsensicalvideogenerator
#   ~/.local/share/applications/io.github.nvg.NonsensicalVideoGenerator.desktop
#   ~/.local/share/icons/hicolor/256x256/apps/nonsensicalvideogenerator.png
#
# Upgrade replaces only that prefix. It does not touch the data root
# (~/.local/share/NonsensicalVideoGenerator, or XDG_DATA_HOME / NVG_DATA_DIR).
# No sudo.

set -eu

MARKER_ID=io.github.nvg.NonsensicalVideoGenerator
APP_DIR_NAME=NonsensicalVideoGenerator

die() {
    printf 'error: %s\n' "$*" >&2
    exit 1
}

self_dir() {
    CDPATH= cd -- "$(dirname -- "$0")" && pwd
}

sha256_file() {
    if command -v shasum >/dev/null 2>&1; then
        shasum -a 256 "$1" | awk '{ print $1 }'
    else
        sha256sum "$1" | awk '{ print $1 }'
    fi
}

abspath_dir() {
    raw=$1
    case $raw in
        /*) ;;
        *) raw=$(pwd)/$raw ;;
    esac
    raw=$(printf '%s' "$raw" | tr -s '/')
    case $raw in
        /) die "refusing prefix /" ;;
        */) raw=${raw%/} ;;
    esac
    parent=$(dirname -- "$raw")
    base=$(basename -- "$raw")
    if [ -z "$base" ] || [ "$base" = "/" ] || [ "$base" = "." ] || [ "$base" = ".." ]; then
        die "refusing prefix $1"
    fi
    if [ "$parent" = "/" ]; then
        die "refusing prefix $raw"
    fi
    mkdir -p -- "$parent"
    parent=$(CDPATH= cd -- "$parent" && pwd)
    case $parent in
        /) die "refusing prefix $raw" ;;
    esac
    printf '%s/%s\n' "$parent" "$base"
}

refuse_prefix_text() {
    case $1 in
        *\"*|*\$*|*\\*|*'`'*|*\|*|*\&*|*'
'*)
            die "prefix contains a character this installer will not put in a desktop file"
            ;;
    esac
}

data_root() {
    printf '%s/.local/share/%s\n' "$HOME" "$APP_DIR_NAME"
}

prefix_is_unsafe() {
    prefix=$1
    home=$HOME
    data=$(data_root)
    mac_data=$home/Library/Application\ Support/$APP_DIR_NAME
    case $prefix in
        /|/usr|/usr/local|/opt|/opt/homebrew|/Applications|/bin|/sbin|/lib|/lib64|/etc|/var|/System|/Users|/home|/tmp|/private|/private/tmp|/Volumes)
            return 0
            ;;
        "$home"|"$home/.local"|"$home/.local/share"|"$home/.local/bin"|"$home/Library"|"$home/Library/Application Support")
            return 0
            ;;
        "$data"|"$data"/*|"$mac_data"|"$mac_data"/*)
            return 0
            ;;
    esac
    case $data in
        "$prefix"|"$prefix"/*) return 0 ;;
    esac
    case $mac_data in
        "$prefix"|"$prefix"/*) return 0 ;;
    esac
    return 1
}

STAGE=
OLD=
PREFIX=

cleanup() {
    status=$?
    if [ "$status" -eq 0 ]; then
        return
    fi
    if [ -n "$STAGE" ] && [ -d "$STAGE" ]; then
        rm -rf "$STAGE"
    fi
    if [ -n "$OLD" ] && [ -d "$OLD" ] && [ -n "$PREFIX" ] && [ ! -d "$PREFIX" ]; then
        mv "$OLD" "$PREFIX"
        printf 'restored the previous install at %s\n' "$PREFIX" >&2
    elif [ -n "$OLD" ] && [ -d "$OLD" ]; then
        printf 'previous install kept at %s\n' "$OLD" >&2
    fi
}
trap cleanup EXIT

if [ -z "${HOME:-}" ] || [ ! -d "$HOME" ]; then
    die "HOME is not set to a directory"
fi

PREFIX=$HOME/.local/opt/$APP_DIR_NAME
while [ "$#" -gt 0 ]; do
    case $1 in
        --prefix)
            [ "$#" -ge 2 ] || die "--prefix needs a directory"
            PREFIX=$2
            shift 2
            ;;
        --prefix=*)
            PREFIX=${1#--prefix=}
            [ -n "$PREFIX" ] || die "--prefix needs a directory"
            shift
            ;;
        *)
            die "unknown argument: $1"
            ;;
    esac
done

SRC=$(self_dir)
[ -f "$SRC/NVG-PACKAGE.txt" ] || die "NVG-PACKAGE.txt is missing next to install-linux.sh"
[ -f "$SRC/nonsensicalvideogenerator" ] || die "launcher is missing next to install-linux.sh"
[ -f "$SRC/NonsensicalVideoGenerator" ] || die "app binary is missing next to install-linux.sh"
[ -f "$SRC/share/nonsensicalvideogenerator.desktop" ] || die "desktop template is missing"
[ -f "$SRC/share/icon.png" ] || die "icon is missing"
[ -f "$SRC/uninstall-linux.sh" ] || die "uninstall-linux.sh is missing"

refuse_prefix_text "$PREFIX"
PREFIX=$(abspath_dir "$PREFIX")
refuse_prefix_text "$PREFIX"
if prefix_is_unsafe "$PREFIX"; then
    die "refusing prefix $PREFIX"
fi

BIN_LINK=$HOME/.local/bin/nonsensicalvideogenerator
DESKTOP=$HOME/.local/share/applications/io.github.nvg.NonsensicalVideoGenerator.desktop
ICON=$HOME/.local/share/icons/hicolor/256x256/apps/nonsensicalvideogenerator.png

if [ -L "$BIN_LINK" ]; then
    target=$(readlink "$BIN_LINK")
    case $target in
        "$PREFIX/nonsensicalvideogenerator") ;;
        *) die "refusing to replace $BIN_LINK (it points at $target)" ;;
    esac
elif [ -e "$BIN_LINK" ]; then
    die "refusing to replace $BIN_LINK because it is not a symlink"
fi

if [ -e "$DESKTOP" ] && [ ! -f "$DESKTOP" ]; then
    die "refusing to replace $DESKTOP because it is not a file"
fi
if [ -f "$DESKTOP" ] && ! grep -q "nvg-package-marker=$MARKER_ID" "$DESKTOP"; then
    die "refusing to replace $DESKTOP because it is not an NVG desktop file"
fi

PARENT=$(dirname -- "$PREFIX")
BASE=$(basename -- "$PREFIX")
STAGE=$PARENT/.$BASE.nvg-new.$$
rm -rf "$STAGE"
mkdir -p -- "$STAGE"
cp -a "$SRC"/. "$STAGE"/
chmod 755 "$STAGE/nonsensicalvideogenerator" "$STAGE/NonsensicalVideoGenerator" "$STAGE/install-linux.sh" "$STAGE/uninstall-linux.sh"

if [ -d "$PREFIX" ]; then
    if [ -f "$PREFIX/NVG-PACKAGE.txt" ]; then
        OLD=$PARENT/.$BASE.nvg-old.$$
        rm -rf "$OLD"
        mv "$PREFIX" "$OLD"
    else
        contents=$(find "$PREFIX" -mindepth 1 -print -quit || true)
        if [ -n "$contents" ]; then
            die "prefix exists and is not an NVG install: $PREFIX"
        fi
        rmdir "$PREFIX"
    fi
fi

mv "$STAGE" "$PREFIX"
STAGE=

mkdir -p -- "$(dirname -- "$BIN_LINK")" "$(dirname -- "$DESKTOP")" "$(dirname -- "$ICON")"
ln -sfn "$PREFIX/nonsensicalvideogenerator" "$BIN_LINK"
awk -v prefix="$PREFIX" -v icon="$ICON" '
    { gsub(/@PREFIX@/, prefix); gsub(/@ICON@/, icon); print }
' "$PREFIX/share/nonsensicalvideogenerator.desktop" > "$DESKTOP"
cp "$PREFIX/share/icon.png" "$ICON"
ICON_SHA=$(sha256_file "$ICON")
cat > "$PREFIX/NVG-INSTALL-MANIFEST.txt" <<EOF
prefix=$PREFIX
symlink=$BIN_LINK
desktop=$DESKTOP
icon=$ICON
icon_sha256=$ICON_SHA
EOF

if [ -n "$OLD" ] && [ -d "$OLD" ]; then
    rm -rf "$OLD"
    OLD=
fi

printf 'Installed Nonsensical Video Generator\n'
printf '  App: %s\n' "$PREFIX"
printf '  Command: %s\n' "$BIN_LINK"
printf '  Data stays at: %s\n' "$(data_root)"
printf '  If XDG_DATA_HOME is set, the app uses $XDG_DATA_HOME/%s instead.\n' "$APP_DIR_NAME"
printf '  NVG_DATA_DIR overrides both. This installer does not create or remove that data.\n'
trap - EXIT
