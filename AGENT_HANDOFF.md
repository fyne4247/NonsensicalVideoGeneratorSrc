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
