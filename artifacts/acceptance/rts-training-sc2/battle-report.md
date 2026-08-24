# Scenario Card: rts-training-sc2

## Intent
- Player goal: inspect Gateway training with readable queue/progress.
- Gameplay domain: rts_sc2_training.

## Determinism Inputs
- Seed: fixed-step deterministic simulation at 60 FPS.
- Map: `rts_sc2_training`.
- Clock profile: `FixedFrame`.

## Action Script
1. Auto-select the producer building.
2. Queue 1 slot-2 training orders.
3. Observe progress, readable queue items, and mid-progress resource movement.
4. Let the queue finish and verify spawned unit count plus ending resources.

## Expected Outcomes
- Primary success condition: progress/status and queue rows stay readable throughout training.
- Failure branch condition: queue labels collapse to `Cast Ability`, progress never starts, or resource movement mismatches the style.
- Key metrics: start Minerals=800, end Minerals=700, avg frame ms=0.318.

## Timeline
- [T+001] Gateway is selected by default and ready to train.
- [T+002] 1 orders are visible as readable queue rows, not generic cast placeholders.
- [T+003] Mid-progress resource movement matches the intended Minerals pacing.
- [T+004] Queue completes with 1 Zealot spawns and the expected final Minerals total.

## Evidence Artifacts
- `artifacts/acceptance/rts-training-sc2/trace.jsonl`
- `artifacts/acceptance/rts-training-sc2/panel-trace.jsonl`
- `artifacts/acceptance/rts-training-sc2/battle-report.md`
- `artifacts/acceptance/rts-training-sc2/path.mmd`
- `artifacts/acceptance/rts-training-sc2/screens/*_ui.png`
- `artifacts/acceptance/rts-training-sc2/screens/*.svg`
