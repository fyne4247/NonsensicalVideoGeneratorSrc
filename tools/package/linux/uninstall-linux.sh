#!/bin/sh
# Remove only the files install-linux.sh created.
# Never removes the data root, even when --prefix is pointed somewhere else.

set -eu

MARKER_ID=io.github.nvg.NonsensicalVideoGenerator
APP_DIR_NAME=NonsensicalVideoGenerator

die() {
    printf 'error: %s\n' "$*" >&2
    exit 1
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
        /) die "refusing to remove /" ;;
        */) raw=${raw%/} ;;
    esac
    parent=$(dirname -- "$raw")
    base=$(basename -- "$raw")
    if [ -z "$base" ] || [ "$base" = "/" ] || [ "$base" = "." ] || [ "$base" = ".." ]; then
        die "refusing to remove $1"
    fi
    if [ "$parent" = "/" ]; then
        die "refusing to remove $raw"
    fi
    if [ ! -d "$parent" ]; then
        die "prefix parent does not exist: $parent"
    fi
    parent=$(CDPATH= cd -- "$parent" && pwd)
    case $parent in
        /) die "refusing to remove $raw" ;;
    esac
    printf '%s/%s\n' "$parent" "$base"
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

if [ -z "${HOME:-}" ] || [ ! -d "$HOME" ]; then
    die "HOME is not set to a directory"
fi

SELF=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PREFIX=$HOME/.local/opt/$APP_DIR_NAME
if [ -f "$SELF/NVG-INSTALL-MANIFEST.txt" ]; then
    PREFIX=$SELF
fi

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

PREFIX=$(abspath_dir "$PREFIX")
if prefix_is_unsafe "$PREFIX"; then
    die "refusing to remove $PREFIX"
fi
[ -f "$PREFIX/NVG-PACKAGE.txt" ] || die "not an NVG install (no NVG-PACKAGE.txt): $PREFIX"
[ -f "$PREFIX/NVG-INSTALL-MANIFEST.txt" ] || die "not an installed copy (no NVG-INSTALL-MANIFEST.txt): $PREFIX"

MAN_PREFIX=
MAN_LINK=
MAN_DESKTOP=
MAN_ICON=
MAN_ICON_SHA=
while IFS= read -r line; do
    case $line in
        prefix=*) MAN_PREFIX=${line#prefix=} ;;
        symlink=*) MAN_LINK=${line#symlink=} ;;
        desktop=*) MAN_DESKTOP=${line#desktop=} ;;
        icon=*) MAN_ICON=${line#icon=} ;;
        icon_sha256=*) MAN_ICON_SHA=${line#icon_sha256=} ;;
    esac
done < "$PREFIX/NVG-INSTALL-MANIFEST.txt"

if [ "$MAN_PREFIX" != "$PREFIX" ]; then
    die "manifest prefix does not match $PREFIX"
fi

DATA=$(data_root)
DATA_BEFORE=
if [ -d "$DATA" ]; then
    DATA_BEFORE=present
fi

if [ -n "$MAN_LINK" ] && [ -L "$MAN_LINK" ]; then
    target=$(readlink "$MAN_LINK")
    if [ "$target" = "$PREFIX/nonsensicalvideogenerator" ]; then
        rm -f "$MAN_LINK"
        printf 'removed symlink %s\n' "$MAN_LINK"
    else
        printf 'left symlink %s (it points at %s)\n' "$MAN_LINK" "$target"
    fi
elif [ -n "$MAN_LINK" ] && [ -e "$MAN_LINK" ]; then
    printf 'left %s (it is not a symlink)\n' "$MAN_LINK"
fi

if [ -n "$MAN_DESKTOP" ] && [ -f "$MAN_DESKTOP" ]; then
    if grep -q "nvg-package-marker=$MARKER_ID" "$MAN_DESKTOP" && grep -q "$PREFIX/nonsensicalvideogenerator" "$MAN_DESKTOP"; then
        rm -f "$MAN_DESKTOP"
        printf 'removed desktop file %s\n' "$MAN_DESKTOP"
    else
        printf 'left desktop file %s\n' "$MAN_DESKTOP"
    fi
fi

if [ -n "$MAN_ICON" ] && [ -f "$MAN_ICON" ]; then
    now=$(sha256_file "$MAN_ICON")
    if [ -n "$MAN_ICON_SHA" ] && [ "$now" = "$MAN_ICON_SHA" ]; then
        rm -f "$MAN_ICON"
        printf 'removed icon %s\n' "$MAN_ICON"
    else
        printf 'left icon %s (contents differ from the install)\n' "$MAN_ICON"
    fi
fi

rm -rf "$PREFIX"
printf 'removed %s\n' "$PREFIX"

rmdir "$HOME/.local/share/applications" 2>/dev/null || true
rmdir "$HOME/.local/share/icons/hicolor/256x256/apps" 2>/dev/null || true
rmdir "$HOME/.local/share/icons/hicolor/256x256" 2>/dev/null || true
rmdir "$HOME/.local/share/icons/hicolor" 2>/dev/null || true
rmdir "$HOME/.local/share/icons" 2>/dev/null || true
rmdir "$HOME/.local/bin" 2>/dev/null || true

if [ -d "$DATA" ]; then
    printf 'User data was kept at: %s\n' "$DATA"
elif [ "$DATA_BEFORE" = "present" ]; then
    die "data root disappeared: $DATA"
else
    printf 'User data was not present and was not created. Default location: %s\n' "$DATA"
fi
printf 'If XDG_DATA_HOME is set, also check $XDG_DATA_HOME/%s. Uninstall does not remove it.\n' "$APP_DIR_NAME"
printf 'NVG_DATA_DIR, when set, is the data root. Uninstall does not remove it.\n'
