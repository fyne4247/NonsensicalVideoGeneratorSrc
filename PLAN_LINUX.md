# Linux plan — 2026-11-01

## Goal and recommendation
Run NVG on Linux with its existing interface and video generation intact. Prefer a shared native DesktopGL port with macOS if a bounded feasibility trial succeeds; use a robust Proton launcher if native playback or vendored dependencies require a substantial rewrite. This document is a plan, not an implementation or a claim of Linux compatibility.

Start with Linux x86_64 on Ubuntu 24.04 as the reference environment, because the repository's existing instructions name it. Verify on a real graphical Linux session; a build on macOS or a headless Linux container does not prove graphics/audio functionality. Linux ARM64, Flatpak packaging and Steam Deck certification are outside the first delivery.

## Observed baseline
- Baseline `58ecdd1b93d2035a1f9df60119cd82a7544692ba`; clean team checkout before planning.
- `NonsensicalVideoGenerator.csproj` targets net8.0-windows and WindowsDX 3.8.2.1105, with WinForms, WindowsDesktop, System.Speech and WindowsAPICodePack. Copy targets hardcode Windows output paths and Windows DLLs.
- `LINUX_BUILD_INSTRUCTIONS.md` is a Windows-under-GE-Proton development procedure, not a native build. Its statement that Ubuntu 24.04 was tested is upstream documentation, not verification in this session.
- `proton.sh` assumes one Steam installation path, GE-Proton10-34, a checkout at ~/NVGDev/NVGMonoGame and the game's existing prefix. It forwards only two command tokens after the directory, leaves paths unquoted and uses ln -sf against that existing prefix.
- `install-dotnet-sdk-in-proton.sh` passes a destination path as a second wget URL instead of using an output option, does not create .temp, and does not validate downloads. Current scripts must not be treated as reliable installers.
- `public` submodule is uninitialized; locale files need that checkout at recorded commit 38995cb4a419b7e511f1f1d462e01fb5b0dcbf5c.
- No automated test project found. This host is macOS; dotnet and Wine were not on PATH during initial inspection. No build or run was attempted.

## Proposed tasks and acceptance

### L1 — Shared native feasibility trial (first; coordinate with macOS S1)
Files: NonsensicalVideoGenerator.csproj, content/Content.mgcb or a separate DesktopGL content definition, src/backend/platform/* (new), and the minimal affected call sites listed in L2/L3. Preserve the Windows default and use a separate native build switch/output directory.

Use the existing MonoGame version first, switching the native build to net8.0 + DesktopGL. Keep restore/intermediate outputs separate too, so Windows and native dependency graphs cannot contaminate each other. Rebuild content for DesktopGL; do not reuse Windows XNB content. Initialize locales from the pinned submodule. Audit the actual framework references, CPU architecture and native imports of MonoGame.Extended2, Extended.VideoPlayback, MoonSharp, Sdcb.FFmpeg and Steamworks.NET before deciding they are portable. A PE .NET assembly alone does not prove Windows-only or AnyCPU compatibility.

**Done:** on real Linux, native app opens its normal interface, loads fonts/locales, imports a local audio/video fixture, shows thumbnails, plays a preview with audio, and generates a short playable MP4 using a stock effect. Record exact dependency versions and Windows build regression results. Missing previews are an unresolved limitation, not a successful full port.

**Decision gate:** if native succeeds with localized changes, continue L2–L4; if it needs replacement of the renderer/video stack or major vendored-library rewrites, record the evidence and switch to L5 rather than silently expanding scope. The trial may use the existing external-player option as a diagnostic, but a permanent reduced-feature delivery needs explicit owner agreement. No broad .NET/MonoGame upgrades during this trial unless a specific blocker requires one.

### L2 — Shared OS boundaries and filesystem behavior
Files: src/backend/platform/*; src/backend/{UserInterface,Accessibility,Program,Generator}.cs; src/interactables/TextInput.cs; src/data/{PluginHandler,LibraryData,SaveData,FramePlayer}.cs; src/pages/{LibraryPage,PluginsPage}.cs; other concrete call sites discovered in the scoped path audit; baseplugins/user/stock/rave.lua.

- Replace WinForms dialog/window/clipboard dependencies on the native target. Prefer one shared clipboard implementation through SDL, validating native library naming and freeing SDL-owned memory. For Linux dialogs use an available desktop chooser (zenity/kdialog initially); cancellation and a missing helper must return clearly, without hanging. Preserve multi-selection and filters. Use DesktopGL file-drop events where supported.
- Route folder/URL/media opening through a helper using xdg-open on Linux and structured process arguments. Preserve Windows behavior.
- Replace eager System.Speech construction with an OS speech service (Linux Speech Dispatcher or espeak-ng) that supports cancel/queued speech and remains usable if no voice service is installed. Preserve keyboard accessibility; make missing narration visible.
- Replace PowerShell-based child termination with tracked child processes and process-tree termination. Cancel only this job's owned processes, never every ffmpeg process on the computer.
- Centralize application/resource roots, tools and writable state. Use Path.Combine for filesystem paths; preserve intentional FFmpeg/Lua escaping. Audit plugin-visible paths, downloads, job directories, frame extraction, save files and case-sensitive resource names. Do not rewrite users' plugins or migrate existing data silently.
- Set frei0r search paths in the app/child-process environment, preserving caller configuration where valid; do not persist user-wide environment settings.
- Remove native-target System.Drawing.Bitmap thumbnail decoding at LibraryPage.cs:504. Prefer existing MonoGame decoding or FFmpeg RGBA output, keeping graphics work on the proper thread and preserving the black-to-translucent conversion. System.Drawing.Color primitives are a separate case, not a blanket removal target.

**Done:** chooser cancel/multi-select, clipboard copy/paste, paths with spaces/non-ASCII, file drop, folder opening, fullscreen and cancellation work; cancellation leaves unrelated processes alive. Saved settings and imported files survive restart. Focused tests cover path/tool resolution, missing helpers, argv handling and owned-process cleanup, with desktop behavior verified separately.

### L3 — Native media, plugin and integration dependencies
Files: src/backend/platform/*; src/data/{PluginHandler,FramePlayer,SteamManager}.cs; src/backend/{Global,Program}.cs; NonsensicalVideoGenerator.csproj; packages references; relevant stock plugins.

- Use native ffmpeg/ffprobe, ImageMagick 7, yt-dlp and Deno; make diagnostics check actual executable availability AND required FFmpeg codecs/filters. Existing useSystem flags do not fix every hardcoded path or the bundled-only Installed() checks.
- Validate Sdcb.FFmpeg's native ABI against Linux shared libraries. The bundled DLL names indicate FFmpeg 7-era libraries; Ubuntu's stock FFmpeg may not match. Do not symlink incompatible SONAMEs or install system-wide library aliases. If needed provide app-local matching libraries with recorded provenance/license, or rebuild the binding and video extension coherently. A DllImport resolver fixes names/search paths, not ABI incompatibility.
- Validate the Extended video player independently: MonoGame DesktopGL has no built-in VideoPlayer. Do not assume this repository's vendored extension works because graphics compile.
- Check required frei0r effects and run stock plugin samples, including rave's frame paths. Investigate building borsboom/vocoder from source; if infeasible, report the exact unavailable plugin rather than substituting a different effect silently.
- Obtain matching cross-platform Steamworks managed/native release artifacts from upstream with provenance; test with Steam present and absent. Preserve release ownership/restart behavior. A deliberate local development build may opt out of integration; that must not become a general bypass for Steam releases. Test Workshop only if authorized account access is available; never upload during validation.
- Verify Discord RPC absence does not block startup. Keep optional service failures separate from rendering failures.

**Done:** native preview/audio and representative stock effects produce valid results, unsupported optional tools show useful messages, native libraries load without Wine, and dependency versions/licenses are recorded. Windows default still builds and retains its release integration behavior.

### L4 — Linux launch, packaging and instructions
Files: start-linux.sh (new), LINUX_BUILD_INSTRUCTIONS.md, README.md, native output/copy targets and optional local validation scripts.

Provide a launcher that works from any directory, checks prerequisites and forwards all arguments. Publish a self-contained linux-x64 directory where practical, with assets and native dependencies; users should not need the SDK merely to launch. Distinguish build prerequisites from runtime prerequisites. Prefer a user-writable install/data location initially; do not modify existing save/library layouts without a migration plan.

**Done:** a clean reference Linux account can launch the packaged app from a path with spaces, import/generate/play, cancel/retry, restart and retain settings. Record X11/Wayland session coverage; do not claim both unless exercised. Documentation clearly separates native and Proton paths, and contains no uploading/release commands in setup flows.

### L5 — Proton/Wine fallback (after failed native gate, or a small separately justified reliability task)
Files: proton.sh, start.sh, install-dotnet-sdk-in-proton.sh, a dedicated start-proton.sh if useful, LINUX_BUILD_INSTRUCTIONS.md.

Retain the Windows app/tools within Wine initially; native Linux executables cannot simply replace .exe paths from a Windows process. Prefer Proton for Steam-backed use; make the tested runtime selectable instead of permanently pinning assumptions about the latest version. Detect/accept Steam library, Proton and prefix paths, quote every path, forward "$@", and report missing prerequisites with actionable messages. Do not replace or reuse the owner's existing prefix without deliberate selection; use an isolated development prefix by default where Steam behavior permits and verify that case.

Use a published self-contained win-x64 build where viable; otherwise install the Windows Desktop runtime for running, not the entire SDK. Only install an SDK inside Wine when source/content compilation actually requires it. Separate build and launch. Verify downloads and stop on failure. Do not auto-restart Steam, change system packages or create libjpeg ABI aliases. Never invoke publish.sh: it uploads to Steam.

**Done:** on real Linux, clean selected prefix starts the app, dialogs/preview/audio and a generated MP4 work, all arguments survive spaces, repeated launches work, cancellation leaves no owned jobs running, and missing dependencies fail clearly. If only the upstream procedure was examined, report this as planned, not tested.

## Dependencies and merge boundaries
L1 determines native feasibility. L2 and L3 share large files (Generator, PluginHandler, LibraryPage); serialize their edits or explicitly assign one owner per file. L4 follows the verified native result. L5 is the alternative fallback and must not be a prerequisite for claiming native runtime. macOS and Linux should share the platform interfaces and dependency work rather than create divergent ports. Any future implementation requires reviewed task branches and the applicable workflow gates; this planning assignment does not authorize publishing.

## Risks, open evidence, and defaults
- Linux hardware/desktop access is not established. Choose an available authorized Linux test host before claiming runtime success; do not infer permission to modify a remembered remote computer.
- Vendored extension source/rebuild provenance and FFmpeg library versions are the largest uncertainty; native effort remains provisional.
- Existing third-party Lua plugins can contain arbitrary Windows commands; scope first native compatibility claims to audited stock plugins and describe any limitations.
- No credentials, Steam account purchases, uploads, paid wrappers, system package installs, restarts, pushes, or destructive data/prefix operations are part of this planning task.
- No unresolved product choice blocks writing this plan. Routine library/platform choices belong to the agents. Ask the owner only if the evidence requires dropping an existing feature, broadening into a major rewrite, or spending money.

## Sources checked
- [MonoGame supported platforms](https://docs.monogame.net/articles/getting_started/platforms.html): DesktopGL supports Linux/macOS/Windows; SDL/OpenGL/OpenAL; no built-in VideoPlayer.
- [Microsoft System.Drawing.Common compatibility](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing): Unix opt-in removed starting .NET 7.
- [Valve Proton](https://github.com/ValveSoftware/Proton): Wine-based Steam compatibility path for Windows games on Linux.

## NEXT ACTION
Review this plan with Opus alongside PLAN_MACOS.md. Reconcile a single shared feasibility trial, record review findings in AGENT_HANDOFF.md, then present the native-first recommendation and fallback to the owner. Do not start implementation during this planning assignment.
