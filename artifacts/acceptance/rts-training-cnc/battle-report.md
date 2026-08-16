# Scenario Card: rts-training-cnc

## Intent
- Player goal: inspect War Factory training with readable queue/progress.
- Gameplay domain: rts_cnc_training.

## Determinism Inputs
- Seed: fixed-step deterministic simulation at 60 FPS.
- Map: `rts_cnc_training`.
- Clock profile: `FixedFrame`.

## Action Script
1. Auto-select the producer building.
2. Queue 1 slot-2 training orders.
3. Observe progress, readable queue items, and mid-progress resource movement.
4. Let the queue finish and verify spawned unit count plus ending resources.

## Expected Outcomes
- Primary success condition: progress/status and queue rows stay readable throughout training.
- Failure branch condition: queue labels collapse to `Cast Ability`, progress never starts, or resource movement mismatches the style.
- Key metrics: start Credits=2800, end Credits=1900, avg frame ms=0.338.

## Timeline
- [T+001] War Factory is selected by default and ready to train.
- [T+002] 1 orders are visible as readable queue rows, not generic cast placeholders.
- [T+003] Mid-progress resource movement matches the intended Credits pacing.
- [T+004] Queue completes with 1 Rhino Tank spawns and the expected final Credits total.

## Evidence Artifacts
- `artifacts/acceptance/rts-training-cnc/trace.jsonl`
- `artifacts/acceptance/rts-training-cnc/panel-trace.jsonl`
- `artifacts/acceptance/rts-training-cnc/battle-report.md`
- `artifacts/acceptance/rts-training-cnc/path.mmd`
- `artifacts/acceptance/rts-training-cnc/screens/*_ui.png`
- `artifacts/acceptance/rts-training-cnc/screens/*.svg`
