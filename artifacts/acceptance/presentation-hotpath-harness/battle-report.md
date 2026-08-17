# Scenario Card: presentation-hotpath-harness

## Intent
- Player goal: move the local avatar across a 10k+ hotpath crowd, read the live visible-entity panel while the virtual camera follows, and isolate presentation lanes without leaving the shared acceptance scene.
- Gameplay domain: CameraAcceptanceMod diagnostics / visible-entity panel / HUD bar / HUD text / selection label / primitive / culling crowd lanes.

## Determinism Inputs
- Seed: none
- Map: `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_hotpath.json`
- Crowd: `10240` deterministic Dummy entities from the runtime spawn queue.
- Clock profile: fixed `1/60s` headless acceptance ticks with explicit `WorldHudToScreenSystem` projection.
- Controls: WASD local-avatar movement plus `F6 panel`, `F7 diagnostics HUD`, `F8 selection labels`, `F9 bars`, `F10 HUD text`, `F11 terrain`, `G guides`, `F12 primitives`, `C crowd`.

## Action Script
1. Load the shared hotpath map and wait for the deterministic crowd to spawn.
2. Verify the panel exposes the current visible entities for the active camera view.
3. Capture the baseline with all presentation lanes enabled.
4. Toggle diagnostics HUD, selection labels, bars, HUD text, terrain, guides, primitives, crowd, and panel one by one.
5. Restore the hotpath defaults and verify the same scene returns to the baseline shape.

## Expected Outcomes
- Primary success condition: the panel prints the visible entities for the current camera view, and each live toggle changes only its target lane or render gate while the rest of the scene remains stable.
- Failure branch condition: visible-entity panel stays stale after view changes, bars/text/selection survive after their toggle, terrain/guides gates fail to flip, panel fails to unmount/remount, or crowd removal does not collapse the culling workload inputs.
- Key metrics: crowd count, visible crowd count, world/screen HUD item counts, selection-label count, HUD buffer drops, culling timing, HUD projection timing, native overlay build/draw timing, dirty-lane count, and rebuilt-lane count.

## Evidence Artifacts
- `artifacts/acceptance/presentation-hotpath-harness/trace.jsonl`
- `artifacts/acceptance/presentation-hotpath-harness/battle-report.md`
- `artifacts/acceptance/presentation-hotpath-harness/path.mmd`

## Timeline
- [T+006] baseline_hotpath_defaults | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.49ms | HudProj=1.10ms | OverlayBuild=7.94ms | OverlayDraw=129.88ms | Dirty=2 | Rebuilt=0
- [T+007] steady_state_same_view | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.40ms | HudProj=0.99ms | OverlayBuild=6.61ms | OverlayDraw=107.64ms | Dirty=0 | Rebuilt=0
- [T+009] diag_hud_off | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=OFF | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.67ms | HudProj=0.97ms | OverlayBuild=5.42ms | OverlayDraw=89.64ms | Dirty=0 | Rebuilt=0
- [T+013] selection_labels_off | Crowd=10240/3136 | Bars=3136->1303 | Text=3136->1317 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.45ms | HudProj=0.88ms | OverlayBuild=4.84ms | OverlayDraw=74.78ms | Dirty=1 | Rebuilt=0
- [T+015] bars_off | Crowd=10240/3136 | Bars=0->0 | Text=3136->1317 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.86ms | HudProj=1.06ms | OverlayBuild=4.03ms | OverlayDraw=62.12ms | Dirty=1 | Rebuilt=0
- [T+017] hud_text_off | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.57ms | HudProj=0.72ms | OverlayBuild=3.34ms | OverlayDraw=51.00ms | Dirty=1 | Rebuilt=0
- [T+019] terrain_on | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=OFF | Prims=ON | Cull=1.52ms | HudProj=0.48ms | OverlayBuild=2.74ms | OverlayDraw=41.88ms | Dirty=0 | Rebuilt=0
- [T+021] guides_on | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=ON | Cull=1.55ms | HudProj=0.33ms | OverlayBuild=2.25ms | OverlayDraw=34.40ms | Dirty=0 | Rebuilt=0
- [T+023] primitives_off | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=OFF | Cull=1.46ms | HudProj=0.22ms | OverlayBuild=1.84ms | OverlayDraw=28.27ms | Dirty=0 | Rebuilt=0
- [T+027] cull_crowd_off | Crowd=0/0 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=OFF | Cull=0.65ms | HudProj=0.05ms | OverlayBuild=1.51ms | OverlayDraw=23.24ms | Dirty=0 | Rebuilt=0
- [T+029] panel_off | Crowd=0/0 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=OFF | HUD=OFF | Terr=ON | Guides=ON | Prims=OFF | Cull=0.53ms | HudProj=0.03ms | OverlayBuild=1.24ms | OverlayDraw=19.11ms | Dirty=0 | Rebuilt=0
- [T+049] restored_hotpath_defaults | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=2.41ms | HudProj=0.95ms | OverlayBuild=1.38ms | OverlayDraw=17.94ms | Dirty=2 | Rebuilt=0

## Outcome
- success: yes
- verdict: shared presentation hotpath harness keeps a 10k+ crowd, prints visible entities in the panel, and toggles diagnostics/bars/HUD text/terrain/guides/primitives/culling gates without changing the underlying scene contract.
- reason: baseline crowd `10240` and restored crowd `10240` match, the panel keeps a visible-entity window alive, the hotpath defaults keep terrain/guides disabled for pan verification, and toggle snapshots show bars/text/labels/crowd collapsing to zero exactly when their lane is disabled.

## Summary Stats
- snapshot count: `12`
- baseline visible crowd: `3136`
- baseline world bars/text: `3136` / `3152`
- restored world bars/text: `3136` / `3152`
- max culling sample: `2.41` ms
- max HUD projection sample: `1.10` ms
- max native overlay build sample: `7.94` ms
- max native overlay draw sample: `129.88` ms
- baseline/reused dirty lanes: `2` -> `0`
- reusable wiring: `CameraAcceptanceHotpathLaneSystem`, `CameraAcceptancePanelController`, `CameraAcceptanceSelectionOverlaySystem`, `WorldHudToScreenSystem`, `PresentationTimingDiagnostics`
