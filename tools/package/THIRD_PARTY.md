# Third-party components

This is the template for the `THIRD_PARTY.md` written into each package.
`build-macos-installer.sh` and `build-linux-package.sh` fill in the commit,
checksums, and `file(1)` lines at build time. Pins live in the build scripts.
`SOURCE_OFFER.txt` in the package names the same GPL revisions.

Community packages do not include Steam files, `steam_appid.txt`, or Steam
natives. Workshop and achievements stay unavailable.

## Built from source

### FFmpeg n7.1.1

- Source: https://github.com/FFmpeg/FFmpeg tag `n7.1.1`
- Licence: GPL-3.0-or-later
- One build (`tools/ffmpeg7/build-ffmpeg7.sh`) produces the shared libraries and `bin/ffmpeg` plus `bin/ffprobe`, with `--enable-gpl --enable-libx264 --enable-frei0r`.
- Configure cannot see Homebrew. x264 is found through a pkg-config wrapper limited to the x264 prefix.

### x264

- Source: https://code.videolan.org/videolan/x264 commit `b35605ace3ddf7c1a5d67a2eb553f034aef41d55` (stable)
- Licence: GPL-2.0-or-later
- Static PIC library (`tools/x264/build-x264.sh`, `--disable-cli`). Linked into FFmpeg. The x264 program is not shipped.
- On x86_64, a missing nasm configures `--disable-asm`. aarch64 keeps the platform assembler. An osx-x64 build on an arm64 Mac is this case: `clang -arch x86_64` with `--host` set from `-dumpmachine`, and no nasm.

### frei0r v2.3.3

- Source: https://github.com/dyne/frei0r commit `cdeddc7553bbfdc446c61d1bfa6a51bcc029b4a8`
- Licence: GPL-2.0-or-later. `nervous.cpp` is GPL-3.0-or-later.
- Built by `tools/frei0r/build-frei0r.sh` without OpenCV, Cairo, or gavl.
- Installed as `bin/frei0r-1/`. The app sets `FREI0R_PATH` there.

### vocoder

- Source: https://github.com/borsboom/vocoder commit `3faed27b4cb457c458a1feddc309c3fd0271af5a` (pinned in `tools/vocoder/build-vocoder.sh`)
- Licence: permissive, notice required. Zerius Vocoder is Copyright (C) 1996-1999, 2002 Emanuel Borsboom. The FFT code is Copyright (C) 1993 Steven Trainoff. The extended-precision conversion is Copyright (C) 1988-1991 Apple Computer Inc.
- The build applies the stdin-EOF exit change described in `tools/vocoder/NOTES.md`.
- File in the package: `bin/vocoder`
- Notice text already in the app tree: `vocoder-LICENSE.txt`

### ImageMagick 7.1.2-34 (macOS)

- Source tarball: `ImageMagick-7.1.2-34.tar.xz`, SHA-256 `55fe0ec246245b46c429f9e6bc65294ad4a78a66b88abd9817971ca157856323`
- Git tag commit: `a0c4bf729a26a5c2feb28b0365b47a8db736078a`
- Licence: ImageMagick License
- Static `bin/magick` from `tools/magick/build-magick.sh`. No X11.
- Delegates, also built from source: zlib 1.3.1, libpng 1.6.47, libjpeg-turbo 3.1.0. An osx-x64 cross build passes `--host` from the compiler triple. libjpeg-turbo SIMD is off when nasm is missing.

## Official release binaries, pinned

Bundled only when the upstream checksum matches and `file(1)` shows the architecture for that package. Otherwise the tool is left out and `ToolPaths` falls through to `PATH`.

### yt-dlp 2026.10.21

- Licence: The Unlicense
- macOS: `yt-dlp_macos` (universal x86_64 + arm64), SHA-256 `7a6fe71d7d680f1fb0ac18d4949873d77db2859e90017c0f44bd94b23be4ee40`. The osx-x64 package keeps only the x86_64 slice (`lipo -thin`).
- Linux x64: `yt-dlp_linux`, SHA-256 `d8bd33fca9d5b23bd5b9952b3fca36db5c46331933fda9f598a3da14ce2c243f`
- Checksums: https://github.com/yt-dlp/yt-dlp/releases/download/2026.10.21/SHA2-256SUMS
- Installed as `bin/yt-dlp`

### deno 2.9.8

- Licence: MIT
- macOS arm64: `deno-aarch64-apple-darwin.zip`, SHA-256 `9f0bf660393da2eaff044cdc7444cb08c73f235849b61d224d825a194ce7c98e`
- macOS x64: `deno-x86_64-apple-darwin.zip`, SHA-256 `4b2f5edd2639b7470f034238b321d7002d8dfb4726d8558f52c17afac370eb2c`
- Linux x64: `deno-x86_64-unknown-linux-gnu.zip`, SHA-256 `e890975bc2549807bbcc0b584ffafda72cc7c93349dd99c80483dd784f6c4609`
- Each zip has a sibling `.sha256sum` on the v2.9.8 release.
- Installed as `bin/deno`

### ImageMagick 7.1.2-34 (Linux AppImage)

- Licence: ImageMagick License
- Official `ImageMagick-7.1.2-34-gcc-x86_64.AppImage`, SHA-256 `6e9edfd35d5f30b57c27533afe003d6085faaf8607b207426f398d11751acf93` (GitHub release asset digest)
- Installed as `bin/magick` only if `magick -version` exits 0 on the build machine. An AppImage that needs FUSE and cannot start is left out.

## Not bundled

- Steam
- The x264 command-line program
