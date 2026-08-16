# Scenario Card: rts-training-war3

## Intent
- Player goal: inspect Barracks training with readable queue/progress.
- Gameplay domain: rts_war3_training.

## Determinism Inputs
- Seed: fixed-step deterministic simulation at 60 FPS.
- Map: `rts_war3_training`.
- Clock profile: `FixedFrame`.

## Action Script
1. Auto-select the producer building.
2. Queue 1 slot-2 training orders.
3. Observe progress, readable queue items, and mid-progress resource movement.
4. Let the queue finish and verify spawned unit count plus ending resources.

## Expected Outcomes
- Primary success condition: progress/status and queue rows stay readable throughout training.
- Failure branch condition: queue labels collapse to `Cast Ability`, progress never starts, or resource movement mismatches the style.
- Key metrics: start Minerals=700, end Minerals=565, avg frame ms=0.265.

## Timeline
- [T+001] Barracks is selected by default and ready to train.
- [T+002] 1 orders are visible as readable queue rows, not generic cast placeholders.
- [T+003] Mid-progress resource movement matches the intended Minerals pacing.
- [T+004] Queue completes with 1 Footman spawns and the expected final Minerals total.

## Evidence Artifacts
- `artifacts/acceptance/rts-training-war3/trace.jsonl`
- `artifacts/acceptance/rts-training-war3/panel-trace.jsonl`
- `artifacts/acceptance/rts-training-war3/battle-report.md`
- `artifacts/acceptance/rts-training-war3/path.mmd`
- `artifacts/acceptance/rts-training-war3/screens/*_ui.png`
- `artifacts/acceptance/rts-training-war3/screens/*.svg`
