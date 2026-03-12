# Scenario Card: navigation2d-playground-scenarios

## Intent
- Player goal: switch multiple Navigation2D avoidance scenarios from the playable mod without hidden constants.
- Gameplay domain: `Navigation2DPlaygroundMod` scenario catalog, explicit config pipeline, blocker-aware presentation, and reusable input/UI wiring.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Clock profile: fixed `1/60s`, `4` steering/integration steps per scenario
- Initial entities: `96` agents per team, `6` configured scenarios
- Config source: `game.json -> ConfigPipeline.MergeGameConfig() -> GameConfig.Navigation2D.Playground`
- Input source: `mods/Navigation2DPlaygroundMod/assets/Input/default_input.json`
- UI source: `UIRoot` + `ReactivePage`, with `ScreenOverlayBuffer` retained for telemetry evidence.

## Action Script
1. Validate `Navigation2D.Playground` catalog and input/map assets.
2. Spawn each configured scenario through `Navigation2DPlaygroundScenarioSpawner`.
3. Run steering and integration for four deterministic ticks.
4. Record blocker counts, moving desired-velocity agents, and sampled average speed.

## Expected Outcomes
- Primary success condition: every scenario spawns correctly and produces measurable movement.
- Failure branch condition: scenario index/catalog wiring is invalid, or blocker scenarios spawn without blockers.
- Key metrics: dynamic agent count, blocker count, moving desired-velocity agents, average sampled speed.

## Evidence Artifacts
- `artifacts/acceptance/navigation2d-playground-scenarios/trace.jsonl`
- `artifacts/acceptance/navigation2d-playground-scenarios/battle-report.md`
- `artifacts/acceptance/navigation2d-playground-scenarios/path.mmd`

## Timeline
- [T+001] Scenario#1 Pass Through [pass_through] | Teams=2 | Dynamic=192 | Blockers=0 | MovingDesired=192 | AvgSpeed=160.3cm/s
- [T+002] Scenario#2 Orthogonal Cross [orthogonal_cross] | Teams=2 | Dynamic=192 | Blockers=0 | MovingDesired=192 | AvgSpeed=161.3cm/s
- [T+003] Scenario#3 Bottleneck [bottleneck] | Teams=2 | Dynamic=192 | Blockers=20 | MovingDesired=192 | AvgSpeed=102.2cm/s
- [T+004] Scenario#4 Lane Merge [lane_merge] | Teams=2 | Dynamic=192 | Blockers=0 | MovingDesired=192 | AvgSpeed=162.9cm/s
- [T+005] Scenario#5 Circle Swap [circle_swap] | Teams=2 | Dynamic=192 | Blockers=0 | MovingDesired=179 | AvgSpeed=292.5cm/s
- [T+006] Scenario#6 Goal Queue [goal_queue] | Teams=1 | Dynamic=96 | Blockers=18 | MovingDesired=80 | AvgSpeed=252.4cm/s

## Outcome
- success: yes
- verdict: scenario switching, blocker visualization, and explicit playground config are wired into the existing mod/input/UI pipeline.
- reason: every configured scenario spawned, blocker scenarios contained blockers, and all scenarios produced non-zero desired motion plus non-zero sampled speed.

## Summary Stats
- scenario count: `6`
- agents per team in acceptance run: `96`
- total dynamic agents exercised across catalog: `1056`
- total blockers exercised across catalog: `38`
- reusable wiring: config via `Navigation2D.Playground`, input via `default_input.json`, view modes via `viewmodes.json`, camera via virtual camera registry, telemetry via `ScreenOverlayBuffer`
