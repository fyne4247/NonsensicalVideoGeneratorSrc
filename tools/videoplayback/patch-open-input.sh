#!/bin/sh
# Rebuild packages/MonoGame.Extended.VideoPlayback from the pristine input.
#
# DecodeContext stored the AVFormatContext from avformat_alloc_context, then
# passed a local copy to avformat_open_input. FFmpeg 7 frees that context on
# failure and nulls the local pointer only. Dispose then called
# avformat_free_context on the stale field and crashed in av_opt_free.
# The patch stores the local pointer back into the field before Verify
# disposes it. No other method is changed.
#
# Usage:
#   tools/videoplayback/patch-open-input.sh           # write the package dll
#   tools/videoplayback/patch-open-input.sh --check   # rebuild and compare
#
# Hashes are tools/videoplayback/HASHES.txt. MonoGame.Framework 3.8.2 is
# required to resolve constants while rewriting; set NVG_MONOGAME_FRAMEWORK
# if it is not in the user NuGet cache.

set -eu

root=$(CDPATH= cd -- "$(dirname "$0")/../.." && pwd)
mode=install
if [ "${1:-}" = "--check" ]; then
    mode=check
elif [ "${1:-}" != "" ]; then
    echo "usage: patch-open-input.sh [--check]" >&2
    exit 2
fi

user=${USER:-$(id -un)}
if [ -z "${DOTNET_ROOT:-}" ] && [ -x "/Users/$user/.dotnet-nvg/dotnet" ]; then
    DOTNET_ROOT="/Users/$user/.dotnet-nvg"
    export DOTNET_ROOT
fi
if [ -n "${DOTNET_ROOT:-}" ]; then
    PATH="$DOTNET_ROOT:$PATH"
    export PATH
fi
if [ -z "${NUGET_PACKAGES:-}" ] && [ -d "/Users/$user/.nuget/packages" ]; then
    NUGET_PACKAGES="/Users/$user/.nuget/packages"
    export NUGET_PACKAGES
fi

framework=${NVG_MONOGAME_FRAMEWORK:-}
if [ -z "$framework" ]; then
    for candidate in \
        "$HOME/.nuget/packages/monogame.framework.desktopgl/3.8.2.1105/lib/net8.0/MonoGame.Framework.dll" \
        "/Users/$user/.nuget/packages/monogame.framework.desktopgl/3.8.2.1105/lib/net8.0/MonoGame.Framework.dll" \
        "/home/$user/.nuget/packages/monogame.framework.desktopgl/3.8.2.1105/lib/net8.0/MonoGame.Framework.dll"
    do
        if [ -f "$candidate" ]; then
            framework=$candidate
            break
        fi
    done
fi
if [ -z "$framework" ] || [ ! -f "$framework" ]; then
    echo "MonoGame.Framework.dll was not found. Set NVG_MONOGAME_FRAMEWORK." >&2
    exit 1
fi

input_dll="$root/tools/videoplayback/input/MonoGame.Extended.VideoPlayback.dll"
input_pdb="$root/tools/videoplayback/input/MonoGame.Extended.VideoPlayback.pdb"
pkg_dll="$root/packages/MonoGame.Extended.VideoPlayback/MonoGame.Extended.VideoPlayback.dll"
pkg_pdb="$root/packages/MonoGame.Extended.VideoPlayback/MonoGame.Extended.VideoPlayback.pdb"
extended="$root/packages/MonoGame.Extended2/MonoGame.Extended2.dll"
sdcb="$root/packages/Sdcb.FFmpeg/Sdcb.FFmpeg.dll"
hashes="$root/tools/videoplayback/HASHES.txt"
work=${NVG_PATCH_WORK:-/tmp/nvg-hard-20/patch-work}
mkdir -p "$work"

dotnet build "$root/tools/videoplayback/PatchOpenInput/PatchOpenInput.csproj" -o "$work/patcher" --nologo -v q
dotnet exec "$work/patcher/PatchOpenInput.dll" \
    "$input_dll" "$input_pdb" \
    "$work/MonoGame.Extended.VideoPlayback.dll" "$work/MonoGame.Extended.VideoPlayback.pdb" \
    "$framework" "$extended" "$sdcb" \
    > "$work/hashes.out"

hash_of() {
    shasum -a 256 "$1" | awk '{print $1}'
}

expected_input_dll=$(awk '$1=="input-dll"{print $2}' "$hashes")
expected_input_pdb=$(awk '$1=="input-pdb"{print $2}' "$hashes")
expected_output_dll=$(awk '$1=="output-dll"{print $2}' "$hashes")
expected_output_pdb=$(awk '$1=="output-pdb"{print $2}' "$hashes")

actual_input_dll=$(hash_of "$input_dll")
actual_input_pdb=$(hash_of "$input_pdb")
actual_output_dll=$(hash_of "$work/MonoGame.Extended.VideoPlayback.dll")
actual_output_pdb=$(hash_of "$work/MonoGame.Extended.VideoPlayback.pdb")

fail=0
if [ "$actual_input_dll" != "$expected_input_dll" ] || [ "$actual_input_pdb" != "$expected_input_pdb" ]; then
    echo "input hash mismatch" >&2
    fail=1
fi
if [ "$actual_output_dll" != "$expected_output_dll" ] || [ "$actual_output_pdb" != "$expected_output_pdb" ]; then
    echo "output hash mismatch" >&2
    echo "dll $actual_output_dll" >&2
    echo "pdb $actual_output_pdb" >&2
    fail=1
fi
if [ "$fail" -ne 0 ]; then
    exit 1
fi

if [ "$mode" = "check" ]; then
    cmp -s "$work/MonoGame.Extended.VideoPlayback.dll" "$pkg_dll"
    cmp -s "$work/MonoGame.Extended.VideoPlayback.pdb" "$pkg_pdb"
    echo "patch check ok"
    exit 0
fi

cp "$work/MonoGame.Extended.VideoPlayback.dll" "$pkg_dll"
cp "$work/MonoGame.Extended.VideoPlayback.pdb" "$pkg_pdb"
echo "installed patched VideoPlayback"
cat "$work/hashes.out"
