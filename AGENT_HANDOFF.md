# Agent Handoff

## Goal and acceptance criteria

Consolidate the current Phase 2 memory update in `/Users/fish4247/.codex/memories` from the authoritative workspace diff, without changing raw evidence. The result must replace the deleted Gmail rollout reference, add the Hunter × Hunter episode-sorting memory, refresh only affected handbook and v1 summary content, and pass provenance/schema audits.

## Tasks

1. Route changed evidence
   - Files: `/Users/fish4247/.codex/memories/phase2_workspace_diff.md`, `raw_memories.md`, `extensions/ad_hoc/instructions.md`, changed rollout summaries.
   - Done: classify the Gmail replacement and Hunter × Hunter task; identify stale summary references and any extension-derived signal.

2. Consolidate handbook entries
   - Files: `/Users/fish4247/.codex/memories/MEMORY.md`.
   - Done: replace deleted rollout references and stale boundaries/findings surgically; add a schema-compliant media-organization block with task metadata, preferences, reusable ranges, and verification.

3. Refresh prompt summary
   - Files: `/Users/fish4247/.codex/memories/memory_summary.md`.
   - Done: preserve exact `v1` header, update the Gmail routing entry to current evidence, add the 2026-11-01 media topic, and retain concise cross-task preferences.

4. Validate
   - Files: finalized `MEMORY.md`, `memory_summary.md`.
   - Done: confirm cited rollout files exist, deleted filename is absent, all task references carry required metadata, and no raw input was modified.

## Dependencies

- Task 1 precedes Tasks 2 and 3.
- Task 2 precedes Task 3.
- Task 4 follows all edits.

## Risks and open questions

- Existing Gmail knowledge appears in more than one overlapping task group; preserve still-supported older evidence while replacing only the deleted summary’s metadata and transient run facts.
- The extension has no resource files in the change diff; its instructions govern handling only if its note content is present.
- No project test suite applies; structural and provenance checks are the relevant verification.
