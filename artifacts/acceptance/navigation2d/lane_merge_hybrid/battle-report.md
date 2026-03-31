# Scenario Card: navigation2d-lane-merge-hybrid

## Intent
- Player goal: verify the launcher-started Navigation2D playground actually decongests over time instead of timing out as a stationary knot in the center.
- Gameplay domain: real launcher bootstrap, real adapter camera and culling services, real Navigation2D playground scenario state.

## Determinism Inputs
- Seed: none
- Map: `mods/Navigation2DPlaygroundMod/assets/Maps/nav2d_playground.json`
- Adapter: `raylib`
- Launch command: `.\scripts\run-mod-launcher.cmd cli launch nav_playground --adapter raylib --record artifacts/acceptance/navigation2d/lane_merge_hybrid`
- Scenario: `Lane Merge`
- Agents per team: `64`
- Clock profile: fixed `1/60s`, timeout tick `720`
- Evidence images: `screens/000_start.png`, `screens/120_t120.png`, `screens/240_t240.png`, `screens/360_t360.png`, `screens/480_t480.png`, `screens/600_t600.png`, `screens/720_t720.png`, `screens/timeline.png`

## Action Script
1. Boot the real playable Navigation2D playground through the unified launcher bootstrap.
2. Force the `Lane Merge` scenario and deterministic agent count through the existing playground state.
3. Simulate until timeout while sampling crowd progress every 30 ticks and capturing timeline frames every 120 ticks.
4. Fail if timeout still looks like a dense stationary center jam.

## Expected Outcomes
- Primary success condition: both teams measurably advance through the conflict zone and timeout no longer shows a dense stationary center jam.
- Failure branch condition: timeout arrives with weak median progress, excessive center occupancy, or too many stationary agents trapped in the center box.
- Key metrics: team median X progress, center occupancy, stopped center agents, moving agent count, crossed fractions.

## Timeline
- [T+000] 000_start | MedianX T0=-9220 T1=-9220 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=0 | Tick=5.194ms
- [T+120] 120_t120 | MedianX T0=-7428 T1=-7427 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=126 | Tick=0.520ms
- [T+240] 240_t240 | MedianX T0=-5793 T1=-5704 | DirectionalCross T0=0% T1=0% | Center=0 move=0 stop=0 | Moving=125 | Tick=0.089ms
- [T+360] 360_t360 | MedianX T0=-3981 T1=-3658 | DirectionalCross T0=0% T1=0% | Center=13 move=13 stop=0 | Moving=126 | Tick=0.125ms
- [T+480] 480_t480 | MedianX T0=-1501 T1=-1133 | DirectionalCross T0=31% T1=28% | Center=39 move=39 stop=0 | Moving=128 | Tick=0.068ms
- [T+600] 600_t600 | MedianX T0=1324 T1=1425 | DirectionalCross T0=75% T1=77% | Center=61 move=60 stop=1 | Moving=127 | Tick=0.077ms
- [T+720] 720_t720 | MedianX T0=4057 T1=4333 | DirectionalCross T0=100% T1=100% | Center=0 move=0 stop=0 | Moving=128 | Tick=0.260ms

## Outcome
- success: yes
- verdict: Timed avoidance passes: median advance is 13277/13553cm and timeout center occupancy is 0/128 with 0 stationary.
- reason: median advance reached `13277` / `13553` cm; timeout center box held `0` of `128` agents with `0` stationary; peak center occupancy was `61` at tick `600`.

## Summary Stats
- trace samples: `25`
- screenshot captures: `7`
- median headless tick: `1.804ms`
- max headless tick: `390.266ms`
- normalized signature: `navigation2d-lane-merge-hybrid|mid:5239/5562|final:13277/13553|center:0/128|stopped:0|peak:61@600`
- reusable wiring: `launcher.runtime.json`, `Navigation2DPlaygroundState`, `Navigation2DRuntime`, `ScreenOverlayBuffer`, `PlayerInputHandler`
