# Scenario Card: navigation2d-playground-playable

## Intent
- Player goal: open the real Navigation2D playground, box-select a crowd slice, right-click move it, spawn more units and blockers, and swap between RTS and follow cameras.
- Gameplay domain: `Navigation2DPlaygroundMod` through `CoreInputMod`, `UIRoot` + `ReactivePage`, virtual camera view modes, and real Navigation2D simulation.

## Determinism Inputs
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Mods: `LudotsCoreMod`, `CoreInputMod`, `Navigation2DPlaygroundMod`
- Scenario baseline: `Pass Through`, `64` agents per team, spawn batch `16`
- Clock profile: fixed `1/60s`, headless `GameEngine.Tick()` loop.
- Input source: `PlayerInputHandler` backed by a deterministic mouse/keyboard test backend.
- Screen mapping: deterministic world/screen transform used by both `IScreenProjector` and `IScreenRayProvider`.

## Action Script
1. Boot the real engine with `CoreInputMod` and mount the playground panel into `UIRoot`.
2. Drag-select the full team-0 spawn block through the shared CoreInput selection system.
3. Right-click a new ground point in move mode and verify all selected `NavGoal2D` targets move with formation offsets.
4. Switch tools with keyboard hotkeys, then right-click spawn team-0 agents and blockers.
5. Press `F2` to enter follow view mode, then `F1` back to RTS command mode.

## Expected Outcomes
- Primary success condition: panel, selection, command, spawn, and camera mode switching all execute through the real runtime pipeline.
- Failure branch condition: UI remounts instead of staying reactive, selection does not fill the shared `SelectionBuffer`, right-click does not update `NavGoal2D`, or spawn counts do not change.
- Key metrics: selected entity count, live agents, blocker count, active mode id, active camera id, primary selected goal, headless tick cost.

## Evidence Artifacts
- `artifacts/acceptance/navigation2d-playground-playable/trace.jsonl`
- `artifacts/acceptance/navigation2d-playground-playable/battle-report.md`
- `artifacts/acceptance/navigation2d-playground-playable/path.mmd`

## Timeline
- [T+001] warmup | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=Move | Selected=0 | Live=128 | Blockers=0 | Goal=(n/a,n/a) | Tick=4.908ms
- [T+002] select_team0 | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=Move | Selected=64 | Live=128 | Blockers=0 | Goal=(9000,-420) | Tick=4.195ms
- [T+003] move_selected | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=Move | Selected=64 | Live=128 | Blockers=0 | Goal=(-2890,-490) | Tick=185.239ms
- [T+004] spawn_team0 | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=SpawnTeam0 | Selected=64 | Live=144 | Blockers=0 | Goal=(-2890,-490) | Tick=0.021ms
- [T+005] spawn_blocker | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=SpawnBlocker | Selected=64 | Live=144 | Blockers=16 | Goal=(-2890,-490) | Tick=0.102ms
- [T+006] follow_mode | Mode=Navigation2D.Playground.Mode.Follow | Camera=Navigation2D.Playground.Camera.Follow | Tool=SpawnBlocker | Selected=64 | Live=144 | Blockers=16 | Goal=(-2890,-490) | Tick=0.091ms
- [T+007] command_mode | Mode=Navigation2D.Playground.Mode.Command | Camera=Navigation2D.Playground.Camera.Command | Tool=SpawnBlocker | Selected=64 | Live=144 | Blockers=16 | Goal=(-2890,-490) | Tick=181.419ms

## Outcome
- success: yes
- verdict: the playground now exposes a formal playable path with mounted reactive UI, CoreInput box selection, right-click move/spawn tools, and view-mode driven camera switching.
- reason: final state returned to `Navigation2D.Playground.Mode.Command` with `64` selected entities preserved, `144` live agents, `16` blockers, and median tick `0.545ms`.

## Summary Stats
- snapshots captured: `7`
- median headless tick: `0.545ms`
- max headless tick: `416.060ms`
- final active camera: `Navigation2D.Playground.Camera.Command`
- final selected ids sample: `#5, #7, #9, #11`
- reusable wiring: `ConfigPipeline`, `CoreInputMod`, `UIRoot`, `ReactivePage`, `ViewModeManager`, `Navigation2DRuntime`

## Open Tech Debt
- `nav-playground-selection-boundaries` -> `artifacts/techdebt/2026-03-12-nav-playground-selection-boundaries.md`
- `nav-playground-steering-60hz-gap` -> `artifacts/techdebt/2026-03-12-nav-playground-steering-60hz-gap.md`
