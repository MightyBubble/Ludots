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
- [T+006] baseline_hotpath_defaults | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.98ms | HudProj=1.18ms | OverlayBuild=9.69ms | OverlayDraw=147.25ms | Dirty=2 | Rebuilt=0
- [T+007] steady_state_same_view | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=2.11ms | HudProj=1.13ms | OverlayBuild=8.02ms | OverlayDraw=122.00ms | Dirty=0 | Rebuilt=0
- [T+009] diag_hud_off | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=OFF | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.71ms | HudProj=1.10ms | OverlayBuild=6.58ms | OverlayDraw=101.34ms | Dirty=0 | Rebuilt=0
- [T+013] selection_labels_off | Crowd=10240/3136 | Bars=3136->1303 | Text=3136->1317 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.92ms | HudProj=1.05ms | OverlayBuild=5.70ms | OverlayDraw=84.15ms | Dirty=1 | Rebuilt=0
- [T+015] bars_off | Crowd=10240/3136 | Bars=0->0 | Text=3136->1317 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=1.99ms | HudProj=1.10ms | OverlayBuild=4.73ms | OverlayDraw=69.78ms | Dirty=1 | Rebuilt=0
- [T+017] hud_text_off | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=2.08ms | HudProj=0.74ms | OverlayBuild=3.91ms | OverlayDraw=57.28ms | Dirty=1 | Rebuilt=0
- [T+019] terrain_on | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=OFF | Prims=ON | Cull=1.89ms | HudProj=0.50ms | OverlayBuild=3.21ms | OverlayDraw=47.03ms | Dirty=0 | Rebuilt=0
- [T+021] guides_on | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=ON | Cull=2.03ms | HudProj=0.34ms | OverlayBuild=2.63ms | OverlayDraw=38.64ms | Dirty=0 | Rebuilt=0
- [T+023] primitives_off | Crowd=10240/3136 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=OFF | Cull=1.88ms | HudProj=0.23ms | OverlayBuild=2.16ms | OverlayDraw=31.74ms | Dirty=0 | Rebuilt=0
- [T+027] cull_crowd_off | Crowd=0/0 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=ON | HUD=ON | Terr=ON | Guides=ON | Prims=OFF | Cull=0.86ms | HudProj=0.06ms | OverlayBuild=1.77ms | OverlayDraw=26.09ms | Dirty=0 | Rebuilt=0
- [T+029] panel_off | Crowd=0/0 | Bars=0->0 | Text=0->0 | Labels=0 | Panel=OFF | HUD=OFF | Terr=ON | Guides=ON | Prims=OFF | Cull=0.63ms | HudProj=0.04ms | OverlayBuild=1.45ms | OverlayDraw=21.45ms | Dirty=0 | Rebuilt=0
- [T+049] restored_hotpath_defaults | Crowd=10240/3136 | Bars=3136->1303 | Text=3152->1333 | Labels=16 | Panel=ON | HUD=ON | Terr=OFF | Guides=OFF | Prims=ON | Cull=2.73ms | HudProj=1.22ms | OverlayBuild=1.70ms | OverlayDraw=20.69ms | Dirty=2 | Rebuilt=0

## Outcome
- success: yes
- verdict: shared presentation hotpath harness keeps a 10k+ crowd, prints visible entities in the panel, and toggles diagnostics/bars/HUD text/terrain/guides/primitives/culling gates without changing the underlying scene contract.
- reason: baseline crowd `10240` and restored crowd `10240` match, the panel keeps a visible-entity window alive, the hotpath defaults keep terrain/guides disabled for pan verification, and toggle snapshots show bars/text/labels/crowd collapsing to zero exactly when their lane is disabled.

## Summary Stats
- snapshot count: `12`
- baseline visible crowd: `3136`
- baseline world bars/text: `3136` / `3152`
- restored world bars/text: `3136` / `3152`
- max culling sample: `2.73` ms
- max HUD projection sample: `1.22` ms
- max native overlay build sample: `9.69` ms
- max native overlay draw sample: `147.25` ms
- baseline/reused dirty lanes: `2` -> `0`
- reusable wiring: `CameraAcceptanceHotpathLaneSystem`, `CameraAcceptancePanelController`, `CameraAcceptanceSelectionOverlaySystem`, `WorldHudToScreenSystem`, `PresentationTimingDiagnostics`
