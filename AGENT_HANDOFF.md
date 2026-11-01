# Platform planning handoff — 2026-11-01

## Objective and acceptance criteria
Owner asks to get NVG running natively on macOS and/or Linux where practical, with Wine as fallback, and explicitly assigns macOS planning to Opus and Linux planning to Sol. Current deliverable is two evidence-based plans and a consolidated recommendation; implementation is not started. Owner subsequently approves native installers alongside Windows installers and designates `ssh 750` as the Linux test host (booting). Owner does not control the existing Steam listing; recommendation is local/community installers first, without Steam publishing.

Plans must identify Windows dependencies, native feasibility risks, file/task boundaries, dependency order, verification requirements and fallback criteria. No runtime support may be claimed without actual verification.

## Repository state
- Team branch: atmux-NonsensicalVideoGeneratorSrc-team-flow-1101-0201.
- Baseline/integration before planning: 58ecdd1b93d2035a1f9df60119cd82a7544692ba.
- Team worktree: /Users/fish4247/.atmux/teams/NonsensicalVideoGeneratorSrc/flow-1101-0201/worktree.
- Initial tracked/untracked working tree clean. public submodule is uninitialized at recorded commit 38995cb4a419b7e511f1f1d462e01fb5b0dcbf5c.
- Target main is checked out at /Users/fish4247/Vibecoding/NonsensicalVideoGeneratorSrc. No merge/push/deployment performed. Planning records stay on the team branch pending review; do not merge implementation that does not exist.

## Assignments and current state
- Sol: PLAN_LINUX.md, this handoff. Linux repository audit and plan complete; combined direction approved by Opus, findings incorporated.
- Opus (flow-1101-0201-opus): PLAN_MACOS.md in the shared team worktree/branch. macOS investigation and combined review complete. PLAN_MACOS.md finalized and handed to Sol for checkpoint; Opus stopped editing. No separate worker branches or worktrees created. Linux/handoff checkpoint: 71a70e1 (flow: plan). macOS plan and final reconciliations are included in the final planning checkpoint.
- Sol is the single handoff writer. Opus edits only PLAN_MACOS.md.

## Findings and recommendation
One shared native DesktopGL build is the candidate for both platforms, with WindowsDX retained as default. Feasibility remains provisional: vendored video playback/native FFmpeg ABI, managed assembly architecture and native Steamworks artifacts must be tested. Additional Windows coupling includes dialogs, clipboard, speech, shell/process control, paths, Lua frame paths and System.Drawing.Bitmap thumbnail decoding.

Linux already has an upstream documented Proton route, but scripts hardcode install paths/prefix and contain quoting/download defects. It is the fallback to harden if a bounded native launch/preview/render trial fails without a major rewrite. macOS wrapper feasibility is weaker and must be separately tested.

Do not treat upstream Ubuntu testing claims or static source inspection as live compatibility. Do not silently drop preview, narration or stock effects. Preserve release ownership checks, existing saves/plugins/media, Windows behavior and licensing. No paid software, account changes, system setup, Steam uploads, pushes or destructive cleanup authorized by this planning task.

## Checks actually performed
- Read project/build/content configuration, Linux scripts, platform and media call sites, selected stock Lua plugins, submodule status and worktree refs.
- Verified current official MonoGame platform documentation and Microsoft's System.Drawing.Common Unix restriction; sources are in PLAN_LINUX.md.
- No dotnet/Wine on PATH during initial inspection. No builds, tests, installs, GUI launches or Linux execution performed. No automated test project found. Test-suite execution is not applicable to this documentation-only planning checkpoint; future code needs both focused tests and real GUI/render validation.
- Opus has its own platform evidence in PLAN_MACOS.md; no claim that Sol independently reran it.

## Review and approvals
- Opus final consolidated review APPROVES native-first direction with fallback. This is planning approval, not verified implementation.
- Findings resolved in plan: make S1/L1 one experimental trial first on Mac then Linux; separate DesktopGL content and intermediate outputs; replace System.Drawing.Bitmap; validate vendored playback; serialize shared large-file edits.
- Ubuntu 24.04 FFmpeg 6.1 is incompatible with the bundled FFmpeg 7 binding; budget app-local 7.1 libraries built from source, never ABI aliases.
- Owner clarification incorporated: build local installers; community build disables Steam integration by build flag, original Steam release keeps ownership checks. No claim about rights to branding/assets/public distribution. Steam publishing requires coordination with current listing controller and separate authorization.
- First read-only SSH check to owner-designated `750` timed out during boot. No remote command executed, no remote mutation. OS/tool/desktop baseline pending. Native compatibility is not verified on either OS.

## NEXT ACTION
When implementation begins, verify `ssh 750` connectivity after boot, inspect OS/architecture/tool/desktop baseline, then execute the shared native S1/L1 experiment with separate outputs and no publication. Record launch, thumbnail, audio preview and stock-effect MP4 evidence on both systems before choosing full native implementation or wrapper fallback. Build installers only after verified runtime and data-root work. Planning is complete, committed on team branch; no main merge because the broader port remains unimplemented. Nothing pushed or deployed.

## Resume check — 2026-11-01
- Owner requests reopening Opus and confirms `ssh 750` should be online.
- Read-only SSH now succeeds: Linux Mint 22.3 (Zena), Ubuntu noble base, x86_64, kernel 7.0.0-42-generic. `/usr/bin/ffmpeg` and `/usr/bin/zenity` are on PATH; dotnet, wine and kdialog were not found on the noninteractive SSH PATH. DISPLAY and WAYLAND_DISPLAY are unset in that SSH session; this does not prove no local desktop session exists.
- No remote files changed, no packages installed, no build/run attempted.
- Opus was closed at the end of the planning assignment. Sol's role explicitly prohibits creating a replacement Opus; owner must reopen Fish Flow and press r to restore it. No agent was spawned.
- NEXT ACTION: Restore Opus through Fish Flow, then send the saved plans, this Linux baseline and the owner request. Continue at the shared feasibility-trial gate rather than redoing planning.

## Opus restored — 2026-11-01
- Received runtime notice that flow-1101-0201-opus has been restarted. Sent the saved plans, reviewed checkpoints, owner installer/distribution constraints and successful Linux SSH baseline to that existing agent. Delivery accepted by atmux.
- Requested a concrete next feasibility-trial assignment/staffing recommendation, without redoing planning. Reply pending; no implementation assigned by Sol.
- NEXT ACTION: Handle Opus's substantive reply at the shared native trial gate. Keep Opus available as requested by the owner.

## Triage — shared native feasibility trial (Opus, 2026-11-01)
Sol stays the handoff writer. This section only holds Opus's triage and worker assignments.
Main checkout `/Users/fish4247/Vibecoding/NonsensicalVideoGeneratorSrc` was clean (`git status --short` empty) when the trial started. Each worker report gets checked against this.

**Scope of this round:** only S1/L1 (gate) and S2 (vocoder). L2–L5, installers and docs wait until the gate result is in. The trial branch is **experimental**: temporary stubs/shims must not be merged into the team branch as delivered features. Sol decides what, if anything, gets kept after the review.

**Staffing:** mixed, 1 hard + 1 easy.
- S1/L1 is **hard and widespread**: it touches the build system, the native ABI (FFmpeg 7 dylib/.so resolution), vendored video playback, content pipeline and several call sites, on two OSes. If it guesses wrong, everything after it is planned on bad data. One worker does it so the Mac and Linux runs share one build switch and one platform layer.
- S2 is **easy and small**: building a plain C program from source plus a sample run. It shares no files with S1 and runs in parallel.

### flow-1101-0201-hard-1 — S1/L1 shared native trial (Grok 4.7, high)
Owns (on its own branch only):
- `NonsensicalVideoGenerator.csproj` (add `NvgPlatform=DesktopGL` switch; the default build must stay exactly WindowsDX)
- `content/Content.mgcb` read-only; new `content/Content.DesktopGL.mgcb` (or a MonoGamePlatform override) with a separate output dir
- `src/backend/platform/*` (new), `src/backend/{Program,Generator,UserInterface,Accessibility,Global}.cs`, `src/data/{SteamManager,PluginHandler,LibraryData,FramePlayer}.cs`, `src/interactables/TextInput.cs`, `src/pages/{LibraryPage,PluginsPage}.cs`: only the minimum each needs to compile and run under DesktopGL (`#if` guards / stubs)
- `baseplugins/user/stock/rave.lua` (path separators only)
- `TRIAL_S1_L1.md` (new): the trial report
Must not touch: `AGENT_HANDOFF.md`, `PLAN_*.md`, `publish.*`, `steamcmd*.vdf`, `proton.sh`, `start.sh`, `install-dotnet-sdk-in-proton.sh`, `LINUX_BUILD_INSTRUCTIONS.md`, `README.md`, `packages/**` (it may add new vendored files under `packages/Steamworks.NET/OSX-Linux-x64/` only if Steam init is tested; otherwise leave Steam off with `NVG_STEAM` unset), anything in the main checkout.
Environment rules:
- .NET 8 SDK user-local only via Microsoft's `dotnet-install.sh` into `~/.dotnet-nvg` (Mac and `ssh 750`). No sudo, no apt, no Homebrew installs/upgrades/uninstalls. Use the already-installed keg-only `ffmpeg@7` on the Mac.
- `ssh 750`: work only inside `~/nvg-trial/`. FFmpeg 7.1 shared libs get built from source into `~/nvg-trial/ffmpeg7/` (`--disable-x86asm` if nasm is missing). If a build needs a system package, stop and report the package name. Never symlink or alias system libraries.
- Linux GUI: check for a logged-in graphical session read-only first (`loginctl list-sessions`, `loginctl show-session <id> -p Type -p Display`). If one exists, launch with that `DISPLAY`/`XAUTHORITY` or `WAYLAND_DISPLAY`. If none exists, stop and report so the owner can log in. Don't start a display server.
- No pushes, publish/upload, Steam account actions, paid tools, or deleting anything outside the worker's own trial dirs.
Steps:
1. Mac (arm64): build the default config first and record whether it still builds on macOS (expected: WindowsDX needs Windows, so record the exact error; a no-op is fine). Then add the DesktopGL switch, build content for DesktopGL, register a `NativeLibrary.SetDllImportResolver` for Sdcb.FFmpeg + Extended.VideoPlayback (mac `lib<name>.<major>.dylib`, Linux `lib<name>.so.<major>`), replace the `System.Drawing.Bitmap` thumbnail decode at `src/pages/LibraryPage.cs:504` on the DesktopGL path, init the `public` submodule in its own worktree for locales.
2. Dependency audit: for MonoGame.Extended2, Extended.VideoPlayback, MoonSharp, Sdcb.FFmpeg, Steamworks.NET, DiscordRichPresence record target framework, CPU arch (corflags-equivalent / PE header + IL-only flag), and P/Invoke native names.
3. Run gate on Mac, then repeat on `ssh 750` (publish `linux-x64` self-contained, copy it to `~/nvg-trial/`).
Acceptance (per OS, pass/fail each, with evidence: log excerpts, file paths, `ffprobe` output, screenshots via `screencapture` / `import -window root` where possible):
- app window opens and stays up ≥30 s, content + fonts + locales load (stdout/stderr log clean of exceptions)
- import one local audio/video fixture; thumbnail generated
- preview plays with audio through vendored Extended.VideoPlayback (if this needs window focus/interaction, mark "needs GUI check" and say exactly what to click)
- one short stock-effect render produces an MP4 that `ffprobe` reads with video+audio streams
- DiscordRPC and Steam-off path don't throw at startup
Decision-gate input: for anything that fails, say whether the fix is localized or needs a renderer/video-stack rewrite (PLAN_LINUX.md L1 decision gate).
Tests: no test project exists. Headless checks: `dotnet build -p:NvgPlatform=DesktopGL` (Mac and Linux), resolver smoke (load `avcodec-61` through the resolver in a tiny console check or at startup with a logged result), `ffprobe` on the render. GUI launches go **last** and run one at a time (no other worker opens windows). Background processes can't take focus on macOS: if a check fails waiting for focus, don't retry; list it under "needs owner/computer-use GUI check".
Report: commit on its branch, then message Opus with the branch, final commit, the commands it ran and their pass/fail summary.

### flow-1101-0201-easy-1 — S2 native vocoder build (Mistral Vibe)
Owns: `tools/vocoder/` (new): `build-vocoder.sh` (fetch borsboom/vocoder at a pinned commit into a scratch dir, build with the system C compiler, output to a given dir) and `tools/vocoder/NOTES.md` (commit hash, compiler, warnings, sample run result, licence of upstream).
Must not touch: anything outside `tools/vocoder/`; the main checkout; any system packages (no sudo/apt/brew).
Steps: build and run on the Mac, then on `ssh 750` inside `~/nvg-vocoder/` only. Sample run: generate two short WAVs with ffmpeg (`sine` + `anoisesrc`), run the vocoder on them, check the output with `ffprobe`. Check how `src/backend/Generator.cs` / `src/data/PluginHandler.cs` call `vocoder.exe` (read-only) and write the exact arguments it passes into NOTES.md, then run the native build with those same arguments.
Acceptance: script builds on both hosts from a clean scratch dir; output WAV is valid per `ffprobe`; NOTES.md records commands run and pass/fail. If no C compiler on `750`, record that and stop for that host.
Dependencies: none. Runs in parallel with hard-1.

### Overlap
No shared files between the two workers. No sequencing needed. Remote dirs are separate (`~/nvg-trial/` vs `~/nvg-vocoder/`).

## S2 vocoder result — Opus review report, 2026-11-01
- Owner/worker: easy-1; branch `atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-easy-1`; approved final commit `346a6f9`. Opus reports worker closed, branch/worktree preserved. Worktree path was not included in report; verify via git worktree list before eventual integration/cleanup.
- Changes: only `tools/vocoder/build-vocoder.sh` and `tools/vocoder/NOTES.md`. Upstream borsboom/vocoder pinned to `3faed27b`, with notice-preserving permissive license per review.
- Verification reported by Opus: build and run on Mac (clang 21) and 750 (gcc 13.3), using the stock plugin argument shape `-b 1024 mod carrier out`; ffprobe validated 2.02-second pcm_s16le WAV output on both. Sol has not duplicated these tests.
- Review state: S2 APPROVED by Opus, not yet merged by Sol. This proves native vocoder feasibility, not application integration. Native executable path wiring remains hard-1 scope.
- Follow-up: NOTES.md incorrectly describes 750 as Ubuntu 24.04; correct to Linux Mint 22.3 (noble base) in reviewed integration. Native vocoder prompts on stdin for missing arguments; application must close stdin to avoid hanging on bad input. Sent both findings to Opus.
- S1/L1 remains in progress. NEXT ACTION: Await Opus's consolidated review; do not repeat completed S2 tests or inspect workers merely for status.

## Owner-requested status check — 2026-11-01
- Owner asks to continue and whether hard-1 is still working. One direct atmux status/capture check reports pane alive and shows active Thinking output around Sdcb.FFmpeg RootPath/resolver handling; no completion report received. This is observed session state, not proof of a passing build or runtime.
- Sent owner continuation instruction to Opus. Do not restart or duplicate the worker. NEXT ACTION: await Opus's consolidated review or an actionable blocker.

## Review — S1/L1 + S2 (Opus, 2026-11-01)
**Gate result: PASS for feasibility, with gaps listed below.** This is a trial result, not a finished port or installer. All shims on hard-1 are experimental.

### Worker branches
| Worker | Branch | Final commit | Status |
|---|---|---|---|
| easy-1 (S2 vocoder) | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-easy-1 | 346a6f9 | APPROVE. Closed. Fix at integration: NOTES.md says 750 is "Ubuntu 24.04"; it is Linux Mint 22.3 (noble base). |
| hard-1 (S1/L1 trial) | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-1 | e8f398c | APPROVE as trial evidence. Do not merge as delivered features. Closed. |

### What hard-1 showed (details in TRIAL_S1_L1.md on its branch)
- Mac arm64 and Linux x64 (750, X11 session :0 already logged in): window up ≥30 s with fonts and locales, fixture import + thumbnail (`BmpThumbnail`, no System.Drawing), Extended.VideoPlayback reached `Playing`, `speed.lua` rendered an MP4 that ffprobe reads as h264+aac, Steam off and DiscordRPC both clean at startup.
- No renderer or video-stack rewrite needed. Every fix was localized:
  - The arm64 MGCB can't load FreeImage/freetype, so content was built with an x64 SDK (`~/.dotnet-nvg-x64`) under Rosetta.
  - Steamworks.NET, Extended2 and VideoPlayback are IL-only but marked AMD64. `retarget-ilonly.py` patches only the build-output copies, never `packages/`.
  - Sdcb.FFmpeg already has its own resolver. FFmpeg 7 libs get staged next to the app (`~/nvg-trial/ffmpeg7` on 750, built from source; Homebrew ffmpeg@7 on the Mac).
- Vocoder: DesktopGL resolves `bin/vocoder` and closes stdin. A bad flag exits in 0.1 s. **With no arguments it still spins** (the vocoder treats EOF as an empty answer). The stock plugin always passes arguments. A real fix is a per-command timeout or a small upstream patch, recorded under L3.

### Trial merge (`flow-test/flow-1101-0201` = team 8e1af0f + easy-1 + hard-1)
- Conflicts: none.
- `dotnet build -p:NvgPlatform=DesktopGL -p:NvgSkipMgcb=true` (separate obj): 0 errors, 4 warnings (obsolete WebClient, pre-existing).
- `tools/vocoder/build-vocoder.sh` from a clean scratch dir: builds.
- No test project exists, so there's no other suite to run.

### Not verified, or might not work
1. **Windows default build regression is NOT verified.** WindowsDX can't build on macOS (MGCB natives are x86-only). hard-1 changed shared files under `#if`, and `LibraryPage.cs` now calls `System.Windows.Clipboard` explicitly. The plan's gate requires the Windows build still building. Candidate: the owner's Windows 11 VM on 750 (`ssh 750 'ssh win11 ...'`), which needs a VM start (it stops ollama with sudo, 6 GB RAM). That needs owner go-ahead.
2. **Audio during preview was not heard.** The player reached `Playing`, but nobody listened. The UI path (Library → Add → picker → click holder) was driven only by the `-trial` code hook, not by clicks. This needs a human/computer-use check on both OSes.
3. The csproj DesktopGL block sets `DefineConstants=DESKTOPGL`, which overwrites DEBUG/TRACE; real work should append to them. `NVG_STEAM` + DesktopGL can't compile yet (Steamworks reference excluded), which is fine for the community build.
4. Untracked build dirs (`bin/`, `obj-desktopgl*/`) need `.gitignore` entries. The linux-x64 publish needed manual copies of content/plugins/locales and a `libavcodec.so.61.so` alias name next to the app (an app-local file, not a system alias). Packaging (L4) has to automate this.
5. The Linux render used system ffmpeg 6.1 CLI. In-process playback used the app-local 7.1 libs. Mixing the two works but needs a decision for installers.
6. The `-trial` and `-vocoder-stdin` hooks in Program.cs/TrialGate.cs are trial-only and should not ship.

### Main checkout
`git -C /Users/fish4247/Vibecoding/NonsensicalVideoGeneratorSrc status --short` empty after both reports. No stray writes.

## Sol acceptance decision and owner VM approval — 2026-11-01
- Accepted Opus's report as evidence that native porting is practical on both platforms; the full L1 acceptance gate is still incomplete because Windows regression, audible preview and normal UI click-through are unverified. No claim of finished native support or installers.
- Owner explicitly approved starting the Windows 11 VM on 750 for the Windows build check, informed that startup stops Ollama to free memory and uses sudo. Sent authorization to Opus; preserve existing VM state and record final VM/Ollama state. No unrelated service changes authorized.
- Asked Opus to coordinate Windows verification and bounded L2–L4 production work from the trial: remove experimental hooks/stubs, append DEBUG/TRACE constants, fix vocoder no-argument spin with a focused check, automate content/dependency staging, establish writable data roots, then installers. Reviewed integration only; no main merge until acceptance and checks pass.
- Shared trial branches remain preserved and unmerged into the team as delivered features. No full-suite evidence exists beyond the builds reported by Opus; absence of a test project does not substitute for the remaining real UI/Windows validation.
- NEXT ACTION: Opus coordinates the authorized Windows check and next bounded assignments; Sol awaits an actionable blocker or consolidated review and maintains this handoff. Nothing pushed/published/deployed.

## Coordination delivery blocker — 2026-11-01
- Two sends of the authorized continuation/VM instructions to Opus were accepted into atmux's queue, then each produced a delivery-failed notification (pane %9, three internal attempts). Stopped retrying.
- One failure-driven status/capture check showed Opus pane alive but idle at its previous review response. Receipt and task start are NOT confirmed; do not claim Windows verification has begun.
- Owner VM approval remains recorded above and valid. NEXT ACTION: Restore message delivery to existing Opus session (or open its pane and provide the instruction to read this handoff), then coordinate the authorized Windows check. Do not create a replacement agent or repeat unbounded message retries.

## Windows regression result — Opus report, 2026-11-01
- Delivery blocker resolved: Opus received authorization and completed the Windows build check. Opus additionally reports owner approval for installing the Microsoft VC++ runtime in the VM.
- VM on 750 started headless. Ollama was already inactive before startup, so no sudo was used. VM is now shut down (no qemu process per Opus); Ollama remains inactive, matching its prior state.
- VM changes: user-local .NET SDK 8.0.427 at `C:\Users\Quickemu\.dotnet-nvg`; Microsoft VC++ 2015–2022 x64 redistributable installed (signature Valid/Microsoft Corporation, installer exit 0). Checkouts: `C:\Users\Quickemu\nvgtest-base` at 8e1af0f and `C:\Users\Quickemu\nvgtest-trial` at e8f398c.
- Default WindowsDX build: before runtime install both baseline and trial failed identically in MGCB (155 TextureImporter errors/missing vcruntime140), while `-t:Compile` passed for both. After runtime install full builds passed for both: exit 0, zero errors, four warnings each. Windows build regression PASS per independent Opus report; Sol did not rerun it.
- Window launch over noninteractive SSH failed identically for baseline/trial at GraphicsDeviceManager.CreateDevice (no D3D adapter); Windows interactive launch remains NOT VERIFIED. Native normal UI click-through and audible preview checks likewise remain pending.
- Opus reports creating and deleting temporary local labels nvgwin-base/nvgwin-trial pointing at existing commits while preparing bundles; no work lost. This violated the no-branch-deletion workflow rule. Reminded Opus to preserve all labels in future and use existing refs/hashes instead. No target history was reported changed.
- Main checkout remains clean per Opus. No merge to main or publication. NEXT ACTION: Opus triages bounded L2–L4 production assignments, preserving successful evidence and pending GUI/audio acceptance; Sol maintains the handoff.

## Windows check (Opus, 2026-11-01, owner-approved VM)
- Default WindowsDX config, Win11 VM on 750, .NET SDK 8.0.427 user-local in the VM, VC++ 2015-2022 x64 runtime installed in the VM (owner approved).
- Full `dotnet build`: baseline 8e1af0f → 0 errors / 4 warnings; trial e8f398c → 0 errors / 4 warnings. **Windows build regression: PASS.**
- Launch over SSH: both crash identically at `GraphicsDeviceManager.CreateDevice` (no D3D adapter in the non-interactive session). **Windows windowed launch: NOT verified.**
- VM shut down afterwards. ollama was already inactive before the check and was left inactive.

## Triage — production round P (Opus, 2026-11-01)
Sol asked for bounded L2–L4 work built on the trial. This round covers L2 + L3 in parallel. L4 (installers/docs) starts only after both are merged into the team branch.
Both workers start from hard-1's trial commit **e8f398c** (I reset their worktrees to it), so the trial code is kept and turned into real code. Trial hooks get removed.

**Staffing:** 2 hard workers in parallel (Grok 4.7). Both tasks are hard: process control, native ABI, data-root design and cross-OS UI helpers, where a wrong guess costs a lot. Each is widespread across 8–12 files. No easy work can be split off cleanly.

### Shared-file decision
Generator.cs and LibraryData.cs need two kinds of change: tool paths (14 `.\bin\*.exe` sites) and data roots. To avoid touching them twice, **data roots are done by changing the working directory**: at startup hard-2 sets `Environment.CurrentDirectory` to the writable data root and gives an absolute resource root for read-only files. Existing relative writes (`library/`, `temp/`, `plugins/`, settings JSON) then land in the data root without edits. So Generator.cs and LibraryData.cs go wholly to hard-3, and hard-2 doesn't touch them.
`PlatformFileDialog.OpenFiles(string title, string filter, bool multiselect, string? initialDir)` is used by hard-3's PluginHandler.cs. **hard-2 must keep that signature.**

### flow-1101-0201-hard-2 — L2 app shell, data roots, UI helpers
Owns: `src/backend/platform/{NvgPaths,PlatformClipboard,PlatformFileDialog,PlatformShell,PlatformTts,GraphicsThread,BmpThumbnail,TrialGate}.cs`, new `src/backend/platform/DataRoots.cs`; `src/backend/{Program,UserInterface,Accessibility,Global,GlobalGraphics}.cs`; `src/interactables/TextInput.cs`; `src/pages/{LibraryPage,PluginsPage}.cs`; `src/data/{SaveData,FramePlayer,ThemeManager}.cs`; new `tests/NvgPlatform.Tests/**`.
Must not touch: anything hard-3 owns (listed below), `AGENT_HANDOFF.md`, `PLAN_*.md`, `TRIAL_S1_L1.md`, publish/Steam/Proton scripts, the main checkout. Ask Opus for any other `src/` file.
Work:
1. Remove the trial hooks: `TrialGate.cs`, `-trial`, `-vocoder-stdin` (Program.cs, UserInterface.cs).
2. Data roots (DesktopGL only; Windows unchanged):
   - Writable root: `~/Library/Application Support/NonsensicalVideoGenerator` on macOS, `${XDG_DATA_HOME:-~/.local/share}/NonsensicalVideoGenerator` on Linux. The `NVG_DATA_DIR` env var overrides it (tests use this).
   - Resource root: `AppContext.BaseDirectory`. MonoGame content must load from the absolute resource root.
   - First run seeds `plugins/`, `templates/`, `locales/` and the baseroot files from the resource root. Never overwrite user-changed files. Later versions add missing stock files only.
   - No migration of existing data.
3. Real helpers:
   - File dialogs: macOS `osascript` (choose file, multiple selections, type filter). Linux tries `zenity`, then `kdialog`. Cancel returns empty. A missing helper shows a visible message and never hangs. Arguments go through `ArgumentList`, never a joined string.
   - Clipboard: SDL2 via P/Invoke. Free SDL-owned memory with `SDL_free`.
   - TTS: `say` on macOS; `spd-say`, then `espeak-ng`, on Linux. Cancel/queue works. A missing voice service is visible but not fatal.
   - Folder open: `open` / `xdg-open`, used for all `explorer.exe` sites in LibraryPage/PluginsPage.
   - DesktopGL `Window.FileDrop` for drag-and-drop.
4. Fix the empty boot-video path (`Could not load boot video:` in the trial log). Sweep the remaining backslash paths in owned files.
Acceptance and tests:
- `tests/NvgPlatform.Tests` (xUnit, net8.0) covers data-root resolution and the `NVG_DATA_DIR` override, seeding without overwriting, paths with spaces/non-ASCII, dialog behaviour with a missing helper, and dialog/shell argv building. Tests must set `NVG_DATA_DIR` and a temp `HOME`/`XDG_DATA_HOME`, and must never touch real user dirs; check this before the first run.
- Run: `dotnet test tests/NvgPlatform.Tests` on the Mac and on 750 (in `~/nvg-p2/`), plus `dotnet build -p:NvgPlatform=DesktopGL` on both.
- Windows default build: compile-check only on the Mac is not possible. Report it as "needs Windows VM build". Opus runs it.
- GUI checks (dialog, clipboard, TTS, drop) go **last, one at a time**. If one needs focus, don't retry; list exact steps for a human.

### flow-1101-0201-hard-3 — L3 tools, media, build
Owns: `NonsensicalVideoGenerator.csproj`, `.gitignore`, `content/Content.DesktopGL.mgcb`; `src/backend/Generator.cs`; `src/data/{PluginHandler,SteamManager,LibraryData}.cs`; `src/backend/platform/{FfmpegNative,SteamworksStub,ProcessTree}.cs`, `src/backend/platform/retarget-ilonly.py`, new `src/backend/platform/ToolPaths.cs`; `baseplugins/**`; `tools/**`; new `tests/NvgMedia.Tests/**`.
First step: `git merge atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-easy-1` (brings `tools/vocoder/`). Then fix NOTES.md: 750 is Linux Mint 22.3 (Ubuntu noble base).
Must not touch: anything hard-2 owns, `AGENT_HANDOFF.md`, `PLAN_*.md`, `TRIAL_S1_L1.md`, `publish.*`, `steamcmd*`, `proton.sh`, the main checkout.
Work:
1. `ToolPaths`: one resolver for ffmpeg, ffprobe, magick, yt-dlp, deno and vocoder. Order: bundled `<resource root>/bin/<name>[.exe]`, then PATH. Replace all 14 Generator.cs sites, the LibraryData yt-dlp/deno sites and the PluginHandler sites. Make `Installed()` checks and diagnostics report the real availability and the required FFmpeg codecs/filters. Windows resolution stays as today.
2. Process control: replace the PowerShell/CIM kill at Generator.cs:73 with owned-process tracking plus `Kill(entireProcessTree:true)`. Cancel kills only this job's processes, never other ffmpeg processes.
3. Vocoder no-argument spin: add a per-command timeout in `Command.Call` (DesktopGL), and/or a tiny patch applied by `tools/vocoder/build-vocoder.sh` that exits on EOF (record the patch in NOTES.md).
4. FFmpeg 7.1 app-local: `tools/ffmpeg7/build-ffmpeg7.sh` (macOS + Linux) builds pinned 7.1.x shared libs from source into a given dir and records the version and licence. Fix the Linux `.so.61.so` lookup in `FfmpegNative`/the resolver, rather than staging an alias name.
5. Build:
   - csproj DesktopGL `DefineConstants` must append (`$(DefineConstants);DESKTOPGL`) so DEBUG/TRACE stay.
   - `.gitignore` gets `bin/`, `obj-desktopgl*/` and the content outputs.
   - `tools/build-content-desktopgl.sh` automates the arm64-Mac content build (x64 SDK under Rosetta, user-local) and the linux-x64 content build.
   - Make the build copy content/plugins/locales into the publish output.
   - `NVG_STEAM` + DesktopGL must either compile or fail with one clear error message.
6. Remove any trial-only code left in owned files. Keep `retarget-ilonly.py` only if it's still needed, and document why.
Acceptance and tests:
- `tests/NvgMedia.Tests` (xUnit, net8.0) covers ToolPaths order and the missing-tool case, owned-process cancellation (spawn `sleep`, cancel, check an unrelated `sleep` survives), vocoder no-arg returning within the timeout, and FFmpeg library-name mapping per OS. Use a temp `NVG_DATA_DIR`/`HOME`.
- Run: `dotnet test tests/NvgMedia.Tests` on the Mac and on 750 (in `~/nvg-p3/`); `dotnet build -p:NvgPlatform=DesktopGL` and `dotnet publish -r linux-x64` on both; `tools/ffmpeg7/build-ffmpeg7.sh` on 750 into `~/nvg-p3/ffmpeg7`; a CLI render of a stock effect with the bundled-tools path, checked with ffprobe.
- No sudo/apt/brew changes. User-local SDKs in `~/.dotnet-nvg` / `~/.dotnet-nvg-x64` only.

### Both workers
- Don't push or publish. Steam stays off.
- Quote every path. macOS has no `timeout`, so use `gtimeout` or a Python wrapper.
- Commit on your own branch. Report: branch, final commit, commands run with their pass/fail output, and anything not verified.

### After P (not started yet)
- Sol merges hard-2 + hard-3 into the team branch.
- Opus runs the Windows VM build and the trial merge.
- Then L4 (one hard worker): `.app`/DMG with ad-hoc signing, a Linux tarball with user-local installer and desktop entry, and docs.
- Human/computer-use GUI pass on Mac and Linux: Library → Add → preview with audio heard, dialog, clipboard, TTS. Windows interactive launch in the VM.

## L2 app shell result — Opus report, 2026-11-01
- hard-2 APPROVED by Opus and closed, branch retained: `atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-2`, final `6445621` atop `c8fdfd0`, base `e8f398c`. Worktree path not included in report; verify actual path before eventual cleanup.
- Changes reported: native writable DataRoots (Mac Application Support/Linux XDG, NVG_DATA_DIR override), non-overwriting seed of plugins/templates/locales/library/docs excluding bin, absolute content resource root, trial hooks removed, real chooser/clipboard/TTS/shell helpers, boot-movie empty-path fix.
- Evidence reported: tests/NvgPlatform.Tests 13/13 on Mac and 750; Linux DesktopGL build with content passed in worker validation. Not independently repeated by Sol.
- Integration dependency: hard-2 alone does not compile DesktopGL because the game project includes tests/**; hard-3 owns the Compile Remove fix. Do not treat hard-2 as an independently buildable delivered state; retain both branches until reviewed integration compiles and checks pass.
- Still unverified: real chooser completion, file drop, audible preview, Windows GUI. Opus plans Windows build regression after integration. hard-3 remains in progress; main clean per Opus.
- NEXT ACTION: Await hard-3 and Opus's consolidated integration review; then verify combined tests/build and remaining UI acceptance before installer work. No main merge or push.
