# Documentation Governance Report

Date: 2026-03-13
Scope: `docs/reference/cli_runbook.md`, `docs/conventions/03_environment_setup.md`, `docs/architecture/startup_entrypoints.md`, `docs/architecture/interaction/features/unit_target/c1_hostile_unit_damage.md`, `docs/architecture/interaction/features/unit_target/c2_friendly_unit_heal.md`, `scripts/record-interaction-c1-hostile-unit-damage.ps1`, `scripts/review-interaction-c1-hostile-unit-damage.ps1`, `scripts/record-interaction-c2-friendly-unit-heal.ps1`, `scripts/review-interaction-c2-friendly-unit-heal.ps1`
Ruleset: wrapper command contract, launcher CLI command parity, repository-relative path integrity, SSOT evidence links, current showcase artifact alignment

## Summary
- Total findings: 0
- P0: 0
- P1: 0
- P2: 0
- P3: 0

## Findings

No open P0-P3 findings remain in the reviewed scope after the launcher CLI parity pass and the C1/C2 interaction doc alignment pass.

Validated evidence:
- `scripts/run-mod-launcher.cmd`
- `scripts/run-mod-launcher.ps1`
- `scripts/record-interaction-c1-hostile-unit-damage.ps1`
- `scripts/review-interaction-c1-hostile-unit-damage.ps1`
- `scripts/record-interaction-c2-friendly-unit-heal.ps1`
- `scripts/review-interaction-c2-friendly-unit-heal.ps1`
- `src/Tools/Ludots.Launcher.Cli/Program.cs`
- `src/Tools/Ludots.Launcher.Backend/LauncherService.cs`
- `docs/architecture/interaction/features/unit_target/c1_hostile_unit_damage.md`
- `docs/architecture/interaction/features/unit_target/c2_friendly_unit_heal.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/battle-report.md`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/trace.jsonl`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/path.mmd`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/summary.json`
- `artifacts/acceptance/interaction-c1-hostile-unit-damage/visual/visible-checklist.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/battle-report.md`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/trace.jsonl`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/path.mmd`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/summary.json`
- `artifacts/acceptance/interaction-c2-friendly-unit-heal/visual/visible-checklist.md`
- `artifacts/reviews/interaction-c2-friendly-unit-heal-claude-review.md`

## Fix Order
1. Keep `docs/reference/cli_runbook.md` as the SSOT for launcher CLI usage.
2. Re-run wrapper and command-parity checks when launcher commands, adapter options, or record-output flags change.
3. Re-run scoped path and evidence-link validation whenever a new interaction showcase doc or review script is added.
4. Keep interaction docs and generated path artifacts explicit about whether negative branches are GAS-native or showcase-local.

## Residual Risks
- The C1 scenario deliberately validates `InvalidTarget` and `OutOfRange` in showcase autoplay before queue submission, so the current evidence does not certify GAS-native `CastFailed` emission for those two branches.
- The C2 scenario deliberately validates hostile-target and dead-ally rejection in showcase autoplay before queue submission, so the current evidence does not certify GAS-native `CastFailed` emission or native dead-target filtering for those two branches.
- The visual evidence is deterministic synthetic capture; future overlay layout changes should still be re-reviewed against `visible-checklist.md` and the generated `timeline.png`.
- Web launcher correctness is documented, but browser performance still depends on the current snapshot transport; see `artifacts/techdebt/2026-03-12-web-ui-snapshot-pipeline.md`.
