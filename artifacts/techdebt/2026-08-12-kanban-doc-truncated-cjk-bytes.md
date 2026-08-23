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
