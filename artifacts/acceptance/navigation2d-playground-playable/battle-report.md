# Scenario Card: navigation2d-playground-playable

## Intent
- Player goal: launch the Navigation2D playground, switch scenarios, and inspect steering/flow/debug overlays without custom test-only runtime paths.
- Gameplay domain: Navigation2D playground mod, input pipeline, HUD overlay, and debug draw integration.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Mods: `LudotsCoreMod`, `Navigation2DPlaygroundMod`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: `InputConfigPipelineLoader` + `PlayerInputHandler.InjectButtonPress()`.

## Action Script
1. Boot the real `GameEngine` with the playground mod through the config pipeline.
2. Warm the entry scene and validate HUD/debug services.
3. Inject `NextScenario`, `IncreaseAgentsPerTeam`, `ToggleFlowDebug`, `CycleFlowDebugMode`, and `ToggleFlowEnabled`.
4. Record scenario services, overlay text, cache counters, flow debug lines, and wall-clock tick cost.

## Expected Outcomes
- Primary success condition: the playground stays interactive through the normal mod/input/UI pipeline.
- Failure branch condition: scenario switching, overlay rendering, or flow debug toggles do not update runtime state.
- Key metrics: scenario index/name, agents per team, flow debug lines, steering cache counters, median tick wall time.

## Evidence Artifacts
- `artifacts/acceptance/navigation2d-playground-playable/trace.jsonl`
- `artifacts/acceptance/navigation2d-playground-playable/battle-report.md`
- `artifacts/acceptance/navigation2d-playground-playable/path.mmd`

## Timeline
- [T+001] warmup | Scenario=1:Pass Through | Agents/team=5000 | Live=10000 | Blockers=0 | Flow=True/False/Mode0 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=165.353ms
- [T+002] next_scenario | Scenario=2:Orthogonal Cross | Agents/team=5000 | Live=10000 | Blockers=0 | Flow=True/False/Mode0 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=135.162ms
- [T+003] increase_agents | Scenario=2:Orthogonal Cross | Agents/team=5500 | Live=11000 | Blockers=0 | Flow=True/False/Mode0 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=12.938ms
- [T+004] toggle_flow_debug | Scenario=2:Orthogonal Cross | Agents/team=5500 | Live=11000 | Blockers=0 | Flow=True/True/Mode0 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=8.453ms
- [T+005] cycle_flow_mode | Scenario=2:Orthogonal Cross | Agents/team=5500 | Live=11000 | Blockers=0 | Flow=True/True/Mode1 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=9.539ms
- [T+006] toggle_flow_enabled | Scenario=2:Orthogonal Cross | Agents/team=5500 | Live=11000 | Blockers=0 | Flow=False/True/Mode1 | FlowDbgLines=0 | CacheHitRate=0.0% | Tick=6.524ms

## Outcome
- success: yes
- verdict: the playable Navigation2D mod is wired through the unified config, input, HUD, and debug draw pipeline.
- reason: final state reached scenario `Orthogonal Cross` with flow enabled=`False`, flow debug lines=`0`, and median headless tick cost `10.763ms`.

## Summary Stats
- snapshots captured: `6`
- median headless tick: `10.763ms`
- max headless tick: `165.353ms`
- final agents per team: `5500`
- final live agents: `11000`
- final flow debug lines: `0`
- reusable wiring: `ConfigPipeline`, `PlayerInputHandler`, `ScreenOverlayBuffer`, `DebugDrawCommandBuffer`, `Navigation2DRuntime`
