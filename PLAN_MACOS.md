# macOS plan (Opus findings, 2026-11-01, reviewed)

**Feasibility: PROVISIONAL.** Native DesktopGL looks practical, but nothing has been built or launched yet.
It becomes "confirmed" only when spike S1 (below) passes.

Each claim is marked **[verified]** (checked on this Mac / against upstream on 2026-11-01) or **[unverified]**.

## Current state
- [verified] `NonsensicalVideoGenerator.csproj`: `net8.0-windows`, `UseWindowsForms`, `MonoGame.Framework.WindowsDX` 3.8.2.1105,
  `DefineConstants=WINDOWSDX`, `Microsoft.WindowsDesktop.App`, `System.Speech`, `WindowsAPICodePack`.
- [verified] The existing Linux path (`LINUX_BUILD_INSTRUCTIONS.md`, `proton.sh`, `start.sh`) runs the Windows build under GE-Proton. Nothing is native.
- [verified] `src/` has 58 .cs files and about 18.8k lines.
- [verified] Dev Mac is arm64 (Apple Silicon). Homebrew has ffmpeg 9.0.2, ffmpeg@7 7.1.5 (installed, keg-only), magick, yt-dlp, deno.
  **dotnet is not installed. wine is not installed.**

## Windows-only touchpoints (shared with the Linux plan)
| Area | Where | Native replacement |
|---|---|---|
| Graphics backend | csproj | `MonoGame.Framework.DesktopGL` 3.8.2.1105. [verified] `content/` has no `.fx` files, so no mgfxc/wine dependency for content. [unverified] DesktopGL 3.8.2 ships arm64 macOS natives (SDL2/OpenAL); S1 checks this |
| WinForms drag-drop / close hook | `src/backend/UserInterface.cs:154,195,270` (`#if WINDOWSDX`) | Already guarded. [unverified] use DesktopGL `Window.FileDrop` in the `#else` branch |
| File dialogs | `src/data/PluginHandler.cs:763,1912,1956`; `src/pages/LibraryPage.cs:1408` | `IFileDialog`: Windows=WinForms; macOS=`osascript` `choose file`; Linux=`zenity`/`kdialog` |
| Clipboard | `src/interactables/TextInput.cs:325,354,359`; `src/pages/LibraryPage.cs:929` | SDL2 `SDL_GetClipboardText`/`SDL_SetClipboardText` P/Invoke (SDL is already loaded by DesktopGL) |
| TTS | `src/backend/Accessibility.cs` (`System.Speech`) | macOS `say`; Linux `spd-say`/`espeak-ng`; Windows unchanged |
| Kill child procs | `src/backend/Generator.cs:73` (powershell + CIM) | Track spawned `Process` objects, or use `Process.Kill(true)` |
| Open folder | `src/pages/LibraryPage.cs:1150`, `src/pages/PluginsPage.cs:784,1232` (`explorer.exe`) | `open` / `xdg-open` |
| Tool paths | `src/data/PluginHandler.cs:80-88,245,575-585`; `src/backend/Generator.cs` (14 `@".\bin\*.exe"` sites); `src/data/LibraryData.cs:605` | One `ToolPaths` helper (OS exe suffix). On mac/Linux, default `useSystem*` (`src/backend/Global.cs:29-32`) to true, with `bin/` as the fallback |
| frei0r env | `src/backend/Program.cs:61` (`EnvironmentVariableTarget.User`, a no-op on Unix) | Also set it at Process level. [verified] `brew` has `frei0r` 3.6.0 |
| Lua plugin paths | `baseplugins/user/stock/rave.lua` (`.\\frames\\...`) | Use `/` |
| Backslash paths | ~40 hits in `src/` | Sweep to `Path.Combine` / `/` |
| Thumbnails (System.Drawing) | `src/pages/LibraryPage.cs:504` (`System.Drawing.Bitmap`; Sol found it) | Not supported on Unix in .NET 8. Replace with MonoGame `Texture2D` pixel read or ffmpeg RGBA output. Keep the black→alpha conversion and graphics-thread safety. Plain `System.Drawing.Color` uses are a separate, harmless case |
| Video playback | vendored `MonoGame.Extended.VideoPlayback` | DesktopGL has no built-in VideoPlayer, so S1 must check this assembly separately (preview playback) |
| Discord RPC | `DiscordRichPresence` 1.2.1.24 | [unverified] cross-platform (Unix socket); S1 checks that it doesn't throw |

## FFmpeg native ABI
- [verified] `packages/Sdcb.FFmpeg/Sdcb.FFmpeg.dll` binds `avcodec-61`, `avformat-61`, `avutil-59`, `avfilter-10`, `avdevice-61`,
  `swscale-8`, `swresample-5`, `postproc-58` = **FFmpeg 7.x**. `MonoGame.Extended.VideoPlayback` uses the same API.
- [verified] Homebrew `ffmpeg` (9.0.2) ships `libavcodec.63`, which is **ABI-incompatible**.
- [verified] Homebrew `ffmpeg@7` 7.1.5 (keg-only, `/opt/homebrew/opt/ffmpeg@7/lib`) ships exactly the required majors:
  `libavcodec.61`, `libavformat.61`, `libavutil.59`, `libavfilter.10`, `libavdevice.61`, `libswscale.8`, `libswresample.5`, `libpostproc.58`. They're arm64 Mach-O.
- .NET won't map `avcodec-61` → `libavcodec.61.dylib` by itself, so register `NativeLibrary.SetDllImportResolver` for the
  Sdcb.FFmpeg and VideoPlayback assemblies (mac: `lib<name>.<major>.dylib`; Linux: `lib<name>.so.<major>`).
- [unverified] `Sdcb.FFmpeg.dll` header says PE32/x86, but it's a .NET assembly, so it's probably AnyCPU IL. S1 must load it on arm64.
- Default: the CLI ffmpeg/ffprobe can stay on Homebrew `ffmpeg` (any version). Only the in-process libs need @7.

## Steamworks
- [verified] The vendored `packages/Steamworks.NET/Steamworks.NET.dll` is **v20.2.0, the Windows build** (binds `steam_api64`).
- [verified] Upstream ships cross-platform artifacts, so the owner doesn't need to supply an SDK:
  - `Steamworks.NET-Standalone_20.2.0.zip` (same version as vendored, HTTP 200) has `OSX-Linux-x64/Steamworks.NET.dll`,
    `libsteam_api.so` and `steam_api.bundle/Contents/MacOS/libsteam_api.dylib`.
  - In 2025.165.0 that dylib is **universal (x86_64 + arm64)** [verified]; 20.2.0's needs the same check (S1).
  - NuGet `Steamworks.NET` 2024.8.0 has `runtimes/{win,osx,linux}-*/` managed DLLs but **no native libs** [verified], so still use the standalone zip natives.
- Recommended default: vendor the 20.2.0 OSX-Linux build next to the Windows one and pick it per build config.
- **Ownership context (owner, 2026-11-01):** the owner does **not** control the Steam listing (app 2516360; a friend sold the rights). So:
  - Community/local builds and installers (the deliverable for this work) are built with Steam integration **off** (`NVG_STEAM` build flag
    unset): no `RestartAppIfNecessary` (it would bounce launches to someone else's Steam app), no `steam_appid.txt`, no Steam natives shipped.
    Workshop/achievements show as visibly unavailable.
  - The Steam build path (flag set) keeps today's ownership/`RestartAppIfNecessary` checks unchanged (`src/data/SteamManager.cs:34`), and
    stays available for whoever controls the listing. Optional local dev Steam integration (flag set + Steam running) is allowed for testing.
  - Nothing in this plan requires the owner to supply publisher credentials, Steamworks partner access or SDK downloads. Any native
    Steam depot or publishing has to be coordinated with the listing's controller and is out of scope.

## Vocoder
- [verified] Upstream source exists: `github.com/borsboom/vocoder`, plain C (`main.c`, `vocode.c`, `fftn.c`, `aiff.c`, `riff.c`) + `Makefile`.
- Default: build it natively with clang/gcc (spike S2). If it doesn't build or run cleanly, the vocoder plugin type shows a visible
  "Vocoder unavailable on this platform" message instead of failing silently.

## Build shape (recommended default)
One csproj, an extra configuration switch, and the Windows/Steam pipeline unchanged:
- `-p:NvgPlatform=DesktopGL` → `TargetFramework=net8.0`, `MonoGame.Framework.DesktopGL`, `DefineConstants=DESKTOPGL`,
  no WinForms/System.Speech/WindowsAPICodePack, `OutputType=Exe`, RIDs `osx-arm64`/`osx-x64`/`linux-x64`.
- Default (no flag) = today's WindowsDX build.
- The CopyPlugins target hardcodes `bin\x64\...\net8.0-windows`, so change it to `$(OutDir)`. `.vscode/tasks.json` and the scripts gain mac/Linux variants.
- Dev runs use `dotnet NonsensicalVideoGenerator.dll`. Installers are covered below.

## Installer scope (owner: mac/Linux installers alongside Windows installers)
- Deliverable: a self-contained `.app` in a `.dmg`, published per arch (`osx-arm64`, `osx-x64`; .NET apphost isn't universal, so ship two DMGs
  first, and a universal bundle via `lipo` is optional later). Users must not need the .NET SDK, Homebrew or Steam.
- Bundle contents: `Info.plist`, `.icns` from `icon.ico`, the published app and content, app-local FFmpeg 7.1 dylibs (avcodec 61 set) with install
  names rewritten (`install_name_tool`/dylibbundler, recording provenance and GPL licence), plus the CLI tools (ffmpeg/ffprobe, magick,
  yt-dlp macOS binary, deno, frei0r plugins, native vocoder if S2 passes) in `Contents/Resources/bin`. Copying from Homebrew pulls in
  many transitive dylibs, so prefer pinned source/static builds and record each one's version + licence.
- Writable data (settings, library, renders, plugins) goes to `~/Library/Application Support/NonsensicalVideoGenerator`, not inside the
  `.app` (signed bundles must stay read-only). The app currently writes next to the assembly (e.g. `Generator.cs:60` `library/video/renders`),
  so the platform layer's resource/data roots (L2) are a hard prerequisite.
- Signing: ad-hoc `codesign -s -` is required for arm64 and is enough for local/community testing (users get a Gatekeeper right-click→Open
  prompt). Developer ID signing + notarisation needs a paid Apple Developer account, so record it as a later optional step; it isn't a blocker.
- Build: a `build-macos-installer.sh` (`dotnet publish -r osx-arm64 --self-contained -p:NvgPlatform=DesktopGL`, assemble bundle, `hdiutil create`).
  No upload step.
- Verify: a fresh macOS user account (or a clean data dir) mounts the DMG, drags it to Applications, launches, imports, previews and renders, and settings
  survive a restart. Do this with computer-use.
- The Windows installer is outside this file. Keep it aligned with Sol's plan.

## Spikes (do these first; they gate everything after them)
- **S1 (hard, one worker):** install .NET 8 SDK, build the DesktopGL config with the platform shims stubbed, then launch on arm64:
  window opens, content loads, Sdcb.FFmpeg resolves the ffmpeg@7 dylibs and plays one video, Steamworks OSX build either inits
  or fails gracefully, DiscordRPC doesn't throw. Output: pass/fail per item, recorded in `AGENT_HANDOFF.md`.
- **S2 (easy):** build borsboom/vocoder natively on macOS, then run it on a sample wav.
If S1 fails on FFmpeg or MonoGame natives, stop and re-plan before any shim work.

## GUI verification
- The launch, window, video playback, file dialog, clipboard and TTS checks can be driven from this session with **computer-use**
  (screenshot + click on the owner's desktop). The owner doesn't need to be the tester. atmux background processes can't take
  window focus, so GUI checks happen from a session with computer-use, not from inside a worker's test run.
- Non-GUI checks (build both configs, resolver unit check, tool path resolution, vocoder CLI) run headless in workers.
- No test project exists in the repo. Acceptance is builds + a headless smoke where possible + a computer-use launch/generate run.

## Wine fallback on macOS
- [verified] There's no Proton on macOS. Steam for Mac can't Steam-Play this Windows app.
- Options: Gcenx `wine-stable`/`wine-crossover` casks (x86_64 under Rosetta 2) or CrossOver. Whisky is archived.
- [unverified] WindowsDX = D3D11 → wined3d→GL or DXVK→MoltenVK on mac; WinForms + .NET 8 Desktop runtime in the prefix. Expect it to be fragile.
- Default: a best-effort `wine-mac.sh` (mirrors `proton.sh`; prefix in `~/Library/Application Support/NVG/wineprefix`), documented as
  unsupported fallback. Low priority, done after the native path.

## Recommended path
Native DesktopGL is the shared target for macOS and Linux. Wine is a fallback only. All of this is **provisional until S1 passes**.
1. S1 + S2 spikes.
2. Platform abstraction layer (`src/backend/platform/`: ToolPaths, Clipboard, FileDialog, Tts, ShellOpen, ProcessTree) with the Windows
   implementation being today's code moved behind it. This is pure refactor.
3. csproj DesktopGL config, FFmpeg resolver, Steamworks per-platform vendoring + optional init.
4. mac/Linux shim implementations, backslash sweep, rave.lua.
5. Docs/scripts: `MACOS_BUILD_INSTRUCTIONS.md`, `start-mac.sh`, `wine-mac.sh`.
6. macOS installer (see Installer scope). Only after the native gate passes.

## Reconciliation with PLAN_LINUX.md (Opus review, 2026-11-01)
- S1 (macOS) and L1 (Linux) are **one shared native trial**. It uses the same build switch, platform layer and dependency audit, and is run first on this
  Mac (the currently reachable host) and then on the owner-designated Linux host `ssh 750` once booted and reachable. The initial read-only SSH check timed out; Linux OS/tool verification remains pending. Gate criteria are the same on both: launch, fonts/locales, import,
  thumbnails (Bitmap replacement), preview with audio via the vendored Extended.VideoPlayback, a short stock-effect MP4 render, and
  the Windows default build still building.
- Content: `content/Content.mgcb:6` hardcodes `/platform:Windows`. The native config must build content for DesktopGL (MonoGamePlatform
  override or a separate mgcb), into a separate output dir.
- macOS-specific notes: the default APFS is case-insensitive, so Linux will surface casing bugs that the Mac won't. Steam/FFmpeg dylibs taken from
  downloads may need `xattr -d com.apple.quarantine` for local dev. Code signing/notarisation is out of scope for this round.
- No owner decisions are needed for this planning round. DesktopGL config and Steam optional-init defaults are routine. Nothing gets deployed or published.

## Final review status
Opus approved the combined native-first direction and fallback on 2026-11-01. See AGENT_HANDOFF.md for reconciled findings and owner clarifications. Temporary spike shims are experimental only and must not be merged as delivered features. Linux and macOS runtime support remain unverified. Public distribution is a separate decision after license/asset/dependency review; no rights to the current Steam listing or branding are assumed.
