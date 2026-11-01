# Platform planning handoff — 2026-11-01

## Objective and acceptance criteria
Owner asks to get NVG running natively on macOS and/or Linux where practical, with Wine as fallback, and explicitly assigns macOS planning to Opus and Linux planning to Sol. Current deliverable is two evidence-based plans and a consolidated recommendation; implementation is not started.

Plans must identify Windows dependencies, native feasibility risks, file/task boundaries, dependency order, verification requirements and fallback criteria. No runtime support may be claimed without actual verification.

## Repository state
- Team branch: atmux-NonsensicalVideoGeneratorSrc-team-flow-1101-0201.
- Baseline/integration before planning: 58ecdd1b93d2035a1f9df60119cd82a7544692ba.
- Team worktree: /Users/fish4247/.atmux/teams/NonsensicalVideoGeneratorSrc/flow-1101-0201/worktree.
- Initial tracked/untracked working tree clean. public submodule is uninitialized at recorded commit 38995cb4a419b7e511f1f1d462e01fb5b0dcbf5c.
- Target main is checked out at /Users/fish4247/Vibecoding/NonsensicalVideoGeneratorSrc. No merge/push/deployment performed. Planning records stay on the team branch pending review; do not merge implementation that does not exist.

## Assignments and current state
- Sol: PLAN_LINUX.md, this handoff. Linux repository audit and plan complete; awaiting Opus's combined plan review.
- Opus (flow-1101-0201-opus): PLAN_MACOS.md in the shared team worktree/branch. macOS investigation complete; revised evidence and review pending. No separate worker branches or worktrees created. Baseline HEAD for both is the integration baseline above until the plan checkpoint.
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
- Plan checkpoint is not implementation approval.
- Opus asked to review combined plan and correct unsupported macOS assumptions. Pending consolidated review; no implementation commits to approve.
- Real Linux test environment remains to be established. Native compatibility is not yet verified on either OS.

## NEXT ACTION
Have Opus review PLAN_LINUX.md and PLAN_MACOS.md, resolve blocking planning findings, and record the review result here. Commit the final planning records, report the shared native-first trial and wrapper fallback to the owner, and leave implementation for the next authorized work step. Never push.
