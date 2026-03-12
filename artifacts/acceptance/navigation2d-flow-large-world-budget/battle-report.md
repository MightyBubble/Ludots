# Scenario Card: navigation2d-flow-large-world-budget

## Intent
- Player goal: keep flowfield propagation smooth in large worlds by using explicit activation-window and world-bound budgets.
- Gameplay domain: `Navigation2D` flow streaming, large-world propagation, and runtime telemetry.

## Determinism Inputs
- Seed: none
- Runtime: `Navigation2DRuntime` + `Navigation2DSteeringSystem2D`
- Loaded tiles: corridor 0..6 plus one out-of-budget tile 20
- Config source: `Navigation2D.FlowStreaming` explicit world/window budget fields
- Clock profile: fixed `1/60s`, two steering updates

## Expected Outcomes
- Flow activation stays inside the explicit world bounds.
- Window work stays bounded by explicit width/height budget.
- Demand beyond bounds clamps instead of forcing a huge propagation rebuild domain.

## Timeline
- tick 1: inside the configured world bounds, the corridor around the goal stays active and far tile 20 remains inactive.
- tick 2: moving demand beyond the explicit world bounds clamps the activation window and keeps tile 6 outside the active set.

## Outcome
- success: yes
- verdict: large-world flowfield activation now honors explicit budget config and exposes enough telemetry for playable verification.
