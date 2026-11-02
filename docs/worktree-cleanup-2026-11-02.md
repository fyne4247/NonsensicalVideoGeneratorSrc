# ATMUX worktree cleanup audit — 2026-11-02

Owner requested removal of old unused fully merged ATMUX worktrees in this repository. Commits were checked against integration HEAD `e628e9a63db22316a6998b975efdcd0fd6dc9677`; this does not claim they are merged into main.

Outcome: no working copies removed. Git refuses normal removal of the sole completely clean candidate because it contains a submodule. All other merged candidates have untracked or ignored contents, which were preserved. No force, branch deletion, remote cleanup, VM changes, or app-data cleanup performed. `git worktree prune` ran.

Opus confirms hard-17 is needed for pending integration/review and hard-18 is active; no other closed-worker copy is needed for review. hard-4 is superseded but is not an ancestor of the integration branch, so remains excluded.

| Worker | Branch | HEAD | Included in integration | Untracked entries | Ignored entries | Result |
|---|---|---|---|---:|---:|---|
| easy-1 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-easy-1 | 346a6f9d05e714e6776e76cf63f8f05c9177b896 | yes | 0 | 0 | Kept: Git submodule removal restriction |
| hard-1 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-1 | e8f398c74828c8cc51f21286eb43032ae9857bfb | yes | 211 | 9 | Kept: local/untracked/ignored contents |
| hard-10 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-10 | 525c3b12a76adba72c429142cebda095f8492719 | yes | 1 | 6 | Kept: local/untracked/ignored contents |
| hard-11 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-11 | ac886ddccd2bbb925ac2274d119b6803a9281e3c | yes | 0 | 6 | Kept: local/untracked/ignored contents |
| hard-12 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-12 | 1d75af44d0318729d4d6cfa4899b4b02a8f55500 | yes | 1 | 8 | Kept: local/untracked/ignored contents |
| hard-13 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-13 | 8279f532fbcf96444079c60832e00c2d48b083d0 | yes | 1 | 6 | Kept: local/untracked/ignored contents |
| hard-14 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-14 | 020c163b31117afd0c456e0473f9e7757e632971 | yes | 1 | 4 | Kept: local/untracked/ignored contents |
| hard-15 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-15 | b083cf83d651cf1b49dd938367d9d927d4362fc5 | yes | 1 | 9 | Kept: local/untracked/ignored contents |
| hard-16 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-16 | 50a3a1ec1fa294e7f9b2f439de71b685c893b7cf | yes | 1 | 10 | Kept: local/untracked/ignored contents |
| hard-17 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-17 | 4f8d80bbc2e790184ff064ff60b3d0ee9b3925be | no | 1 | 2 | Kept: unmerged / pending work |
| hard-18 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-18 | f4d1b76f1c3c37f3bf70252c1110a957b94703aa | no | 1 | 6 | Kept: unmerged / pending work |
| hard-2 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-2 | 6445621c99d13cdd1f0348a7742839cecba89bc2 | yes | 5 | 8 | Kept: local/untracked/ignored contents |
| hard-3 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-3 | 44fbf6e075a68e26d435fac6aa519ea9ef1896f2 | yes | 1 | 7 | Kept: local/untracked/ignored contents |
| hard-4 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-4 | 6223094d55199850255efeca4f7c39b604aeabbf | no | 1 | 6 | Kept: unmerged / pending work |
| hard-5 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-5 | 1591e27032f3174d13bb5197db2e9c1c8b7cdbf7 | yes | 1 | 9 | Kept: local/untracked/ignored contents |
| hard-6 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-6 | cf973d40e1ca35cb4c2d7348e197fbcd8ff3ca6d | yes | 1 | 8 | Kept: local/untracked/ignored contents |
| hard-7 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-7 | 1bd250a86eb54b5056c11252032ce48a79e8601e | yes | 1 | 5 | Kept: local/untracked/ignored contents |
| hard-8 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-8 | d2d0ba14e6a43cbf3fa89aa82a8341ca193ce984 | yes | 1 | 6 | Kept: local/untracked/ignored contents |
| hard-9 | atmux-NonsensicalVideoGeneratorSrc-flow-1101-0201-hard-9 | 6e415d384efe2aeecade38963a24719521d671b4 | yes | 1 | 6 | Kept: local/untracked/ignored contents |

All worker paths are under `/Users/fish4247/.atmux/agents/NonsensicalVideoGeneratorSrc-flow-1101-0201-<worker>`. The ordinary checkout and current team worktree were not candidates.

The clean easy-1 copy and its public submodule had no tracked, untracked or ignored changes. Normal `git worktree remove` refused. Normal `git submodule deinit -- public` succeeded but normal removal still refused. The original submodule configuration was preserved, and the submodule was reinitialized at recorded commit `38995cb4a419b7e511f1f1d462e01fb5b0dcbf5c`; both parent and submodule status are clean after restoration.

The active workflow forbids forced removal. No attempt was made to bypass Git by manually deleting working-copy directories. A future cleanup needs a workflow-compatible way to handle submodule worktrees and an explicit disposition for local build/harness files. Detailed original inventory remains at `/tmp/nvg-worktree-audit-20261102.json`.
