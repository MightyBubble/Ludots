# Documentation Governance Report

Date: 2026-04-01
Scope:
- `docs/architecture/README.md`
- `docs/architecture/entity_selection_architecture.md`
- `docs/architecture/order_navigation_movement.md`
- `docs/architecture/interaction/README.md`
- `docs/rfcs/RFC-0059-road-order-nav-runtime-unification.md`
- `docs/architecture/entity_insight_panel_architecture.md`
- `docs/architecture/item_inventory_equipment_architecture.md`
- `docs/architecture/narrative_quest_dialogue_cinematic.md`
- `docs/architecture/narrative_frontend_kit.md`
- `scripts/acceptance/run-item-system-showcase-acceptance.ps1`
- `scripts/acceptance/run-item-system-showcase-raylib.ps1`
- `scripts/acceptance/run-item-loadout-showcase-acceptance.ps1`
- `scripts/acceptance/run-weapon-bench-showcase-acceptance.ps1`
- `scripts/acceptance/run-forge-socket-showcase-acceptance.ps1`
- `scripts/acceptance/run-raid-loop-showcase-acceptance.ps1`

Ruleset:
- `docs/conventions/04_documentation_governance.md`
- `ludots-doc-governance`
- `doc-governance-checklist.md`
- `link-validation.md`

## Summary
- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Findings

No governance findings remain in the reviewed architecture-entry, road-order, entity-insight, narrative-frontend, and item-showcase scope after conflict cleanup and stale-reference correction.

Validated items:
- `docs/architecture/README.md` keeps one current SSOT entry list and now indexes `entity_insight_panel_architecture.md`, `item_inventory_equipment_architecture.md`, `order_navigation_movement.md`, `narrative_quest_dialogue_cinematic.md`, and `narrative_frontend_kit.md`.
- Added architecture docs use repository-relative links only.
- Item showcase acceptance script references remain repository-relative and align with the current wrapper-script governance requirements.

## Fix Order
1. Keep `docs/architecture/README.md` as the single architecture entry index.
2. Keep `docs/architecture/entity_selection_architecture.md` and `docs/architecture/order_navigation_movement.md` aligned whenever selection-order handoff changes.
3. Re-run path-integrity checks whenever referenced code, docs, or acceptance scripts move.

## Residual Risks
- This report covers the reviewed architecture / RFC / item-showcase scope, not the entire `docs/` tree.
- Future path renames still require a fresh integrity pass.
