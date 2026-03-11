# Scenario Card: camera-acceptance-projection-click

## Intent
- Player goal: verify a launcher-started camera acceptance slice can click ground through the selected adapter, spawn a Dummy at the raycast point, and show a transient cue marker that expires cleanly.
- Gameplay domain: real launcher bootstrap, real adapter projection/raycast wiring, real `CameraAcceptanceMod` projection scenario.

## Determinism Inputs
- Seed: none
- Map: `mods/fixtures/camera/CameraAcceptanceMod/assets/Maps/camera_acceptance_projection.json`
- Adapter: `web`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web --record artifacts/acceptance/launcher-camera-acceptance-web`
- Click target: `3200,2000`
- Clock profile: fixed `1/60s`
- Evidence images: `screens/000_start.png`, `screens/001_after_click.png`, `screens/002_marker_live.png`, `screens/003_marker_expired.png`, `screens/timeline.png`

## Action Script
1. Boot the unified launcher runtime bootstrap for CameraAcceptanceMod.
2. Let the adapter camera and projector settle on the projection map.
3. Project the target world point into screen space with the selected adapter and inject a left click.
4. Capture start, first cue-visible post-click, marker-live, and marker-expired frames.

## Expected Outcomes
- Primary success condition: exactly one Dummy is added at the click target and the first post-click cue-visible frame appears consistently.
- Failure branch condition: click lands on the wrong point, no Dummy appears, or the cue marker lifetime is broken.
- Key metrics: Dummy count delta, spawned world position, cue marker visibility over time, active camera id.

## Timeline
- [T+001] CameraAcceptance.000_start -> map=camera_acceptance_projection camera=Camera.Acceptance.Profile.RtsMoba | Dummy=0 | Cue=Off | Target=-189,-189 | Tick=3.328ms
- [T+002] CameraAcceptance.001_after_click -> map=camera_acceptance_projection camera=Camera.Acceptance.Profile.RtsMoba | Dummy=0 | Cue=On | Target=-189,-189 | Tick=7.873ms
- [T+017] CameraAcceptance.002_marker_live -> map=camera_acceptance_projection camera=Camera.Acceptance.Profile.RtsMoba | Dummy=1 | Cue=On | Target=-189,-189 | Tick=0.305ms
- [T+018] CameraAcceptance.003_marker_expired -> map=camera_acceptance_projection camera=Camera.Acceptance.Profile.RtsMoba | Dummy=1 | Cue=Off | Target=-189,-189 | Tick=0.219ms

## Outcome
- success: yes
- verdict: Projection click passes: Dummy count is 0->0, cue marker lives across the mid capture, and expires by tick 18.
- reason: Dummy count moved `0` -> `1`, spawned at `3200,2000`, cue visibility sequence `110`.

## Summary Stats
- screenshot captures: `4`
- median headless tick: `0.304ms`
- max headless tick: `22.758ms`
- active camera at click: `Camera.Acceptance.Profile.RtsMoba`
- normalized signature: `camera_acceptance_projection_click|dummy:0->1|spawn:3200,2000|cue:110|camera:-189,-189`
- final camera target: `-189,-189`
- reusable wiring: `launcher.runtime.json`, `GameBootstrapper`, `CoreScreenProjector`, `IScreenRayProvider`, `PlayerInputHandler`
