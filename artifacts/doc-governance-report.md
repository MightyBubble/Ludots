# Documentation Governance Report

Date: 2026-03-24
Scope:

- `docs/architecture/item_inventory_equipment_architecture.md`
- `scripts/acceptance/run-item-system-showcase-acceptance.ps1`
- `scripts/acceptance/run-item-system-showcase-raylib.ps1`
- `scripts/acceptance/run-item-loadout-showcase-acceptance.ps1`
- `scripts/acceptance/run-weapon-bench-showcase-acceptance.ps1`
- `scripts/acceptance/run-forge-socket-showcase-acceptance.ps1`
- `scripts/acceptance/run-raid-loop-showcase-acceptance.ps1`

Ruleset:

- `docs/conventions/04_documentation_governance.md`
- `C:/Users/ROG/.codex/skills/ludots-doc-governance/references/doc-governance-checklist.md`
- `C:/Users/ROG/.codex/skills/ludots-doc-governance/references/link-validation.md`

## Summary

- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Findings

No governance violations remain in the scoped item showcase split-mod packet.

## Fix Order

1. Keep `docs/architecture/item_inventory_equipment_architecture.md` as the SSOT for the split-mod showcase delivery shape.
2. When adding new focused item demos, extend the same shared-runtime section and add matching acceptance/evidence paths there.
3. Keep wrapper-script examples aligned with the real script parameters before publishing any new doc examples.

## Residual Risks

- This report only covers the item showcase split-mod delivery slice; unrelated historical docs and wrapper scripts were not re-audited in this run.
