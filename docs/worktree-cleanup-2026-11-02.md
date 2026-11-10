# Staging consolidation and worktree cleanup — 2026-11-02

## Current result

- Created local staging from main and merged the complete reviewed team integration with --no-ff. Initial staging merge: 839a6fa0df36965590c289c231e7b6820c83bf8f. This is testing/staging, not approval for a release or a merge into main.
- Removed20 inactive worker worktrees whose HEADs are ancestors of staging. Every source branch and commit was verified preserved afterward. No force, branch deletion, manual worktree-directory deletion, target history rewrite, or push.
- All untracked and ignored contents were moved intact, by same-filesystem rename, to the recovery directory before Git removed each now-clean checkout. Private submodule repositories were preserved there too. No local data was discarded.
- Recovery: /Users/fish4247/.atmux/recovery/NVG-staging-20261102-xona7fzs (8.4GB allocated at completion). The pilot.json and cleanup-journal.json files record full paths and preserved local-item mappings. Keep this directory until its contents have an explicit disposition.
- Measured available space:322MiB before consolidation,4.6GiB after. Background/system reclamation may contribute; do not attribute the entire change to worktree removal.
- Retained the ordinary dev checkout, current team checkout, and unmerged superseded hard-4 working copy. main/dev remain58ecdd1b93d2035a1f9df60119cd82a7544692ba.
- Owner media, frozen evidence, /tmp packages/test roots, VM, remote750 files and current Opus session were untouched.

## Worker records

| Worker | Branch | HEAD | Outcome | Preserved local paths |
|---|---|---|---|---:|
| easy-1 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-easy-1 | 346a6f9d05e714e6776e76cf63f8f05c9177b896 | Removed normally; refs kept | 0 |
| hard-1 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-1 | e8f398c74828c8cc51f21286eb43032ae9857bfb | Removed normally; refs kept | 7 |
| hard-10 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-10 | 525c3b12a76adba72c429142cebda095f8492719 | Removed normally; refs kept | 7 |
| hard-11 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-11 | ac886ddccd2bbb925ac2274d119b6803a9281e3c | Removed normally; refs kept | 6 |
| hard-12 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-12 | 1d75af44d0318729d4d6cfa4899b4b02a8f55500 | Removed normally; refs kept | 9 |
| hard-13 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-13 | 8279f532fbcf96444079c60832e00c2d48b083d0 | Removed normally; refs kept | 7 |
| hard-14 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-14 | 020c163b31117afd0c456e0473f9e7757e632971 | Removed normally; refs kept | 5 |
| hard-15 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-15 | b083cf83d651cf1b49dd938367d9d927d4362fc5 | Removed normally; refs kept | 10 |
| hard-16 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-16 | 50a3a1ec1fa294e7f9b2f439de71b685c893b7cf | Removed normally; refs kept | 11 |
| hard-17 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-17 | 4f8d80bbc2e790184ff064ff60b3d0ee9b3925be | Removed normally; refs kept | 3 |
| hard-18 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-18 | 3986999d10d0b0eb4a341f1bc57504e9f35500e8 | Removed normally; refs kept | 7 |
| hard-19 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-19 | 4c7f56a7c34e841ba6e87139e2df277dac5f9c47 | Removed normally; refs kept | 10 |
| hard-2 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-2 | 6445621c99d13cdd1f0348a7742839cecba89bc2 | Removed normally; refs kept | 8 |
| hard-20 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-20 | e976925e658132e4614e20286614f6a26aa9a208 | Removed normally; refs kept | 6 |
| hard-3 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-3 | 44fbf6e075a68e26d435fac6aa519ea9ef1896f2 | Removed normally; refs kept | 8 |
| hard-4 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-4 | 6223094d55199850255efeca4f7c39b604aeabbf | Kept: not merged into staging | 0 |
| hard-5 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-5 | 1591e27032f3174d13bb5197db2e9c1c8b7cdbf7 | Removed normally; refs kept | 10 |
| hard-6 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-6 | cf973d40e1ca35cb4c2d7348e197fbcd8ff3ca6d | Removed normally; refs kept | 9 |
| hard-7 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-7 | 1bd250a86eb54b5056c11252032ce48a79e8601e | Removed normally; refs kept | 6 |
| hard-8 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-8 | d2d0ba14e6a43cbf3fa89aa82a8341ca193ce984 | Removed normally; refs kept | 7 |
| hard-9 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-9 | 6e415d384efe2aeecade38963a24719521d671b4 | Removed normally; refs kept | 7 |

## Safe submodule handling

The initial audit could remove nothing because the copies contained local files or submodules. Git still refuses normal removal after deinitialization if a worktree-private modules directory remains ([upstream implementation](https://github.com/git/git/blob/master/builtin/worktree.c)). This was resolved without forcing deletion: verify the submodule clean and pinned; deinitialize it normally; move its private Git metadata to recovery; verify its commit still readable; restore shared submodule configuration; then run normal git worktree remove. Local extras were preserved first. On refusal, restore the moved data and leave the worktree.

Archived submodule metadata can retain its original worktree path in configuration. Restore its original metadata location before reinitializing that checkout, or recover the recorded commit from the preserved Git repository. Do not prune/delete the recovery directory automatically.

## Validation and remaining work

The staging code is identical to tested integration f658747: Mac142/142, native Release build passes; Windows121pass/21platform skips/zero failures and app build passes (Opus-reviewed evidence). Consolidation changes history/documentation only; no redundant suite rerun. Check staging and team trees match after the final cleanup-record merge.

Unresolved acceptance remains unchanged: the historical library-file loss is unexplained; visible chooser responsiveness, fresh final packages and real-window playback/remaining platform GUI checks need completion. No main merge or publication authorized by staging consolidation.
