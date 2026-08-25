# Tech Debt Report: TD-2026-08-12-kanban-doc-truncated-cjk-bytes

Date: 2026-08-12
Reporter: Cursor Agent
Owner: Docs Governance / Presentation
Severity: P3
Scope: Subsystem

## Trigger
- Scenario: presenter rename audit (PR #894)
- Entry point: `gitbook/architecture/presenter-development-kanban.md`
- Repro steps:
  1. Open the kanban page from `gitbook/SUMMARY.md`.
  2. Observe that 426 CJK characters render as garbage.
  3. Inspect the bytes: every damaged character kept its first two UTF-8 bytes
     and lost its third byte plus the character that followed it, both collapsed
     into a single literal `?`.

## Evidence
- `gitbook/architecture/presenter-development-kanban.md`
- `src/Tests/GasTests/Config/ConfigMergeE2ETests.cs` (same damage pattern, repaired in this branch)

## Impact
- User-visible impact: the presenter development kanban is unreadable for Chinese readers; roughly 850 characters of task history and acceptance notes are lost.
- Correctness/stability risk: none at runtime; the page is documentation only.
- Blast radius: one gitbook page. The identical damage in the config merge test file is already repaired, so the pattern is understood and reproducible.

## Fuse Decision
- Mode: explicit-degrade
- Reason: the damage predates the rename and reconstructing the text means authoring roughly 850 characters of Chinese from context. Guessing content into an architecture page is worse than leaving visible garbage, so the page stays damaged but recoverable.
- Observability fields:
  - debt_id: `TD-2026-08-12-kanban-doc-truncated-cjk-bytes`
  - fuse_mode: `explicit-degrade`
  - reason_code: `docs.gitbook.truncated_cjk_bytes`

## Containment and Follow-up
- Immediate containment: keep the original bytes. Rewriting them to U+FFFD looks like normalization but destroys the two surviving bytes that narrow each damaged character to 64 candidates, which is what makes repair possible at all.
- Permanent fix direction: repair the page against the same recovery model used for the config merge test file, with a Chinese reader confirming each reconstructed character, then normalize the file to UTF-8.
- Target milestone: next docs governance pass.

## Resolution (2026-08-24)
- Status: RESOLVED. `gitbook/architecture/presenter-development-kanban.md` is repaired and normalized to UTF-8 (0 U+FFFD, 200 lines restored).
- Method: instead of per-character guessing, every damaged line was aligned against the last clean git base `fdddb3aff6:gitbook/architecture/performer-development-kanban.md` (18159 chars, 0 damage); each damaged run was refilled with the exact base segment and the performer→presenter rename was re-applied. 145 lines auto-refilled, 53 lines hand-mapped from base rows, 1 post-damage annotation line (§8.4 status note) reconstructed from sentence pattern; adjacent-duplication artifacts rescanned to zero.
- The same damage in `gitbook/architecture/presenter-as-actor-architecture.md` (339 U+FFFD, not previously tracked by this debt) was repaired the same way against base `942d077cd0` (182 lines restored, 0 artifacts).
