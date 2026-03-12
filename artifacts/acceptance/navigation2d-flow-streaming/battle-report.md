# Scenario Card: navigation2d-flow-streaming

## Intent
- Player goal: keep flow propagation bounded to active demand windows instead of mirroring all loaded chunks into every flow.
- Gameplay domain: `Navigation2D` runtime flow streaming, steering integration, and explicit config pipeline.

## Determinism Inputs
- Seed: none
- Runtime: `Navigation2DRuntime` + `Navigation2DSteeringSystem2D`
- Loaded tiles: corridor 0..5 plus one far tile 10
- Config source: `Navigation2D.FlowStreaming`
- Clock profile: fixed `1/60s`, three steering updates

## Expected Outcomes
- Only tiles inside the goal-to-demand window become active.
- Far loaded tiles stay inactive.
- Unloaded tiles leave the active set without hidden fallback.

## Timeline
- tick 1: demand at tile 3 activates the goal-to-demand corridor and leaves far tile 10 inactive.
- tick 2: moving demand forward keeps flow output non-zero and active tiles bounded.
- tick 3: unloading tile 4 removes it from the active set on the next steering update.

## Outcome
- success: yes
- verdict: flow demand collection, active-window streaming, and chunk-unload cleanup are wired end to end.
