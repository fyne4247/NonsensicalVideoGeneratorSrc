# S1/L1 DesktopGL feasibility trial

Branch `atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-1`. Experimental stubs (Steam stand-in, `-trial` gate, file dialog, TTS) are not delivered features.

Default `dotnet build` stays WindowsDX. DesktopGL is opt-in: `-p:NvgPlatform=DesktopGL`. `NVG_STEAM` was unset.

## Commands

Mac SDK: `~/.dotnet-nvg` (8.0.427, arm64). Content MGCB: `~/.dotnet-nvg-x64` under `arch -x86_64` (Rosetta). No brew or apt changes.

WindowsDX (expected fail on this Mac):

```
DOTNET_ROOT=$HOME/.dotnet-nvg dotnet build NonsensicalVideoGenerator.csproj -c Debug
```

Exit 1. Restore succeeded. MGCB `/platform:Windows` exit 134 (MSB3073). `libFreeImage.dylib` in `dotnet-mgcb` 3.8.2.1105 is i386+x86_64 only (`DllNotFoundException` on arm64). `libfreetype6.dylib` is x86_64 only.

DesktopGL app (content pipeline skipped on the arm64 host):

```
DOTNET_ROOT=$HOME/.dotnet-nvg dotnet build NonsensicalVideoGenerator.csproj -c Debug \
  -p:NvgPlatform=DesktopGL -p:NvgSkipMgcb=true \
  -p:BaseIntermediateOutputPath=obj-desktopgl/ \
  -p:MSBuildProjectExtensionsPath=obj-desktopgl/
```

Exit 0. Output: `bin/x64/Debug/net8.0-desktopgl/`.

DesktopGL content (196 XNB, 0 failed):

```
arch -x86_64 $HOME/.dotnet-nvg-x64/dotnet tool run mgcb -- \
  /@:content/Content.DesktopGL.mgcb /platform:DesktopGL \
  /outputDir:<worktree>/content/bin/DesktopGL/Content \
  /intermediateDir:<worktree>/content/obj/DesktopGL/Content \
  /workingDir:<worktree>/content
```

Those XNB files were copied to `bin/x64/Debug/net8.0-desktopgl/Content/`.

Mac run, from that output directory, `~/.dotnet-nvg/dotnet NonsensicalVideoGenerator.dll -trial -keeplog`. Log `/tmp/nvg-mac-trial.log`. Screenshot `/tmp/nvg-mac-trial.png`.

Linux publish from the Mac:

```
dotnet publish NonsensicalVideoGenerator.csproj -c Release -r linux-x64 --self-contained true \
  -p:NvgPlatform=DesktopGL -p:NvgSkipMgcb=true \
  -p:BaseIntermediateOutputPath=obj-desktopgl/ \
  -p:MSBuildProjectExtensionsPath=obj-desktopgl-linux/ \
  -p:PublishDir=bin/linux-x64/publish/
```

`BaseIntermediateOutputPath` must stay `obj-desktopgl/` or the SDK compiles the generated AssemblyInfo twice (CS0579). Content, plugins, locales, and the library fixture were copied into the publish dir (the publish target does not). `libSDL2.so` in that dir points at the bundled `libSDL2-2.0.so.0`. rsync to `750:~/nvg-trial/nvg-desktopgl/`.

FFmpeg 7.1 shared libs were built on 750 into `~/nvg-trial/ffmpeg7` (`libavcodec.so.61` and the matching avformat/avutil/avfilter/avdevice/swscale/swresample/postproc SONAMEs). System ffmpeg stays 6.1.1 and was not aliased. Launch:

```
cd ~/nvg-trial/nvg-desktopgl
DISPLAY=:0 XAUTHORITY=$HOME/.Xauthority \
  LD_LIBRARY_PATH=$HOME/nvg-trial/ffmpeg7/lib \
  NVG_FFMPEG_LIB=$HOME/nvg-trial/ffmpeg7/lib \
  ./NonsensicalVideoGenerator -trial -keeplog
```

Session `c2` was already `Type=x11`, `Display=:0`, `State=active`. No display server was started. `xdpyinfo` on `:0` succeeded. Log `~/nvg-trial/linux-trial.log`. Screenshot `~/nvg-trial/linux-trial.png` (`import -window root`).

## Dependency audit

PE machine and ILONLY on the assemblies next to the Mac build. ILONLY x86 is AnyCPU. ILONLY x64 is rejected by the arm64 runtime until the machine id is rewritten.

| Assembly | Machine | ILONLY | Native imports |
| --- | --- | --- | --- |
| Sdcb.FFmpeg.dll | x86 | yes | avcodec-61, avformat-61, avutil-59, avfilter-10, avdevice-61, swscale-8, swresample-5, postproc-58 |
| MonoGame.Extended.VideoPlayback.dll | x64 | yes | calls Sdcb.FFmpeg.Raw (no own avcodec-61 import) |
| MonoGame.Extended2.dll | x64 | yes | none |
| MonoGame.Extended.dll (nuget 4.0.2) | x86 | yes | none |
| MoonSharp.Interpreter.dll | x86 | yes, 32BITPREFERRED | none |
| Steamworks.NET.dll | x64 | yes | steam_api64. Loads under osx-x64, FileLoadException on arm64 |
| DiscordRPC.dll 1.2.1.24 | x86 | yes | discord-rpc |
| MonoGame.Framework.dll DesktopGL | x86 | yes | SDL2 |
| MonoGame osx native SDL2 and OpenAL | universal x86_64+arm64 | n/a | bundled |
| MonoGame linux-x64 | n/a | n/a | bundled libopenal.so.1 and libSDL2-2.0.so.0 |

## Acceptance

`-trial` drives import, thumbnail, VideoPlayer, and `speed.lua` from the game thread. It does not click the library UI. The window that stayed up is the accessibility / photosensitive screen. A human click-through of Add and the video holder was not done.

| Item | macOS arm64 | Linux x64 (ssh 750, DISPLAY :0) |
| --- | --- | --- |
| Window up ≥ 30 s, fonts and locales | Pass. Alive at 30 s. Screenshot shows "Nonsensical Video Generator v1.8.3.0", pixel font, "Vybrať jazyk (Slovenčina)". | Pass. Alive at 30 s. Screenshot shows the same screen, "Select Language (English)", own taskbar entry. |
| Import local fixture and thumbnail | Pass. `TRIAL import OK ./library/video/materials/trial-fixture.mp4`. Thumbnail 2668 pixels, 8150 bytes. Black maps to alpha 192 in `BmpThumbnail`. | Pass. Same import and thumbnail lines. CLI ffmpeg is 6.1.1. |
| Preview through Extended.VideoPlayback | Pass for the player. `TRIAL playback started duration=3.0666669`, then `state=Playing position=1.6118702`. Speaker output was not metered. | Pass for the player. `state=Playing position=1.6132863`. Speaker not metered. |
| speed.lua MP4 with video and audio | Pass. `temp/job_082014826/output.mp4`. ffprobe: h264 1440x1080 and aac 48 kHz stereo. | Pass. `temp/job_082546948/output.mp4`. ffprobe: h264 and aac. Encoder Lavc60 (system ffmpeg 6.1). |
| Discord and Steam-off at startup | Pass. `[Steamworks.NET] Steam integration off (NVG_STEAM unset).` No uncaught exception. | Pass. Same Steam line. `FFmpeg Sdcb init OK`. No uncaught exception. |

Needs GUI check (one attempt, not retried): click Library, Add, pick `library/video/outros/defaultoutro.mp4`, click the holder to preview, and listen for audio. The `-trial` path already ran those operations in code.

## Verdict

No failure needs a renderer or video-stack rewrite. Extended.VideoPlayback decoded and played on both OSes once FFmpeg 7 was visible to Sdcb.

Localized:

- arm64 MGCB cannot load FreeImage or freetype. Content was built with the osx-x64 SDK under Rosetta into a separate output dir. `NvgSkipMgcb=true` on the arm64 app build.
- `Steamworks.NET.dll` is an IL-only image marked AMD64, so arm64 CoreCLR will not load it. DesktopGL compiles an in-process no-op `Steamworks` stand-in and does not reference the DLL. WindowsDX still references `packages/Steamworks.NET/Steamworks.NET.dll`.
- `MonoGame.Extended2.dll` and `MonoGame.Extended.VideoPlayback.dll` are IL-only images marked AMD64. `src/backend/platform/retarget-ilonly.py` rewrites those output copies to machine I386. Native images are left alone. A linux-x64 publish also rewrites `NonsensicalVideoGenerator.dll` the same way; the linux-x64 host still ran it.
- Sdcb.FFmpeg's static constructor already calls `SetDllImportResolver` and maps `avcodec-61` to `libavcodec.61.dylib` or `libavcodec.so.61`. A second resolver throws `InvalidOperationException`. The trial stages those files next to the app. On Linux, .NET then looks up `libavcodec.so.61.so`, so that name is staged too. `LD_LIBRARY_PATH` must include `~/nvg-trial/ffmpeg7/lib` so the SONAME dependencies resolve. Mac used keg-only `/opt/homebrew/opt/ffmpeg@7/lib` (absolute install names). Smoke: Mac `avcodec_version=4002661`, Linux `avcodec_version=4002660`.
- Thumbnail decode on DesktopGL uses `BmpThumbnail` and uploads on the game thread. Windows still uses `System.Drawing.Bitmap`.
- Boot movie log line `Could not load boot video:` had an empty path. The fixture preview still started.

`public` was already at `38995cb4a419b7e511f1f1d462e01fb5b0dcbf5c`. Locales copied from `public/locales`.
