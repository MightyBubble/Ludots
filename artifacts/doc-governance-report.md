# Documentation Governance Report

Date: 2026-07-19
Scope: PR #658 MassNavigation/Formation SSOT pages and capability README
Ruleset: `gitbook/contributing/documentation-governance.md`, `ludots-doc-governance`

## Summary

- Total open findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Resolved Findings

### P0-01 Stale ownership model

- Problem: formal docs described MassNavigation as an Order consumer and Formation as Optional Core.
- Impact: the documentation certified the same architectural inversion fixed by issue #690.
- Evidence:
  - `gitbook/reference/mass-navigation-formal-chain.md`
  - `gitbook/reference/mass-navigation-user-book.md`
  - `gitbook/architecture/entity-simulation-layering.md`
  - `gitbook/architecture/entity-simulation-uat.md`
- Resolution: replaced with Command Router cluster forwarding, GAS-owned lifecycle, showcase-owned Formation and typed MassNavigation execution.

### P1-01 Removed feature still promised to players

- Problem: user/UAT docs promised Q/E rotation and dedicated Formation orders.
- Impact: tests and documentation preserved a presentation-only action as a gameplay feature.
- Evidence:
  - `gitbook/reference/mass-navigation-user-book.md`
  - `gitbook/architecture/uat-playable-showcase-matrix.md`
- Resolution: removed rotation from player and Mod contracts.

### P1-02 Numeric boundary assigned Order completion to MassNavigation

- Problem: numeric SSOT allowed solver arrival to mutate `OrderBuffer` directly.
- Impact: ownership and failure semantics crossed module boundaries.
- Evidence:
  - `gitbook/architecture/mass-navigation-numeric-domain.md`
- Resolution: arrival/failure now cross the boundary only as `MovePlanExecutionResult`; GAS completes or cancels.

## Path Integrity

- Canonical pages remain under `gitbook/`.
- No parallel ADR was added.
- `gitbook/SUMMARY.md` now includes the UAT showcase matrix.
- All 10 changed Markdown files passed Markdown-link and repository-path validation.
- Guide and capability README paths use explicit repository-relative targets.
- Evidence paths point to current source/test files.

## Fix Order

1. Completed: formal chain and responsibility boundary.
2. Completed: Mod/player guide and UAT contract.
3. Completed: numeric boundary and capability README.

## Residual Risks

- Historical issue comments and closed issues remain historical evidence; issue #690 is the only current SSOT.

## Persistence / Replay showcase review (2026-08-23)

Scope: `gitbook/architecture/persistence-online-replay-showcase-design.md`, `gitbook/acceptance/persistence-online-replay.feature`, `artifacts/acceptance/persistence-online-replay/`, `showcase.registry.json`.

Ruleset: `ludots-doc-governance` checklist, link-validation rules, and showcase-design delivery gates.

### Summary

- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

### Evidence

- The showcase registry points to a real Mod, launcher preset, UAT feature, acceptance directory, and tracked screenshots.
- Repository-relative paths in the changed design/UAT/report files resolve.
- The design explicitly marks reconnect as a single-process equivalent fault injection; real Online adapter end-to-end remains a follow-up boundary.
- Runtime evidence is recorded in `artifacts/acceptance/persistence-online-replay/trace.jsonl` and `battle-report.md`.

### Residual risks

- A real network transport adapter and multi-process client/server test are not part of this showcase delivery and must not be described as accepted by this evidence.
