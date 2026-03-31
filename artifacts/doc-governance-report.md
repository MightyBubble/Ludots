# Documentation Governance Report

Date: 2026-04-01
Scope:
- `docs/architecture/README.md`
- `docs/architecture/entity_insight_panel_architecture.md`
- `docs/architecture/item_inventory_equipment_architecture.md`
- `scripts/acceptance/run-item-system-showcase-acceptance.ps1`
- `scripts/acceptance/run-item-system-showcase-raylib.ps1`
- `scripts/acceptance/run-item-loadout-showcase-acceptance.ps1`
- `scripts/acceptance/run-weapon-bench-showcase-acceptance.ps1`
- `scripts/acceptance/run-forge-socket-showcase-acceptance.ps1`
- `scripts/acceptance/run-raid-loop-showcase-acceptance.ps1`
Ruleset:
- `docs/conventions/04_documentation_governance.md`
- `C:/Users/123/.codex/skills/ludots-doc-governance/references/doc-governance-checklist.md`
- `C:/Users/123/.codex/skills/ludots-doc-governance/references/link-validation.md`

## Summary
- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Findings

No governance violations remain in the merged architecture-entry and item-showcase acceptance scope.

Validated items:
- `docs/architecture/README.md` keeps one current SSOT entry list and now indexes both `entity_insight_panel_architecture.md` and `item_inventory_equipment_architecture.md`.
- Added architecture docs use repository-relative links only.
- Item showcase acceptance script references remain repository-relative and align with the current wrapper-script governance requirements.

## Fix Order
1. Keep `docs/architecture/README.md` as the single architecture entry index.
2. Extend the existing item and entity-insight SSOT docs instead of creating parallel overview files.
3. Re-run governance checks when acceptance scripts or architecture entry links change.

## Residual Risks
- This report covers the merged entity insight and item-system slices only; unrelated historical docs and artifacts were not re-audited in this run.
