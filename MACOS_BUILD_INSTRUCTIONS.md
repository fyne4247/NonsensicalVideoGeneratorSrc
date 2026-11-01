# macOS build instructions

Native builds use MonoGame DesktopGL. They do not use Steam, and they do not upload anything. The Windows build in `README.md` is unchanged.

A finished package is a self-contained `.app` inside a `.dmg`. A person running it does not need the .NET SDK, Homebrew, or Steam.

## Build machine

- An Apple Silicon Mac for `osx-arm64` and for `osx-x64`. The x64 package is a cross build and needs Rosetta 2 (`arch -x86_64 uname -m` prints `x86_64`). An Intel Mac can build `osx-x64` only.
- User-local .NET 8 SDK at `~/.dotnet-nvg/dotnet`. On Apple Silicon, content also needs the osx-x64 SDK at `~/.dotnet-nvg-x64/dotnet`, because the content builder's native libraries are x86_64-only. `tools/build-content-desktopgl.sh` selects that SDK and runs it under Rosetta.
- `git`, `cc`, `c++`, `make`, `python3`, and `curl` on the default macOS paths (`/usr/bin`).
- `cmake`, used only to compile frei0r and libjpeg-turbo. The script uses `/usr/bin/cmake` or, if that is absent, `/opt/homebrew/bin/cmake`. It does not run `brew install`. Configure is pointed at the prefixes this script just built, and the library audit rejects any `/opt/homebrew` or `/usr/local` dependency.
- `sips`, `iconutil`, `hdiutil`, `codesign`, `plutil`, `otool`, and `install_name_tool`, which ship with macOS.
- A few gigabytes of free space. Source trees are deleted after each library is installed.

Do not use `sudo`, Homebrew, or `apt` for this package. The FFmpeg configure runs with `/usr/bin:/bin` only, plus a pkg-config wrapper whose search path is the x264 prefix, so it does not link `/opt/homebrew` or `/usr/local` libraries.

`publish.sh` uploads to Steam. Do not run it as part of this setup.

## Build

From the repository root:

```sh
sh tools/package/build-macos-installer.sh osx-arm64 "$HOME/nvg-macos-out"
```

`osx-x64` uses the same command with that rid:

```sh
sh tools/package/build-macos-installer.sh osx-x64 "$HOME/nvg-macos-out"
```

On an arm64 Mac the script compiles x264, frei0r, FFmpeg, ImageMagick (zlib, libpng, libjpeg-turbo) and the vocoder with `clang -arch x86_64`. `uname` still says `arm64`, so the build scripts take the target from `clang -dumpmachine` and pass `--host` / `--arch=x86_64` / `CMAKE_OSX_ARCHITECTURES`. The publish step uses `~/.dotnet-nvg-x64/dotnet` under Rosetta. There is no nasm here, so x264 is `--disable-asm`, FFmpeg is `--disable-x86asm`, and libjpeg-turbo SIMD is off. Encodes still work; they are slower than an asm build. `yt-dlp_macos` is a universal binary and is thinned to x86_64. `lipo -archs` on every Mach-O in that app is `x86_64` only.

The script:

1. Builds DesktopGL content with `tools/build-content-desktopgl.sh`.
2. Publishes a self-contained Release build: `dotnet publish -c Release -r <rid> --self-contained -p:NvgPlatform=DesktopGL`.
3. Removes `steam_appid.txt`, Steam natives, the Windows FFmpeg DLLs, and `redist/` from the publish output. The DesktopGL publish step copies `baseroot/` as a whole, and those files are Windows-only.
4. Builds static x264 (`tools/x264/build-x264.sh`), frei0r plugins (`tools/frei0r/build-frei0r.sh`), and one FFmpeg 7.1 build that produces the shared libraries plus `ffmpeg` and `ffprobe` (`tools/ffmpeg7/build-ffmpeg7.sh`, `--enable-gpl --enable-libx264 --enable-frei0r`). It then builds a static ImageMagick 7 `magick` with zlib, libpng and libjpeg-turbo (`tools/magick/build-magick.sh`) and the vocoder with `tools/vocoder/build-vocoder.sh`.
5. Downloads pinned yt-dlp and deno release binaries when the upstream checksum matches and `file(1)` shows the right architecture. See `tools/package/THIRD_PARTY.md`.
6. Rewrites Mach-O install names to `@rpath` / `@loader_path`, writes `Info.plist` (bundle id `io.github.nvg.NonsensicalVideoGenerator`, version from the csproj), builds an `.icns` from `icon.ico`, ad-hoc signs with `codesign --force --deep -s -`, and writes a UDZO DMG.

Output: `NonsensicalVideoGenerator-<version>-<rid>.dmg`.

The `.app` layout puts the apphost, managed assemblies, `Content/`, `bin/`, and `lib/` in `Contents/MacOS`. The running app's resource root is that directory (`AppContext.BaseDirectory`). `bin/` holds `ffmpeg`, `ffprobe`, `magick`, `frei0r-1/`, yt-dlp, deno, and vocoder. `lib/` holds the FFmpeg 7.1 dylibs. `Contents/Resources` holds the icon and `ThirdParty/`, including `SOURCE_OFFER.txt` for the GPL components.

## Install and data

Open the DMG and drag **Nonsensical Video Generator** to the Applications symlink, or to any other folder. The package build does not write to `/Applications`.

Ad-hoc signing is enough for a local run. Gatekeeper can still block a downloaded copy the first time: Control-click the app and choose Open. Developer ID signing and notarisation need a paid Apple Developer account and are not done here.

Writable data is not stored inside the `.app`. The default data root is:

```text
~/Library/Application Support/NonsensicalVideoGenerator
```

`NVG_DATA_DIR` overrides it. Replacing or deleting the `.app` does not change that directory. The DMG does not write to it.

On launch the app adds missing stock plugins, templates, locales, and library files to the data root. It never overwrites a file that is already there, so an updated stock plugin, template, or locale does not replace a user's copy.

## What is inside the app

A person running the app does not need Homebrew, a system `ffmpeg`, or a system `magick`.

- `bin/ffmpeg` and `bin/ffprobe` from the FFmpeg 7.1 build, with libx264 and the frei0r wrapper. `ffmpeg -encoders` lists `libx264` and `aac`.
- `bin/frei0r-1/`, including `nervous` and `colorize`. The app sets `FREI0R_PATH` there.
- `bin/magick`, a static ImageMagick 7 with PNG and JPEG.
- `bin/yt-dlp`, `bin/deno`, and `bin/vocoder` when their builds or checksums succeed.

Licences, commits, and the GPL source offer are in `THIRD_PARTY.md` and `SOURCE_OFFER.txt` on the DMG and under `Contents/Resources/ThirdParty/`.

Steam files are not in the package. If the bundled tool is missing, resolution still checks `PATH`, then `/opt/homebrew/bin` and `/usr/local/bin`.

## Tools in the package

The same pins are used on both operating systems. macOS `magick` is the source build. Linux `magick` is the AppImage, and only when `magick -version` succeeds on the build machine.

| Tool | macOS | Linux | Version / commit | Licence | Source | When it is missing |
| --- | --- | --- | --- | --- | --- | --- |
| ffmpeg | bundled | bundled | n7.1.1, commit `db69d06eeeab4f46da15030a80d539efb4503ca8` | GPL-3.0-or-later | https://github.com/FFmpeg/FFmpeg | `ffmpeg missing. Looked in bundled <resource root>/bin then PATH then /opt/homebrew/bin and /usr/local/bin.` On Linux the Homebrew clause is omitted. If the binary exists but a stock encoder, codec, or filter is absent: `ffmpeg <path>: missing <list>.` |
| ffprobe | bundled | bundled | same FFmpeg build as ffmpeg | GPL-3.0-or-later | https://github.com/FFmpeg/FFmpeg | `ffprobe missing. Resolved: ffprobe` |
| magick | bundled, static source build | bundled AppImage when `magick -version` works, otherwise external | 7.1.2-34. macOS commit `a0c4bf729a26a5c2feb28b0365b47a8db736078a` | ImageMagick License | macOS: https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34.tar.xz Linux: https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34-gcc-x86_64.AppImage | `magick missing. Resolved: magick` |
| frei0r | bundled plugins | bundled plugins | tag v2.3.3, commit `cdeddc7553bbfdc446c61d1bfa6a51bcc029b4a8` | GPL-2.0-or-later. `nervous` is GPL-3.0-or-later | https://github.com/dyne/frei0r | No missing-tool sentence. Startup prints `FREI0R_PATH: <assembly dir>/bin/frei0r-1`. A missing plugin is ffmpeg's own filter error on that command. |
| vocoder | bundled | bundled | commit `3faed27b4cb457c458a1feddc309c3fd0271af5a` | permissive, notice required | https://github.com/borsboom/vocoder | No missing-tool sentence. The command log shows `> vocoder` and process start raises the runtime's file-not-found exception. |
| yt-dlp | bundled | bundled | 2026.10.21 | The Unlicense | https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp_macos and `yt-dlp_linux` | No missing-tool sentence. A library download prints `Failed to download clip: ` plus the process-start exception. |
| deno | bundled | bundled | 2.9.8 | MIT | https://github.com/denoland/deno/releases/tag/v2.9.8 | No missing-tool sentence. yt-dlp is started with `--js-runtimes deno:<resolved>`, and yt-dlp's own stderr is printed. |

## Check a DMG without installing it

```sh
sh tools/package/build-macos-installer.sh osx-arm64 "$HOME/nvg-macos-out"
```

Mount the DMG read-only, copy the `.app` to a temporary directory (not `/Applications`), and check the signature and library paths:

```sh
DMG="$HOME/nvg-macos-out/NonsensicalVideoGenerator-1.8.3.0-osx-arm64.dmg"
# Use the version in the filename the build printed.
COPY=$(mktemp -d)
# hdiutil attach -readonly -nobrowse -plist prints the mount point.
codesign -v --deep "$COPY/NonsensicalVideoGenerator.app"
```

Every Mach-O under the app should have no `/opt/homebrew` or `/usr/local` path in `otool -L`. There must be no `steam_appid.txt` and no Steam natives.

Launch once, with a temporary home and data directory:

```sh
export HOME="$COPY/home"
export NVG_DATA_DIR="$COPY/data"
mkdir -p "$HOME" "$NVG_DATA_DIR"
"$COPY/NonsensicalVideoGenerator.app/Contents/MacOS/NonsensicalVideoGenerator"
```

The log should contain `Data root:` under that temporary directory and `FFmpeg Sdcb init OK`. Quit the app after that check. One GUI launch is enough. If the window needs a click the terminal session cannot give it, open the copied `.app` from Finder once and watch the same log.

`osx-x64` is a separate DMG. The .NET apphost is not a universal binary. On Apple Silicon, run its tools and the app under Rosetta (`arch -x86_64`). A Mach-O in that app must not contain an arm64 slice.
