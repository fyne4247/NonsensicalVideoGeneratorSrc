# Linux build instructions

Native DesktopGL is the way to build and run Nonsensical Video Generator on Linux. The Proton section below is an old fallback for the Windows build. It is not part of the native setup.

Nothing in this setup uploads a build. `publish.sh` runs `steamcmd` and uploads to Steam. Do not run it.

The native package is a self-contained linux-x64 `.tar.gz` with a per-user installer. A person who only runs the installed app does not need the .NET SDK.

## Build machine and run time

Build prerequisites:

- Linux x86_64.
- User-local .NET 8 SDK at `~/.dotnet-nvg/dotnet`. No system package install.
- `git`, `cc`, `c++`, `make`, `python3`, `curl`, and `cmake`. The script does not install them.
- A few gigabytes of free scratch space. The FFmpeg object tree is deleted after the libraries are copied.

Runtime prerequisites:

- The unpacked or installed tree from this package. `ffmpeg`, `ffprobe`, and frei0r are inside it. A render does not need a system ffmpeg.
- `magick` is the official AppImage when `magick -version` works on the build machine. If that fails, usually because FUSE is missing, magick is left out and the app looks on `PATH`.
- An X11 display for the window (`DISPLAY`). Wayland was not given its own check.

No `sudo`, no `apt`, and no `libjpeg` symlink. Those belonged to the Proton notes and are not used here.

## Build the tarball

From the repository root, on the Linux machine:

```sh
sh tools/package/build-linux-package.sh "$HOME/nvg-linux-out"
```

The script publishes `-c Release -r linux-x64 --self-contained -p:NvgPlatform=DesktopGL`, builds content with `tools/build-content-desktopgl.sh`, builds static x264, frei0r, and one FFmpeg 7.1 that includes `ffmpeg` and `ffprobe` with libx264, builds the vocoder, and downloads pinned yt-dlp, deno, and (when `magick -version` works) the official ImageMagick AppImage. Checksums, licences, and `SOURCE_OFFER.txt` are written into the archive. Steam files are removed from the publish output. On x86_64, a missing nasm configures x264 with `--disable-asm`.

Output: `NonsensicalVideoGenerator-<version>-linux-x64.tar.gz`.

The archive's top directory contains:

- `NonsensicalVideoGenerator`, the self-contained apphost
- `nonsensicalvideogenerator`, a POSIX `sh` launcher that resolves its own directory, sets `LD_LIBRARY_PATH` and `NVG_FFMPEG_LIB` to `lib/`, and forwards every argument
- `lib/`, the FFmpeg 7.1 `.so` files. `bin/ffmpeg` and `bin/ffprobe` use `RUNPATH` `$ORIGIN/../lib`.
- `bin/`, including `ffmpeg`, `ffprobe`, `frei0r-1/`, vocoder, and the other CLI tools that were bundled
- `install-linux.sh` and `uninstall-linux.sh`
- content, plugins, templates, locales, and the stock library seed

Paths with spaces are fine. Quote them.

## Tools in the package

The same pins are used on both operating systems. macOS `magick` is the source build. Linux `magick` is the AppImage, and only when `magick -version` succeeds on the build machine.

| Tool | macOS | Linux | Version / commit | Licence | Source | When it is missing |
| --- | --- | --- | --- | --- | --- | --- |
| ffmpeg | bundled | bundled | n7.1.1, commit `db69d06eeeab4f46da15030a80d539efb4503ca8` | GPL-3.0-or-later | https://github.com/FFmpeg/FFmpeg | `ffmpeg missing. Looked in bundled <resource root>/bin then PATH.` On macOS that continues ` then /opt/homebrew/bin and /usr/local/bin.` If the binary exists but a stock encoder, codec, or filter is absent: `ffmpeg <path>: missing <list>.` |
| ffprobe | bundled | bundled | same FFmpeg build as ffmpeg | GPL-3.0-or-later | https://github.com/FFmpeg/FFmpeg | `ffprobe missing. Resolved: ffprobe` |
| magick | bundled, static source build | bundled AppImage when `magick -version` works, otherwise external | 7.1.2-34. macOS commit `a0c4bf729a26a5c2feb28b0365b47a8db736078a` | ImageMagick License | macOS: https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34.tar.xz Linux: https://github.com/ImageMagick/ImageMagick/releases/download/7.1.2-34/ImageMagick-7.1.2-34-gcc-x86_64.AppImage | `magick missing. Resolved: magick` |
| frei0r | bundled plugins | bundled plugins | tag v2.3.3, commit `cdeddc7553bbfdc446c61d1bfa6a51bcc029b4a8` | GPL-2.0-or-later. `nervous` is GPL-3.0-or-later | https://github.com/dyne/frei0r | No missing-tool sentence. Startup prints `FREI0R_PATH: <assembly dir>/bin/frei0r-1`. A missing plugin is ffmpeg's own filter error on that command. |
| vocoder | bundled | bundled | commit `3faed27b4cb457c458a1feddc309c3fd0271af5a` | permissive, notice required | https://github.com/borsboom/vocoder | No missing-tool sentence. The command log shows `> vocoder` and process start raises the runtime's file-not-found exception. |
| yt-dlp | bundled | bundled | 2026.10.21 | The Unlicense | https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/yt-dlp_macos and `yt-dlp_linux` | No missing-tool sentence. A library download prints `Failed to download clip: ` plus the process-start exception. |
| deno | bundled | bundled | 2.9.8 | MIT | https://github.com/denoland/deno/releases/tag/v2.9.8 | No missing-tool sentence. yt-dlp is started with `--js-runtimes deno:<resolved>`, and yt-dlp's own stderr is printed. |

## Install, upgrade, uninstall

Extract the archive and install for the current user. The default prefix is `~/.local/opt/NonsensicalVideoGenerator`.

```sh
tar -xzf NonsensicalVideoGenerator-1.8.3.0-linux-x64.tar.gz
cd NonsensicalVideoGenerator-1.8.3.0-linux-x64
sh install-linux.sh
```

A different prefix:

```sh
sh install-linux.sh --prefix "$HOME/apps/Nonsensical Video Generator"
```

The installer creates:

- the prefix (a copy of the archive tree)
- `~/.local/bin/nonsensicalvideogenerator` pointing at the launcher
- `~/.local/share/applications/io.github.nvg.NonsensicalVideoGenerator.desktop`
- `~/.local/share/icons/hicolor/256x256/apps/nonsensicalvideogenerator.png`

It does not use `sudo`. It does not follow `XDG_DATA_HOME` for those three files. It refuses a prefix of `/`, your home directory, `~/.local`, the data root, or a directory that already contains the data root. If the prefix already holds this app, an upgrade replaces only that app directory and does not touch user data. If the prefix exists and is not one of these installs, the installer stops.

Run it from a menu or from a terminal:

```sh
nonsensicalvideogenerator
```

Uninstall from the installed copy, or pass the same prefix:

```sh
sh "$HOME/.local/opt/NonsensicalVideoGenerator/uninstall-linux.sh"
sh uninstall-linux.sh --prefix "$HOME/apps/Nonsensical Video Generator"
```

Uninstall removes the prefix, the symlink when it still points at that prefix, the desktop file when it still has this app's marker, and the icon when its checksum still matches. It prints where user data stays, and it does not delete it.

The default data root is:

```text
~/.local/share/NonsensicalVideoGenerator
```

If `XDG_DATA_HOME` is set, the app uses `$XDG_DATA_HOME/NonsensicalVideoGenerator`. `NVG_DATA_DIR` overrides both. On launch the app adds missing stock plugins, templates, locales, and library files to the data root. It never overwrites a file that is already there, so an updated stock plugin, template, or locale does not replace a user's copy. The installer replaces only the app directory and never touches that data.

## Check the package

Extract into a directory whose path contains a space. Install with a temporary `HOME` and a prefix that also contains a space. Launch through the symlink with `DISPLAY` set and `NVG_DATA_DIR` pointed at a temporary directory. The process should stay up, the log should show `Data root:` under that directory, and `ldd` on `lib/libavcodec.so.61` with the bundle's `LD_LIBRARY_PATH` should resolve the other FFmpeg libraries inside the prefix. Uninstall and confirm the data directory is still there.

```sh
export HOME=/tmp/nvg-home
export NVG_DATA_DIR=/tmp/nvg-data
mkdir -p "$HOME" "$NVG_DATA_DIR"
sh install-linux.sh --prefix "/tmp/nvg prefix"
"$HOME/.local/bin/nonsensicalvideogenerator"
```

Use a real temporary `HOME` you can delete, not your actual home.

## Proton fallback

`proton.sh`, `start.sh`, and `install-dotnet-sdk-in-proton.sh` are the previous route: the Windows build under GE-Proton. They are not used by the native package. Known defects, left as they are:

- `install-dotnet-sdk-in-proton.sh` passes the output path to `wget` as a second URL (`wget <url> .temp/...`) instead of `wget -O`. It does not create `.temp`, does not quote the path, and does not check the download.
- `proton.sh` does not quote `$HOME`, `$1`, `$2`, or `$3`, so a path with a space breaks. It links `~/NVGDev` into Steam's compatdata for app id 2516360 and expects GE-Proton10-34 under `~/.local/share/Steam`.
- `start.sh` calls `./proton.sh` with a relative Debug output path and no quotes. It only works from the repository root, and only for that Debug Windows build.
- `publish.sh` uploads with `steamcmd`. It is not a setup step.

The old notes that installed `steamcmd` with `apt` and created `libjpeg` ABI symlinks with `sudo` do not apply to the native build. Do not add those symlinks.
