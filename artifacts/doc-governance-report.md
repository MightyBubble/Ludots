# Doc Governance Report

Date: 2026-04-01
Scope:
- `docs/architecture/time_flow.md`
- `docs/audits/pr92_timeflow_core_mainline_delivery.md`
- `docs/architecture/README.md`
- `docs/audits/README.md`

Rule Set:
- `docs/conventions/04_documentation_governance.md`
- `C:/Users/123/.codex/skills/ludots-doc-governance/references/doc-governance-checklist.md`

Findings:
- P0: none
- P1: none
- P2: none
- P3:
  - Removed two stale links from `docs/architecture/README.md`:
    - `animation_profile_clip_pipeline.md`
    - `animation_profile_clip_kanban.md`

Validation:
- Changed-doc markdown links resolved successfully.
- New TimeFlow SSOT stays within `docs/architecture/`.
- PR92 split-plan document stays within `docs/audits/` and does not redefine SSOT.

Fix Order:
1. Keep `docs/architecture/time_flow.md` as the only TimeFlow SSOT.
2. Keep upper-layer PR92 convergence notes in `docs/audits/`.
3. Regenerate this report if changed-doc scope expands.
